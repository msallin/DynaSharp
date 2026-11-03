using System.Globalization;
using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies credentials from the <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c> and, for temporary credentials,
/// <c>AWS_SESSION_TOKEN</c> and <c>AWS_CREDENTIAL_EXPIRATION</c> environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The variables are read on first use, and the resulting <see cref="AwsCredentials"/> instance is returned by every later call. Without
/// <c>AWS_CREDENTIAL_EXPIRATION</c> the credentials never expire and the variables are not read again, which also keeps the signer's key cache
/// effective. The provider is thread-safe: concurrent first calls all receive the same instance.
/// </para>
/// <para>
/// With <c>AWS_CREDENTIAL_EXPIRATION</c>, an ISO 8601 time such as <c>2026-11-03T12:00:00Z</c> that tools like <c>aws configure export-credentials
/// --format env</c> write, the credentials carry that expiration, as in botocore. The variables are read again from 15 minutes before it, and every call
/// waits for that from 10 minutes before it, with the windows of <see cref="WebIdentityCredentialsProvider"/>; if DynamoDB rejects the credentials as
/// expired, <see cref="TryInvalidate"/> drops them and the next call reads the variables again. So a process that sets new variables before the old
/// ones expire goes on without a restart. Credentials whose expiration has passed fail, and name the variable. A value that is not a time fails too.
/// </para>
/// <para>
/// A failure is not cached. If the variables are missing on the first call, a later call reads them again, so a
/// process that sets them late recovers without a restart.
/// </para>
/// </remarks>
public sealed class EnvironmentCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string AccessKeyIdVariable = "AWS_ACCESS_KEY_ID";
    private const string SecretAccessKeyVariable = "AWS_SECRET_ACCESS_KEY";
    private const string SessionTokenVariable = "AWS_SESSION_TOKEN";
    private const string ExpirationVariable = "AWS_CREDENTIAL_EXPIRATION";
    private const string SourceName = "environment variables";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshingCredentialsCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentCredentialsProvider"/> class that reads the
    /// environment of the current process.
    /// </summary>
    public EnvironmentCredentialsProvider()
        : this(ProcessEnvironment.GetVariable)
    {
    }

    internal EnvironmentCredentialsProvider(Func<string, string?> getEnvironmentVariable)
        : this(getEnvironmentVariable, TimeProvider.System, NullLogger.Instance)
    {
    }

    internal EnvironmentCredentialsProvider(Func<string, string?> getEnvironmentVariable, TimeProvider timeProvider, ILogger logger)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _timeProvider = timeProvider;
        _cache = new RefreshingCredentialsCache(SourceName, timeProvider, logger, () => ValueTask.FromResult(CredentialsOutcome.Found(Load())));
    }

    /// <summary>
    /// Gets the credentials from the environment, reading it on the first call, and again near the expiration of <c>AWS_CREDENTIAL_EXPIRATION</c>.
    /// </summary>
    /// <param name="cancellationToken">Not used: reading environment variables is synchronous and instant.</param>
    /// <returns>The same <see cref="AwsCredentials"/> instance until the variables are read again.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// <c>AWS_ACCESS_KEY_ID</c> or <c>AWS_SECRET_ACCESS_KEY</c> is not set or empty, or <c>AWS_CREDENTIAL_EXPIRATION</c> is not a time or has passed.
    /// The next call reads the environment again.
    /// </exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await _cache.GetAsync(CancellationToken.None).ConfigureAwait(false);
        return outcome.Credentials!;
    }

    /// <summary>
    /// Drops credentials that DynamoDB rejected as expired, if <c>AWS_CREDENTIAL_EXPIRATION</c> gave them an expiration, so that the next call reads the
    /// environment again. A client does this by itself.
    /// </summary>
    /// <param name="rejected">The instance that was rejected. Any other than the current one has been replaced already, and is ignored.</param>
    /// <returns>
    /// <see langword="true"/> if the credentials have an expiration: the environment may hold new ones by now. <see langword="false"/> for credentials
    /// without one, which the environment holds once and for all.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        if (rejected.Expiration is null)
        {
            return false;
        }

        _cache.Drop(rejected);
        return true;
    }

    // For the default chain: an unset AWS_ACCESS_KEY_ID means this source does not apply, while a half-set environment is an
    // error that stops the chain, as PartialCredentialsError does in botocore.
    internal ValueTask<CredentialsOutcome> TryGetCredentialsAsync(CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(_getEnvironmentVariable(AccessKeyIdVariable))
            ? ValueTask.FromResult(CredentialsOutcome.DoesNotApply($"{AccessKeyIdVariable} is not set or empty"))
            : _cache.GetAsync(CancellationToken.None);

    private AwsCredentials Load()
    {
        var accessKeyId = _getEnvironmentVariable(AccessKeyIdVariable);
        var secretAccessKey = _getEnvironmentVariable(SecretAccessKeyVariable);

        if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
        {
            throw new CredentialsUnavailableException(
                $"No credentials in the environment: {AccessKeyIdVariable} and {SecretAccessKeyVariable} must both be set and not empty " +
                $"({SessionTokenVariable} is optional and only needed for temporary credentials).");
        }

        DateTimeOffset? expiration = null;
        if (_getEnvironmentVariable(ExpirationVariable) is { Length: > 0 } text)
        {
            // The value is not echoed, as for the other variables of credentials.
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                throw new CredentialsUnavailableException($"{ExpirationVariable} is not an ISO 8601 time, such as 2026-11-03T12:00:00Z.");
            }

            if (parsed <= _timeProvider.GetUtcNow())
            {
                throw new CredentialsUnavailableException(
                    $"The credentials in the environment expired at {parsed.ToString("O", CultureInfo.InvariantCulture)}, the time in {ExpirationVariable}: set new ones, or check the system clock.");
            }

            expiration = parsed;
        }

        return new AwsCredentials(accessKeyId, secretAccessKey, _getEnvironmentVariable(SessionTokenVariable), expiration);
    }
}
