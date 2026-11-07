using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;

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
/// an access key but no secret, and a profile that needs a feature DynaSharp does not support fail loudly with a
/// <see cref="CredentialsUnavailableException"/> that names the file and line, or the setting and the
/// profile. They are never skipped, because the next source would sign requests as a different principal. The unsupported
/// settings are <c>login_session</c> and <c>mfa_serial</c> on a role. Messages never contain file content.
/// </para>
/// <para>
/// The order is botocore's. <c>role_arn</c> with <c>source_profile</c> or <c>credential_source</c> comes first and takes the profile over even if it has
/// keys: it assumes the role with <see cref="AssumeRoleCredentialsProvider"/>, with <c>role_session_name</c>, <c>external_id</c> and
/// <c>duration_seconds</c> (900 to 43200) of the profile. A <c>source_profile</c> with static keys, or without <c>role_arn</c>, gives its own credentials,
/// from any source below; one with <c>role_arn</c> and no keys assumes its role in turn, so roles chain, and a profile that comes back is a loop, which fails,
/// unless a profile names itself and has static keys. <c>credential_source</c> is <c>Environment</c>, <c>Ec2InstanceMetadata</c> or <c>EcsContainer</c>.
/// Then <c>web_identity_token_file</c> with <c>role_arn</c> federates through <see cref="WebIdentityCredentialsProvider"/>; then <c>sso_account_id</c> or
/// <c>sso_role_name</c> gets the role of AWS IAM Identity Center through <see cref="SsoCredentialsProvider"/>, with the <c>sso_session</c> of a
/// <c>[sso-session name]</c> section of the config file or the older <c>sso_start_url</c> and <c>sso_region</c> in the profile; then the static keys of the
/// credentials file, <c>credential_process</c>, and the static keys of the config file. The region of STS is that of the client, otherwise
/// <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> or the <c>region</c> of this profile.
/// </para>
/// <para>
/// <c>credential_process</c> runs a program that writes the credentials as JSON, see the AWS documentation of the setting: on Windows the command line is
/// split by the rules of the C runtime, elsewhere like a POSIX shell, and the program starts without a shell, gets 20 seconds, no input, and is killed
/// with what it started when it runs longer. It runs again before the <c>Expiration</c> it wrote, with the windows of
/// <see cref="WebIdentityCredentialsProvider"/>. A failure names the profile and the exit code, never what the program wrote, which is a secret. Static
/// keys in the credentials file of the profile go ahead of it, and static keys in its config file come after it, as in botocore.
/// </para>
/// <para>
/// The files are read on the first call that finds what the profile asks for, and not again, so changes need a restart. Static keys are one
/// <see cref="AwsCredentials"/> instance without expiration. A failed call is not cached and reads the files
/// again. The provider is thread-safe. Reading happens synchronously on the calling thread: it is a small local file.
/// </para>
/// <para>
/// Differences from the AWS SDKs: <c>AWS_DEFAULT_PROFILE</c> is not read, and a profile named by <c>AWS_PROFILE</c> that does not
/// exist is reported by <see cref="GetCredentialsAsync"/> only, not when the client is created.
/// </para>
/// </remarks>
public sealed class ProfileCredentialsProvider : IRefreshableCredentialsProvider
{
    private readonly CredentialsDependencies _dependencies;
    private readonly ILogger _logger;
    private readonly string? _profileName;
    private readonly AwsRegion? _region;
    private ICredentialsProvider? _resolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileCredentialsProvider"/> class for the profile named by the
    /// <c>AWS_PROFILE</c> environment variable, or <c>default</c>.
    /// </summary>
    public ProfileCredentialsProvider()
        : this(CredentialsDependencies.Default, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileCredentialsProvider"/> class for a named profile.
    /// </summary>
    /// <param name="profileName">The profile, for example <c>dev</c>. <c>AWS_PROFILE</c> is ignored.</param>
    /// <exception cref="ArgumentException"><paramref name="profileName"/> is null or empty.</exception>
    public ProfileCredentialsProvider(string profileName)
        : this(CredentialsDependencies.Default, NotEmpty(profileName))
    {
    }

    internal ProfileCredentialsProvider(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile, string? profileName)
        : this(CredentialsDependencies.Default with { GetEnvironmentVariable = getEnvironmentVariable, ReadFile = readFile }, profileName)
    {
    }

    internal ProfileCredentialsProvider(CredentialsDependencies dependencies, string? profileName, AwsRegion? region = null)
    {
        _dependencies = dependencies;
        _logger = dependencies.LoggerFactory.CreateLogger<ProfileCredentialsProvider>();
        _profileName = profileName;
        _region = region;
    }

    /// <summary>
    /// Gets the credentials of the profile, reading the files on the first call that finds what the profile asks for.
    /// </summary>
    /// <param name="cancellationToken">Stops this call from waiting for STS or a <c>credential_process</c>. The files are read synchronously.</param>
    /// <returns>For static keys, the same <see cref="AwsCredentials"/> instance on every call; otherwise the same instance until it is refreshed.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// The profile or its keys are not found, a file is malformed, the profile has only one of the two keys, the profile uses
    /// a setting that DynaSharp does not support or a role that is configured wrongly, or STS or its <c>credential_process</c> fails. The message says which.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while a program ran.</exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await TryGetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials("shared AWS files");
    }

    /// <summary>
    /// Drops credentials that DynamoDB rejected as expired, if the profile gets them from a source that can be asked again. A client does this by itself.
    /// </summary>
    /// <param name="rejected">The instance that was rejected. Any other than the current one has been replaced already, and is ignored.</param>
    /// <returns>
    /// <see langword="true"/> if the profile assumes a role, federates a web identity, uses SSO or runs a <c>credential_process</c>, which the next call asks again. <see langword="false"/> for static
    /// keys, which the files hold once and for all, and before the first call.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return Volatile.Read(ref _resolved) is IRefreshableCredentialsProvider refreshable && refreshable.TryInvalidate(rejected);
    }

    internal async ValueTask<CredentialsOutcome> TryGetCredentialsAsync(CancellationToken cancellationToken)
    {
        var resolved = Volatile.Read(ref _resolved);
        if (resolved is null)
        {
            var (outcome, source) = Resolve();
            if (source is null)
            {
                return outcome;
            }

            // Concurrent first calls publish one source, and with it one instance of static keys.
            resolved = Interlocked.CompareExchange(ref _resolved, source.Provider, null);
            if (resolved is null)
            {
                resolved = source.Provider;
                CredentialsLog.ProfileSourceChosen(_logger, source.ProfileName, source.Description);
            }
        }

        return CredentialsOutcome.Found(await resolved.GetCredentialsAsync(cancellationToken).ConfigureAwait(false));
    }

    private static string NotEmpty(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        return profileName;
    }

    // Either a source, or why there is none.
    private (CredentialsOutcome Outcome, ProfileSource? Source) Resolve()
    {
        var getEnvironmentVariable = _dependencies.GetEnvironmentVariable;
        var profileName = _profileName ?? SharedConfigFiles.SelectProfileName(getEnvironmentVariable);
        var files = SharedConfigFiles.Load(getEnvironmentVariable, _dependencies.ReadFile);
        if (!files.Profiles.TryGetValue(profileName, out var profile))
        {
            // Only the fallback to 'default' may be missing. A profile asked for by name is a source that applies, and the next one would be another principal.
            var reason = $"the profile '{profileName}' was not found ({files.Description})";
            return (_profileName is null && !SharedConfigFiles.IsProfileNamed(getEnvironmentVariable)
                ? CredentialsOutcome.DoesNotApply(reason)
                : CredentialsOutcome.Broken(reason), null);
        }

        var source = new ProfileChain(files, _dependencies, _region).Resolve(profile);
        return source is null
            ? (CredentialsOutcome.DoesNotApply($"profile '{profile.Name}' has no aws_access_key_id"), null)
            : (default, source);
    }
}
