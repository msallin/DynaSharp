using DynaSharp.Configuration;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies the long-term or session credentials of a profile in the shared AWS credentials file (<c>~/.aws/credentials</c>) and
/// config file (<c>~/.aws/config</c>), the files that the AWS CLI and the AWS SDKs share.
/// </summary>
/// <remarks>
/// <para>
/// Which profile: the one given to the constructor, otherwise the one named by the <c>AWS_PROFILE</c> environment variable,
/// otherwise <c>default</c>. Where the files are: <c>AWS_SHARED_CREDENTIALS_FILE</c> and <c>AWS_CONFIG_FILE</c>, otherwise the
/// <c>.aws</c> folder in the home folder of the user. The files are parsed exactly as botocore does. In the credentials
/// file a profile is a section <c>[dev]</c>; in the config file it is <c>[profile dev]</c>, except <c>[default]</c>. If both files
/// define the profile, the credentials file wins option by option. The options read are <c>aws_access_key_id</c>,
/// <c>aws_secret_access_key</c> and, for temporary credentials, <c>aws_session_token</c> (or the older
/// <c>aws_security_token</c>, which wins when both are set). Credentials in the credentials file are preferred to credentials in
/// the config file of the same profile; the two are never mixed.
/// </para>
/// <para>
/// Both files are optional. A missing file, or a file the process may not read, counts as empty. If no profile was asked for by name
/// and <c>default</c> does not exist, or if the profile has no <c>aws_access_key_id</c>, <see cref="GetCredentialsAsync"/> throws a
/// <see cref="CredentialsUnavailableException"/> and the default chain moves on to the next source, as botocore does. In contrast, a
/// profile named by the constructor or by <c>AWS_PROFILE</c> that does not exist, a file that exists but is malformed, a profile with
/// an access key but no secret, and a profile that needs a feature DynaSharp does not support yet fail loudly with a
/// <see cref="CredentialsUnavailableException"/> that names the file and line, or the setting and the
/// profile. They are never skipped, because the next source would sign requests as a different principal. The unsupported
/// settings are <c>role_arn</c> (assume role), <c>web_identity_token_file</c>, <c>sso_session</c> and every other <c>sso_*</c>
/// setting, <c>login_session</c>, and <c>credential_process</c> (unless the credentials file of the profile has static keys, which
/// botocore prefers as well). Messages never contain file content.
/// </para>
/// <para>
/// The files are read on the first successful call and not again, so changes need a restart; the returned
/// <see cref="AwsCredentials"/> instance never changes and has no expiration. A failed call is not cached and reads the files
/// again. The provider is thread-safe. Reading happens synchronously on the calling thread: it is a small local file.
/// </para>
/// <para>
/// Differences from the AWS SDKs: <c>AWS_DEFAULT_PROFILE</c> is not read, and a profile named by <c>AWS_PROFILE</c> that does not
/// exist is reported by <see cref="GetCredentialsAsync"/> only, not when the client is created.
/// </para>
/// </remarks>
public sealed class ProfileCredentialsProvider : ICredentialsProvider
{
    private const string AccessKeyIdOption = "aws_access_key_id";
    private const string SecretAccessKeyOption = "aws_secret_access_key";
    private const string SessionTokenOption = "aws_session_token";
    private const string SecurityTokenOption = "aws_security_token";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly ReadTextFile _readFile;
    private readonly string? _profileName;
    private AwsCredentials? _credentials;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileCredentialsProvider"/> class for the profile named by the
    /// <c>AWS_PROFILE</c> environment variable, or <c>default</c>.
    /// </summary>
    public ProfileCredentialsProvider()
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileCredentialsProvider"/> class for a named profile.
    /// </summary>
    /// <param name="profileName">The profile, for example <c>dev</c>. <c>AWS_PROFILE</c> is ignored.</param>
    /// <exception cref="ArgumentException"><paramref name="profileName"/> is null or empty.</exception>
    public ProfileCredentialsProvider(string profileName)
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, NotEmpty(profileName))
    {
    }

    internal ProfileCredentialsProvider(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile, string? profileName)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _readFile = readFile;
        _profileName = profileName;
    }

    /// <summary>
    /// Gets the credentials of the profile, reading the files on the first successful call only.
    /// </summary>
    /// <param name="cancellationToken">Not used: reading the files is synchronous.</param>
    /// <returns>The same <see cref="AwsCredentials"/> instance on every successful call.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// The profile or its keys are not found, a file is malformed, the profile has only one of the two keys, or the profile uses
    /// a setting that is not supported yet. The message says which.
    /// </exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await TryGetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials("shared AWS files");
    }

    internal ValueTask<CredentialsOutcome> TryGetCredentialsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(TryGetCredentials());

    private static string NotEmpty(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        return profileName;
    }

    // The order is the one of botocore's credential chain for a profile: assume role, web identity and SSO come first and take the
    // profile over even if it also has keys; then static keys of the credentials file; then login and credential_process; then
    // static keys of the config file.
    private static CredentialsOutcome Resolve(SharedProfile profile)
    {
        if (FindUnsupportedSetting(profile) is { } unsupported)
        {
            throw Unsupported(profile.Name, unsupported);
        }

        if (ReadKeys(profile.Name, profile.CredentialsFileValues) is { } fromCredentialsFile)
        {
            return CredentialsOutcome.Found(fromCredentialsFile);
        }

        if (profile.Values.ContainsKey("login_session"))
        {
            throw Unsupported(profile.Name, ("login_session", "the AWS login flow"));
        }

        if (profile.Values.ContainsKey("credential_process"))
        {
            throw Unsupported(profile.Name, ("credential_process", "an external credential process"));
        }

        return ReadKeys(profile.Name, profile.ConfigFileValues) is { } fromConfigFile
            ? CredentialsOutcome.Found(fromConfigFile)
            : CredentialsOutcome.DoesNotApply($"profile '{profile.Name}' has no {AccessKeyIdOption}");
    }

    private static (string Setting, string Feature)? FindUnsupportedSetting(SharedProfile profile)
    {
        if (profile.Values.ContainsKey("role_arn") && !profile.Values.ContainsKey("web_identity_token_file"))
        {
            return ("role_arn", "assuming a role");
        }

        if (profile.Values.ContainsKey("web_identity_token_file"))
        {
            return ("web_identity_token_file", "web identity federation");
        }

        foreach (var option in profile.Values.Keys)
        {
            if (option.StartsWith("sso_", StringComparison.Ordinal))
            {
                return (option, "AWS IAM Identity Center (SSO)");
            }
        }

        return null;
    }

    private static CredentialsUnavailableException Unsupported(string profileName, (string Setting, string Feature) unsupported) =>
        new($"The profile '{profileName}' uses the setting '{unsupported.Setting}' ({unsupported.Feature}), which DynaSharp does not support yet. " +
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

    private CredentialsOutcome TryGetCredentials()
    {
        if (Volatile.Read(ref _credentials) is { } cached)
        {
            return CredentialsOutcome.Found(cached);
        }

        var profileName = _profileName ?? SharedConfigFiles.SelectProfileName(_getEnvironmentVariable);
        var files = SharedConfigFiles.Load(_getEnvironmentVariable, _readFile);
        if (!files.Profiles.TryGetValue(profileName, out var profile))
        {
            // Only the fallback to 'default' may be missing. A profile asked for by name is a source that applies, and the next one would be another principal.
            var reason = $"the profile '{profileName}' was not found ({files.Description})";
            return _profileName is null && !SharedConfigFiles.IsProfileNamed(_getEnvironmentVariable)
                ? CredentialsOutcome.DoesNotApply(reason)
                : CredentialsOutcome.Broken(reason);
        }

        var outcome = Resolve(profile);
        if (outcome.Credentials is null)
        {
            return outcome;
        }

        // Concurrent first calls publish one instance.
        var published = Interlocked.CompareExchange(ref _credentials, outcome.Credentials, null) ?? outcome.Credentials;
        return CredentialsOutcome.Found(published);
    }
}
