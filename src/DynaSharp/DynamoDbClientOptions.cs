using System.Text;
using DynaSharp.Credentials;
using Microsoft.Extensions.Logging;

namespace DynaSharp;

/// <summary>
/// Configures a <see cref="DynamoDbClient"/> or a <see cref="Streams.DynamoDbStreamsClient"/>. Every setting is optional; unset settings
/// are resolved from the environment the same way the AWS SDKs do.
/// </summary>
/// <remarks>
/// The options are a record, so a variation of a shared configuration is a <c>with</c> expression, for example
/// <c>options with { MaxAttempts = 1 }</c> for a client that must not retry its updates. The copy is shallow: it shares the credentials
/// provider, the HTTP handler and the logger factory of the original. <see cref="DynamoDbClient.Options"/> gives these options back as a client resolved them. <see cref="ToString"/> names the type of the
/// provider and nothing it holds, whatever the provider prints, so options are safe to log. What the client does with the settings while an operation runs,
/// that is retries, timeouts, errors and telemetry, is described on <see cref="Requests.DynamoDbRequests"/>.
/// </remarks>
public sealed record DynamoDbClientOptions
{
    /// <summary>
    /// Gets the region. When <see langword="null"/>, the <c>AWS_REGION</c> and then the <c>AWS_DEFAULT_REGION</c>
    /// environment variable is used, and then the <c>region</c> setting of the profile selected by <c>AWS_PROFILE</c> (default
    /// <c>default</c>) in the shared AWS files (<c>~/.aws/config</c>, <c>AWS_CONFIG_FILE</c>). A missing, unreadable or malformed
    /// file does not fail the client unless no region can be found at all. The region is required even with
    /// <see cref="ServiceUrl"/>, because it is part of the request signature.
    /// </summary>
    public AwsRegion? Region { get; init; }

    /// <summary>
    /// Gets an explicit endpoint, for example <c>http://localhost:8000</c> for DynamoDB Local. When <see langword="null"/>,
    /// the <c>AWS_ENDPOINT_URL_DYNAMODB</c> and then the <c>AWS_ENDPOINT_URL</c> environment variable is used, and
    /// otherwise the public endpoint of the region (<see cref="AwsRegion.DynamoDbEndpoint"/>). It must be an absolute http or
    /// https URL without user info, path, query and fragment; the client rejects anything else when it is created.
    /// A <see cref="Streams.DynamoDbStreamsClient"/> created from the same options uses the same URL (an emulator such as DynamoDB Local
    /// serves both APIs on one address); without it, it reads <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c>, then
    /// <c>AWS_ENDPOINT_URL</c>, and otherwise uses <see cref="AwsRegion.DynamoDbStreamsEndpoint"/>.
    /// </summary>
    public Uri? ServiceUrl { get; init; }

    /// <summary>
    /// Gets the credentials provider. When <see langword="null"/>, the client uses the default chain of the AWS SDKs
    /// (<see cref="DefaultCredentialsProvider"/>), which takes the first source that has credentials: the
    /// <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c> and <c>AWS_SESSION_TOKEN</c> environment variables; the web identity
    /// token file named by <c>AWS_WEB_IDENTITY_TOKEN_FILE</c>, which is how IAM roles for service accounts work on EKS; the profile
    /// selected by <c>AWS_PROFILE</c> in the shared credentials and config files; the container credentials endpoint of ECS and
    /// EKS; the instance metadata service of EC2. Nothing is looked up before the first request, and a source that applies but
    /// is broken fails the request instead of falling through to the next one. To use one source only, or to turn off the
    /// network lookups, pass an <see cref="EnvironmentCredentialsProvider"/>, <see cref="WebIdentityCredentialsProvider"/>, <see cref="ProfileCredentialsProvider"/>,
    /// <see cref="ContainerCredentialsProvider"/>, <see cref="InstanceMetadataCredentialsProvider"/> or
    /// <see cref="StaticCredentialsProvider"/>.
    /// </summary>
    /// <remarks>
    /// Every client that is created without this setting makes its own <see cref="DefaultCredentialsProvider"/>, with its own
    /// cache of credentials. On Amazon EC2, ECS and EKS that means one set of requests to the metadata service, the container endpoint or STS,
    /// and one refresh before expiry, per client; a <see cref="DynamoDbClient"/> and a <see cref="Streams.DynamoDbStreamsClient"/> already
    /// make two. An application that creates more than one client there should create one provider and pass it in the options of all
    /// of them: providers are thread-safe, and the clients then share the cached credentials. A provider that the application creates
    /// does not log, whereas the one a client makes logs to <see cref="LoggerFactory"/>. It also judges the expiry of credentials on the
    /// local clock, whereas the one a client makes uses the clock that the client corrects from the responses of DynamoDB (see
    /// <see cref="MaxAttempts"/>), so that a local clock that is behind does not delay a refresh. The client never disposes a provider it was given.
    /// </remarks>
    public ICredentialsProvider? Credentials { get; init; }

    /// <summary>
    /// Gets the HTTP handler that sends requests. This is the seam for tests and for wire inspection. The client does not
    /// dispose a handler it was given. When <see langword="null"/>, the client creates and owns a pooled
    /// <see cref="SocketsHttpHandler"/>.
    /// </summary>
    /// <remarks>
    /// It carries the requests to DynamoDB, and no others. The providers of credentials have HTTP clients of their own, for the token of <see cref="Credentials.WebIdentityCredentialsProvider"/> and the
    /// keys of the container and metadata endpoints, so a client with a stub here still calls the real STS or metadata endpoint when the environment selects that provider. A test sets <see cref="Credentials"/> as well.
    /// </remarks>
    public HttpMessageHandler? HttpHandler { get; init; }

    /// <summary>
    /// Gets the timeout of a single attempt, from looking up the credentials to reading the whole response, so a slow credentials
    /// provider counts against it. Retries get a fresh timeout each. Defaults to 30 seconds. An attempt that times out surfaces as <see cref="TimeoutException"/>, never as
    /// <see cref="OperationCanceledException"/>. Use <see cref="Timeout.InfiniteTimeSpan"/> for no timeout; otherwise the value must
    /// be positive and at most 4294967294 milliseconds (about 49.7 days).
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the maximum number of attempts per operation, including the first one. Defaults to 4: three retries, so that a throttled call
    /// waits for the table to recover (at most 50, 100 and 200 ms before the retries) instead of giving up after the first short
    /// pause. Use 1 to send every request once and never retry. The value must be at least 1; the client rejects anything else when
    /// it is created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client retries with the "standard" retry mode of the AWS SDKs, tuned for DynamoDB. It repeats a request when an attempt
    /// fails with a throttling error (<see cref="ProvisionedThroughputExceededException"/>, <see cref="ThrottlingException"/>,
    /// <see cref="RequestLimitExceededException"/>, <see cref="LimitExceededException"/>, <see cref="TransactionInProgressException"/>),
    /// with <see cref="ReplicatedWriteConflictException"/> or <c>RequestTimeoutException</c>, with HTTP 500, 502, 503 or 504 (including
    /// <see cref="InternalServerErrorException"/> and a proxy answering 503 with a page that is not a DynamoDB response), with a network
    /// failure (<see cref="HttpRequestException"/>), or when the attempt exceeds <see cref="AttemptTimeout"/>. It never retries a
    /// cancellation, <see cref="CredentialsUnavailableException"/>, <see cref="ValidationException"/>,
    /// <see cref="ConditionalCheckFailedException"/>, <see cref="TransactionCanceledException"/>,
    /// <see cref="TransactionConflictException"/>, <see cref="AccessDeniedException"/>, any other 4xx response or a response body above
    /// 64 MB. After the last attempt the error of that attempt surfaces unchanged.
    /// </para>
    /// <para>
    /// Before a retry the client waits a random time between zero and <c>min(20 s, base * 2^(attempt - 1))</c>, with a base of 50 ms
    /// for throttling errors and 100 ms for everything else. Each attempt has its own <see cref="AttemptTimeout"/> and looks up its
    /// credentials again, so an operation can take up to <c>MaxAttempts * AttemptTimeout</c> plus the waits. Cancelling the call
    /// ends a wait at once.
    /// </para>
    /// <para>
    /// A failure that comes back at once and is not throttling, such as a refused connection, uses up the default of 4 attempts within about a second, because the three waits are at most
    /// 100, 200 and 400 ms: a window of 700 ms at most and 350 ms on average, against 350 ms at most for throttling. That is the time a dependency has to come up. A test fixture that waits
    /// on a TCP port can still fail inside that window when a proxy opens the port before the service listens: wait on an HTTP response.
    /// </para>
    /// <para>
    /// Every client has a retry quota of 500 tokens that is shared by all its operations. A retry takes 5 tokens (10 after a timeout);
    /// a successful request gives back 1, or what its last retry took. When the quota is empty, failed requests are no longer
    /// retried and their errors surface at once, until successful requests have refilled it. This keeps a client from adding load
    /// to a service that is struggling.
    /// </para>
    /// <para>
    /// A request is sent more than once, so it can take effect more than once: when an attempt reached DynamoDB but its response was
    /// lost (a timeout, a dropped connection), the retry repeats a write that was already applied. Idempotent writes such as
    /// <c>PutItem</c> or <c>DeleteItem</c> are safe to repeat; updates that depend on the current value (<c>ADD</c> on a number,
    /// <c>list_append</c>, <c>SET x = x + :n</c>) are applied twice. Guard such updates with a condition expression, for example on
    /// a version attribute. Setting <see cref="MaxAttempts"/> to 1 makes them at most once, not exact: a lost response then reports a failure after the update was applied.
    /// </para>
    /// <para>
    /// If DynamoDB rejects a request as signed with a wrong time (the local clock is more than 4 minutes off) and its <c>Date</c>
    /// header shows the difference, the client remembers the offset, signs all later requests with the corrected time and repeats
    /// the request at once. That repeat counts as an attempt but uses no wait and no quota.
    /// </para>
    /// <para>
    /// If DynamoDB rejects the credentials of a request as expired (<c>ExpiredTokenException</c>) and the provider can replace them, which
    /// is the case for an <see cref="IRefreshableCredentialsProvider"/> such as <see cref="ContainerCredentialsProvider"/> and
    /// <see cref="InstanceMetadataCredentialsProvider"/>, the client lets it drop them and repeats the request at once. That repeat follows
    /// the same rules: it counts as an attempt but uses no wait and no quota. It happens once per call, so if the next credentials are rejected
    /// as well, the error surfaces. Credentials are dropped even when no attempt is left, so that the next call does not use them. Concurrent
    /// requests that fail like this share one fetch. If the fetch fails, its <see cref="CredentialsUnavailableException"/> surfaces.
    /// With a provider that cannot replace credentials, such as <see cref="StaticCredentialsProvider"/>,
    /// <see cref="EnvironmentCredentialsProvider"/> or <see cref="ProfileCredentialsProvider"/>, the error surfaces unchanged after one attempt.
    /// </para>
    /// </remarks>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>
    /// Gets the local clock, used for request signatures, timeouts and retry delays. Signatures and the expiry of the credentials of the
    /// default chain use it plus the correction that the client learned, see <see cref="MaxAttempts"/>. Defaults to <see cref="TimeProvider.System"/>.
    /// It also runs <see cref="AttemptTimeout"/>, so a fake clock that a test moves can time out a request in flight: such a test sets <see cref="AttemptTimeout"/> to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the logger factory. When <see langword="null"/>, nothing is logged.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }

    // A record prints what its members print, and a provider of the application can be a record itself, which prints its keys. An endpoint can carry user info until a client rejects it.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Region = ").Append(Region)
            .Append(", ServiceUrl = ").Append(ServiceUrl is { IsAbsoluteUri: true } url ? url.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) : ServiceUrl?.ToString())
            .Append(", Credentials = ").Append(Credentials?.GetType())
            .Append(", HttpHandler = ").Append(HttpHandler?.GetType())
            .Append(", AttemptTimeout = ").Append(AttemptTimeout)
            .Append(", MaxAttempts = ").Append(MaxAttempts)
            .Append(", TimeProvider = ").Append(TimeProvider?.GetType())
            .Append(", LoggerFactory = ").Append(LoggerFactory?.GetType());
        return true;
    }
}
