using System.Globalization;
using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Turns a profile of the shared AWS files into the provider of its credentials, in the order of botocore: <c>AssumeRoleProvider</c> first, then
/// the providers of <c>ProfileProviderBuilder</c>, which are web identity, SSO, the static keys of the credentials file, login,
/// <c>credential_process</c> and the static keys of the config file. One instance resolves one profile, with the profiles it sources from.
/// </summary>
/// <remarks>
/// A role takes its source credentials from <c>source_profile</c> or <c>credential_source</c>. A source profile with static keys, or without
/// <c>role_arn</c>, resolves through the providers of <c>ProfileProviderBuilder</c>, so its own <c>role_arn</c> is ignored when it has keys; one with
/// <c>role_arn</c> and no keys assumes its role in turn. That makes a chain of roles, and a profile that comes back is a loop, except a profile that
/// names itself and has static keys: it assumes its role with its own keys. The checks and the messages follow botocore's
/// <c>_get_role_config</c> and <c>_validate_source_profile</c>.
/// </remarks>
internal sealed class ProfileChain
{
    private const string AccessKeyIdOption = "aws_access_key_id";
    private const string SecretAccessKeyOption = "aws_secret_access_key";
    private const string SessionTokenOption = "aws_session_token";
    private const string SecurityTokenOption = "aws_security_token";
    private const string RoleArnOption = "role_arn";
    private const string SourceProfileOption = "source_profile";
    private const string CredentialSourceOption = "credential_source";
    private const string WebIdentityTokenFileOption = "web_identity_token_file";
    private const string SessionNameOption = "role_session_name";
    private const string DurationOption = "duration_seconds";
    private const string ProcessOption = "credential_process";
    private const string SsoSessionOption = "sso_session";

    private static readonly string[] RegionVariables = ["AWS_REGION", "AWS_DEFAULT_REGION"];

    private readonly SharedConfigFiles _files;
    private readonly CredentialsDependencies _dependencies;
    private readonly AwsRegion? _clientRegion;
    private readonly List<string> _visited = [];
    private AwsRegion? _stsRegion;
    private bool _stsRegionResolved;

    /// <summary>Initializes a new instance of the <see cref="ProfileChain"/> class.</summary>
    /// <param name="files">The shared files.</param>
    /// <param name="dependencies">What the providers run with.</param>
    /// <param name="clientRegion">The region of the client that made the provider, for STS; <see langword="null"/> to look for it.</param>
    public ProfileChain(SharedConfigFiles files, CredentialsDependencies dependencies, AwsRegion? clientRegion)
    {
        _files = files;
        _dependencies = dependencies;
        _clientRegion = clientRegion;
    }

    /// <summary>Resolves a profile.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Where the profile gets its credentials, or <see langword="null"/> if it has nothing that gives any.</returns>
    /// <exception cref="CredentialsUnavailableException">The profile, or one it sources from, is configured wrongly. The message names the profile and the setting.</exception>
    public ProfileSource? Resolve(SharedProfile profile)
    {
        _visited.Add(profile.Name);
        return HasAssumeRoleSettings(profile) ? AssumeRole(profile) : ResolveOwn(profile);
    }

    // botocore's _has_assume_role_config_vars: a role, and not one for web identity, which the next provider takes.
    private static bool HasAssumeRoleSettings(SharedProfile profile) =>
        profile.Values.ContainsKey(RoleArnOption) && !profile.Values.ContainsKey(WebIdentityTokenFileOption);

    // botocore's _has_static_credentials: either key counts, even empty.
    private static bool HasStaticKeys(SharedProfile profile) =>
        profile.Values.ContainsKey(AccessKeyIdOption) || profile.Values.ContainsKey(SecretAccessKeyOption);

    private static CredentialsUnavailableException Unsupported(string profileName, string setting, string feature) =>
        new($"The profile '{profileName}' uses the setting '{setting}' ({feature}), which DynaSharp does not support. " +
            $"Use a profile with {AccessKeyIdOption} and {SecretAccessKeyOption}, or pass another credentials provider.");

    // Returns null when the section has no aws_access_key_id; throws when it has one but the credentials are incomplete.
    private static AwsCredentials? ReadKeys(string profileName, IReadOnlyDictionary<string, IniValue>? values)
    {
        if (values is null || !values.ContainsKey(AccessKeyIdOption))
        {
            return null;
        }

        var accessKeyId = SharedProfile.GetText(values, AccessKeyIdOption);
        var secretAccessKey = SharedProfile.GetText(values, SecretAccessKeyOption);
        if (accessKeyId is null || secretAccessKey is null)
        {
            throw new CredentialsUnavailableException(
                $"The profile '{profileName}' is incomplete: {AccessKeyIdOption} and {SecretAccessKeyOption} must both be set and not empty.");
        }

        // As in botocore, a present aws_security_token wins over aws_session_token, even if it is empty.
        var tokenOption = values.ContainsKey(SecurityTokenOption) ? SecurityTokenOption : SessionTokenOption;
        return new AwsCredentials(accessKeyId, secretAccessKey, SharedProfile.GetText(values, tokenOption));
    }

    private static string? ReadSessionName(SharedProfile profile)
    {
        var sessionName = profile.GetText(SessionNameOption);
        return sessionName is null || Sts.IsValidSessionName(sessionName)
            ? sessionName
            : throw new CredentialsUnavailableException($"The {SessionNameOption} of the profile '{profile.Name}' {Sts.SessionNameRule}.");
    }

    private static int? ReadDuration(SharedProfile profile)
    {
        if (!profile.Values.ContainsKey(DurationOption))
        {
            return null;
        }

        // botocore ignores a value that is not a number; a typo here would silently shorten or lengthen every session.
        return int.TryParse(profile.GetText(DurationOption), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 900 and <= 43200
            ? seconds
            : throw new CredentialsUnavailableException($"The {DurationOption} of the profile '{profile.Name}' must be a whole number from 900 to 43200.");
    }

    // The providers of botocore's ProfileProviderBuilder, in its order. Null when the profile has none of them.
    private ProfileSource? ResolveOwn(SharedProfile profile)
    {
        // botocore takes web identity only for a token file that is set and not empty, so an empty one falls through.
        if (profile.GetText(WebIdentityTokenFileOption) is { } tokenFile)
        {
            return WebIdentity(profile, tokenFile);
        }

        // botocore's SSOProvider: the account or the role makes a profile one of SSO. A profile with only sso_session names a session for other
        // tools and falls through.
        if (profile.Values.ContainsKey("sso_account_id") || profile.Values.ContainsKey("sso_role_name"))
        {
            return Sso(profile);
        }

        if (ReadKeys(profile.Name, profile.CredentialsFileValues) is { } fromCredentialsFile)
        {
            return new ProfileSource(profile.Name, "the static keys of the credentials file", new StaticCredentialsProvider(fromCredentialsFile));
        }

        if (profile.Values.ContainsKey("login_session"))
        {
            throw Unsupported(profile.Name, "login_session", "the AWS login flow");
        }

        if (profile.Values.ContainsKey(ProcessOption))
        {
            // An empty value or a block is no command line, which the provider reports when it runs.
            var commandLine = profile.GetText(ProcessOption) ?? string.Empty;
            var process = new ProcessCredentialsProvider(profile.Name, commandLine, OperatingSystem.IsWindows(), ProcessCredentialsProvider.Timeout, _dependencies);
            return new ProfileSource(profile.Name, $"its {ProcessOption}", process);
        }

        return ReadKeys(profile.Name, profile.ConfigFileValues) is { } fromConfigFile
            ? new ProfileSource(profile.Name, "the static keys of the config file", new StaticCredentialsProvider(fromConfigFile))
            : null;
    }

    // botocore's SSOProvider._load_sso_config and _resolve_sso_session_reference.
    private ProfileSource Sso(SharedProfile profile)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (option, value) in profile.Values)
        {
            settings[option] = value.Text;
        }

        string[] required = ["sso_start_url", "sso_region", "sso_account_id", "sso_role_name"];
        string? sessionName = null;
        if (settings.TryGetValue(SsoSessionOption, out var named))
        {
            sessionName = named;
            if (!_files.SsoSessions.TryGetValue(named, out var session))
            {
                throw new CredentialsUnavailableException($"The sso-session '{named}' of the profile '{profile.Name}' does not exist in the config file.");
            }

            foreach (var (option, value) in session)
            {
                if (settings.TryGetValue(option, out var own) && own != value.Text)
                {
                    throw new CredentialsUnavailableException(
                        $"The {option} of the profile '{profile.Name}' is '{own}', and that of its sso-session '{named}' is '{value.Text}': they must agree.");
                }

                settings[option] = value.Text;
            }

            required = [.. required, SsoSessionOption];
        }

        var missing = required.Where(option => string.IsNullOrEmpty(settings.GetValueOrDefault(option))).ToArray();
        if (missing.Length > 0)
        {
            throw new CredentialsUnavailableException($"The profile '{profile.Name}' uses SSO but lacks {string.Join(", ", missing)}.");
        }

        AwsRegion region;
        try
        {
            region = AwsRegion.FromName(settings["sso_region"]);
        }
        catch (ArgumentException exception)
        {
            throw new CredentialsUnavailableException($"The sso_region of the profile '{profile.Name}' is '{settings["sso_region"]}', which is not a valid AWS region name.", exception);
        }

        var accountId = settings["sso_account_id"];
        var roleName = settings["sso_role_name"];
        var ssoSettings = new SsoCredentialsProvider.Settings(
            settings["sso_start_url"], region, accountId, roleName, sessionName, $"Run `aws sso login --profile {profile.Name}` to sign in again.");
        return new ProfileSource(profile.Name, $"the SSO role {roleName} in the account {accountId}", new SsoCredentialsProvider(ssoSettings, _dependencies));
    }

    private ProfileSource WebIdentity(SharedProfile profile, string tokenFile)
    {
        var roleArn = profile.GetText(RoleArnOption)
            ?? throw new CredentialsUnavailableException(
                $"The profile '{profile.Name}' sets {WebIdentityTokenFileOption} but no {RoleArnOption}: web identity federation needs the role to assume.");
        var settings = new WebIdentityCredentialsProvider.Settings(tokenFile, roleArn, StsRegion(profile), ReadSessionName(profile));
        var provider = new WebIdentityCredentialsProvider(
            _dependencies.GetEnvironmentVariable,
            _dependencies.ReadFile,
            _dependencies.Handler,
            _dependencies.TimeProvider,
            _dependencies.LoggerFactory.CreateLogger<WebIdentityCredentialsProvider>(),
            settings.Region,
            settings);
        return new ProfileSource(profile.Name, $"web identity federation into the role {roleArn}", provider);
    }

    private ProfileSource AssumeRole(SharedProfile profile)
    {
        var roleArn = profile.GetText(RoleArnOption) ?? throw new CredentialsUnavailableException($"The {RoleArnOption} of the profile '{profile.Name}' is empty.");
        if (profile.Values.ContainsKey("mfa_serial"))
        {
            throw new CredentialsUnavailableException(
                $"The profile '{profile.Name}' sets mfa_serial. A role that needs the code of an MFA device needs a person at every refresh, which DynaSharp does not ask for. " +
                "Get a session with the AWS CLI and pass its keys, or assume a role without MFA.");
        }

        // An empty value is set, as in botocore, and then names no profile and no source.
        var sourceProfile = profile.Values.ContainsKey(SourceProfileOption) ? profile.GetText(SourceProfileOption) ?? string.Empty : null;
        var credentialSource = profile.Values.ContainsKey(CredentialSourceOption) ? profile.GetText(CredentialSourceOption) ?? string.Empty : null;
        ICredentialsProvider source;
        string description;
        if (sourceProfile is not null && credentialSource is not null)
        {
            throw new CredentialsUnavailableException(
                $"The profile '{profile.Name}' sets both {SourceProfileOption} and {CredentialSourceOption}; a role takes one of them.");
        }
        else if (sourceProfile is not null)
        {
            source = FromSourceProfile(profile.Name, sourceProfile);
            description = $"the profile '{sourceProfile}'";
        }
        else if (credentialSource is not null)
        {
            source = FromCredentialSource(profile.Name, credentialSource);
            description = $"the {CredentialSourceOption} {credentialSource}";
        }
        else
        {
            throw new CredentialsUnavailableException(
                $"The profile '{profile.Name}' sets {RoleArnOption} but neither {SourceProfileOption} nor {CredentialSourceOption}, so nothing can assume the role.");
        }

        var settings = new AssumeRoleCredentialsProvider.Settings(roleArn, ReadSessionName(profile), profile.GetText("external_id"), ReadDuration(profile), StsRegion(profile), null);
        return new ProfileSource(profile.Name, $"assuming the role {roleArn} with the credentials of {description}", new AssumeRoleCredentialsProvider(source, settings, _dependencies));
    }

    private ICredentialsProvider FromSourceProfile(string parentName, string sourceName)
    {
        // botocore's _validate_source_profile, before the source is marked as visited.
        if (!_files.Profiles.TryGetValue(sourceName, out var source))
        {
            throw new CredentialsUnavailableException($"The {SourceProfileOption} '{sourceName}' of the profile '{parentName}' does not exist.");
        }

        if (_visited.Contains(sourceName) && (sourceName != parentName || !HasStaticKeys(source)))
        {
            throw new CredentialsUnavailableException(
                $"The profiles form a loop: {string.Join(" -> ", _visited.Select(name => $"'{name}'"))} -> '{sourceName}'. A profile may name itself as its {SourceProfileOption} only if it has static keys.");
        }

        _visited.Add(sourceName);

        // botocore's _resolve_credentials_from_profile.
        if (HasStaticKeys(source) || !HasAssumeRoleSettings(source))
        {
            return (ResolveOwn(source) ?? throw new CredentialsUnavailableException($"The {SourceProfileOption} '{sourceName}' of the profile '{parentName}' has no credentials.")).Provider;
        }

        return AssumeRole(source).Provider;
    }

    // botocore's CanonicalNameCredentialSourcer with its three providers, whose names it compares exactly.
    private NamedSource FromCredentialSource(string profileName, string name)
    {
        var getEnvironmentVariable = _dependencies.GetEnvironmentVariable;
        Func<CancellationToken, ValueTask<CredentialsOutcome>> tryGetCredentials = name switch
        {
            "Environment" => new EnvironmentCredentialsProvider(
                getEnvironmentVariable, _dependencies.TimeProvider, _dependencies.LoggerFactory.CreateLogger<EnvironmentCredentialsProvider>()).TryGetCredentialsAsync,
            "Ec2InstanceMetadata" => new InstanceMetadataCredentialsProvider(
                getEnvironmentVariable, _dependencies.Handler, _dependencies.TimeProvider, _dependencies.LoggerFactory.CreateLogger<InstanceMetadataCredentialsProvider>(), null).TryGetCredentialsAsync,
            "EcsContainer" => new ContainerCredentialsProvider(
                getEnvironmentVariable, _dependencies.ReadFile, _dependencies.Handler, _dependencies.TimeProvider, _dependencies.LoggerFactory.CreateLogger<ContainerCredentialsProvider>()).TryGetCredentialsAsync,
            _ => throw new CredentialsUnavailableException(
                $"The {CredentialSourceOption} '{name}' of the profile '{profileName}' is none of Environment, Ec2InstanceMetadata and EcsContainer."),
        };
        return new NamedSource($"{CredentialSourceOption} {name} of the profile '{profileName}'", tryGetCredentials);
    }

    // The region of STS: the client's, else what a client would find for this profile, AWS_REGION, AWS_DEFAULT_REGION and its region. Looked up
    // once, and only for a profile that calls STS, so that a broken region does not break static keys. Null leaves it to the provider, which
    // fails at the fetch that needs it.
    private AwsRegion? StsRegion(SharedProfile profile)
    {
        if (!_stsRegionResolved)
        {
            _stsRegion = _clientRegion ?? FindRegion(profile);
            _stsRegionResolved = true;
        }

        return _stsRegion;
    }

    private AwsRegion? FindRegion(SharedProfile profile)
    {
        string? name = null;
        var origin = string.Empty;
        foreach (var variable in RegionVariables)
        {
            name = _dependencies.GetEnvironmentVariable(variable);
            origin = $"The {variable} environment variable";
            if (!string.IsNullOrEmpty(name))
            {
                break;
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            name = profile.GetText("region");
            origin = $"The region setting of the profile '{profile.Name}'";
        }

        if (name is null)
        {
            return null;
        }

        try
        {
            return AwsRegion.FromName(name);
        }
        catch (ArgumentException exception)
        {
            throw new CredentialsUnavailableException($"{origin} contains '{name}', which is not a valid AWS region name.", exception);
        }
    }

    /// <summary>A source of a <c>credential_source</c>, for which having no credentials is an error, as in botocore.</summary>
    private sealed class NamedSource(string name, Func<CancellationToken, ValueTask<CredentialsOutcome>> tryGetCredentials) : ICredentialsProvider
    {
        public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            var outcome = await tryGetCredentials(cancellationToken).ConfigureAwait(false);
            return outcome.GetCredentials(name);
        }
    }
}
