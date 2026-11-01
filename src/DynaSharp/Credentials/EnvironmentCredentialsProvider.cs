using DynaSharp.Configuration;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies credentials from the <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c> and, for temporary credentials,
/// <c>AWS_SESSION_TOKEN</c> environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The variables are read once, on first use, and the resulting <see cref="AwsCredentials"/> instance is returned
/// by every later call. Changing the variables afterwards has no effect, which also keeps the signer's key cache effective.
/// The provider is thread-safe: concurrent first calls all receive the same instance.
/// </para>
/// <para>
/// A failure is not cached. If the variables are missing on the first call, a later call reads them again, so a
/// process that sets them late recovers without a restart.
/// </para>
/// <para>
/// Environment credentials never expire from the provider's point of view. Use a provider that refreshes
/// (for example from a container endpoint) if the process outlives the session token.
/// </para>
/// </remarks>
public sealed class EnvironmentCredentialsProvider : ICredentialsProvider
{
    private const string AccessKeyIdVariable = "AWS_ACCESS_KEY_ID";
    private const string SecretAccessKeyVariable = "AWS_SECRET_ACCESS_KEY";
    private const string SessionTokenVariable = "AWS_SESSION_TOKEN";

    private readonly Func<string, string?> _getEnvironmentVariable;

    // PublicationOnly: the first successfully created value wins for all threads, and a thrown exception is not cached.
    private readonly Lazy<AwsCredentials> _credentials;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentCredentialsProvider"/> class that reads the
    /// environment of the current process.
    /// </summary>
    public EnvironmentCredentialsProvider()
        : this(ProcessEnvironment.GetVariable)
    {
    }

    internal EnvironmentCredentialsProvider(Func<string, string?> getEnvironmentVariable)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _credentials = new Lazy<AwsCredentials>(Load, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    /// Gets the credentials from the environment, reading it on the first call only.
    /// </summary>
    /// <param name="cancellationToken">Not used: reading environment variables is synchronous and instant.</param>
    /// <returns>The same <see cref="AwsCredentials"/> instance on every successful call.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// <c>AWS_ACCESS_KEY_ID</c> or <c>AWS_SECRET_ACCESS_KEY</c> is not set or empty. The next call reads the environment again.
    /// </exception>
    public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_credentials.Value);

    // For the default chain: an unset AWS_ACCESS_KEY_ID means this source does not apply, while a half-set environment is an
    // error that stops the chain, as PartialCredentialsError does in botocore.
    internal ValueTask<CredentialsOutcome> TryGetCredentialsAsync(CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(_getEnvironmentVariable(AccessKeyIdVariable))
            ? ValueTask.FromResult(CredentialsOutcome.DoesNotApply($"{AccessKeyIdVariable} is not set or empty"))
            : ValueTask.FromResult(CredentialsOutcome.Found(_credentials.Value));

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

        return new AwsCredentials(accessKeyId, secretAccessKey, _getEnvironmentVariable(SessionTokenVariable));
    }
}
