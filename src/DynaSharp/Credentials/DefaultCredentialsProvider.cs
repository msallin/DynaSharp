using DynaSharp.Configuration;
using DynaSharp.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// Finds credentials the way the AWS SDKs do, trying these sources in order and using the first that has credentials:
/// <list type="number">
/// <item><description>the environment variables <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c> and <c>AWS_SESSION_TOKEN</c> (<see cref="EnvironmentCredentialsProvider"/>);</description></item>
/// <item><description>the profile in the shared credentials and config files (<see cref="ProfileCredentialsProvider"/>);</description></item>
/// <item><description>the container credentials endpoint of Amazon ECS and EKS (<see cref="ContainerCredentialsProvider"/>);</description></item>
/// <item><description>the instance metadata service of Amazon EC2 (<see cref="InstanceMetadataCredentialsProvider"/>).</description></item>
/// </list>
/// This is what a <see cref="DynamoDbClient"/> uses when <see cref="DynamoDbClientOptions.Credentials"/> is not set.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is looked up when the provider is created. The first call to <see cref="GetCredentialsAsync"/> walks the sources in
/// order and remembers the one that answered; every later call goes straight to it. The choice is logged once, at debug level.
/// A source that does not apply is skipped: an unset variable, a missing file, a missing <c>default</c> profile, a profile without an
/// access key, a disabled or unreachable metadata service. The sources are not mixed, and a source is never asked again once another
/// one was chosen.
/// </para>
/// <para>
/// A source that applies but is broken is not skipped. Its <see cref="CredentialsUnavailableException"/> ends the walk, because
/// moving on would sign requests as a different principal than the one the configuration asks for. This covers a partial
/// environment (an access key without a secret), a profile named by <c>AWS_PROFILE</c> that does not exist, a malformed shared file, a
/// profile that needs a feature DynaSharp does not support yet (assume role, SSO, web identity, <c>credential_process</c>), and a
/// container endpoint that is configured and fails in any way:
/// an error status, a malformed response, missing fields, a timeout, or credentials that are already expired. Setting
/// <c>AWS_PROFILE</c> does not turn the environment variables off, as in the AWS SDKs: the variables are tried first.
/// </para>
/// <para>
/// If no source has credentials, the call throws a <see cref="CredentialsUnavailableException"/> that lists what was tried and
/// why each source did not apply. That failure is not cached, so a later call walks the sources again, which includes a request
/// to the metadata service of up to two seconds.
/// </para>
/// <para>
/// Web identity federation (IAM roles for service accounts on EKS) is not supported yet. If <c>AWS_WEB_IDENTITY_TOKEN_FILE</c>
/// is set and the environment has no credentials, the call fails with a <see cref="CredentialsUnavailableException"/> instead of
/// moving on to the container endpoint or the metadata service, which would hand out the credentials of the node.
/// </para>
/// <para>
/// The provider is thread-safe. The temporary credentials of the container endpoint and the metadata service are refreshed before
/// they expire, see those providers, and are fetched again if DynamoDB rejects them as expired (<see cref="TryInvalidate"/>). Failed
/// refreshes are logged as warnings by the logger factory that the <see cref="DynamoDbClient"/> was given.
/// </para>
/// </remarks>
public sealed class DefaultCredentialsProvider : IRefreshableCredentialsProvider
{
    private readonly Source[] _sources;
    private readonly ILogger _logger;
    private ICredentialsProvider? _chosen;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultCredentialsProvider"/> class that reads the environment and the files of
    /// the current process, uses the system clock and does not log.
    /// </summary>
    public DefaultCredentialsProvider()
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLoggerFactory.Instance, null)
    {
    }

    internal DefaultCredentialsProvider(
        Func<string, string?> getEnvironmentVariable,
        ReadTextFile readFile,
        HttpMessageHandler? handler,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        Random? random)
    {
        _logger = loggerFactory.CreateLogger<DefaultCredentialsProvider>();

        var environment = new EnvironmentCredentialsProvider(getEnvironmentVariable);
        var profile = new ProfileCredentialsProvider(getEnvironmentVariable, readFile, null);
        var container = new ContainerCredentialsProvider(
            getEnvironmentVariable, readFile, handler, timeProvider, loggerFactory.CreateLogger<ContainerCredentialsProvider>());
        var instanceMetadata = new InstanceMetadataCredentialsProvider(
            getEnvironmentVariable, handler, timeProvider, loggerFactory.CreateLogger<InstanceMetadataCredentialsProvider>(), random);

        // The order is botocore's. To add a source (web identity and SSO are planned): implement TryGetCredentialsAsync like the
        // providers here, and put it where botocore has it; web identity comes right after the environment, where the guard is.
        _sources =
        [
            new Source("environment variables", environment, environment.TryGetCredentialsAsync),
            new Source("web identity token file", null, _ => ValueTask.FromResult(RejectWebIdentity(getEnvironmentVariable))),
            new Source("shared credentials and config files", profile, profile.TryGetCredentialsAsync),
            new Source("container credentials endpoint", container, container.TryGetCredentialsAsync),
            new Source("EC2 instance metadata service", instanceMetadata, instanceMetadata.TryGetCredentialsAsync),
        ];
    }

    /// <summary>
    /// Gets the credentials of the first source that has some.
    /// </summary>
    /// <param name="cancellationToken">Cancels a pending lookup, for example a request to the metadata service.</param>
    /// <returns>The credentials, the same instance as long as the source keeps them.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// No source has credentials (the message lists them), or a source that applies is broken (the message says how).
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var chosen = Volatile.Read(ref _chosen);
        return chosen is not null ? chosen.GetCredentialsAsync(cancellationToken) : ResolveAsync(cancellationToken);
    }

    /// <summary>
    /// Drops credentials that DynamoDB rejected as expired, if the chosen source can fetch new ones. A client does this by itself.
    /// </summary>
    /// <param name="rejected">The instance that was rejected. Any other than the current one has been replaced already, and is ignored.</param>
    /// <returns>
    /// <see langword="true"/> if the chosen source is one that can be asked again: the container endpoint or the metadata service.
    /// <see langword="false"/> if it holds keys that were read once, the environment or a profile, or if no source was chosen yet. The
    /// chain never moves on to another source for this.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return Volatile.Read(ref _chosen) is IRefreshableCredentialsProvider refreshable && refreshable.TryInvalidate(rejected);
    }

    // Not supported yet, but botocore would use it before anything that follows: moving on to the container endpoint or the
    // instance metadata service would sign requests with the credentials of the node instead of those of the service account.
    private static CredentialsOutcome RejectWebIdentity(Func<string, string?> getEnvironmentVariable) =>
        string.IsNullOrEmpty(getEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE"))
            ? CredentialsOutcome.DoesNotApply("AWS_WEB_IDENTITY_TOKEN_FILE is not set")
            : throw new CredentialsUnavailableException(
                "AWS_WEB_IDENTITY_TOKEN_FILE is set (web identity federation, for example IAM roles for service accounts on EKS), which DynaSharp does not support yet. " +
                "Pass credentials in the client options, or use environment variables, a profile with access keys, or EKS Pod Identity.");

    private async ValueTask<AwsCredentials> ResolveAsync(CancellationToken cancellationToken)
    {
        var tried = new List<string>(_sources.Length);
        foreach (var source in _sources)
        {
            var outcome = await source.TryGetCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (outcome.Kind == CredentialsOutcomeKind.DoesNotApply)
            {
                tried.Add($"{source.Name} ({outcome.Reason})");
                continue;
            }

            // Whatever is not an explicit "does not apply" ends the walk: this throws for a broken source.
            var credentials = outcome.GetCredentials(source.Name);

            // Only the guard has no provider, and it never has credentials.
            if (Interlocked.CompareExchange(ref _chosen, source.Provider!, null) is null)
            {
                CredentialsLog.SourceChosen(_logger, source.Name);
            }

            return credentials;
        }

        throw new CredentialsUnavailableException(
            "No AWS credentials were found. Set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, configure a profile in ~/.aws/credentials, or pass a credentials provider in the client options. " +
            $"Tried, in order: {string.Join("; ", tried)}.");
    }

    private sealed record Source(string Name, ICredentialsProvider? Provider, Func<CancellationToken, ValueTask<CredentialsOutcome>> TryGetCredentialsAsync);
}
