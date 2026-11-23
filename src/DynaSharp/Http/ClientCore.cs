using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DynaSharp.Configuration;
using DynaSharp.Fluent;
using DynaSharp.Retries;
using DynaSharp.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Http;

/// <summary>
/// What <see cref="DynamoDbClient"/> and <see cref="Streams.DynamoDbStreamsClient"/> share: the resolved configuration, the HTTP client with
/// its handler, the pipeline and the disposed state. The two clients differ only in the endpoint, the target prefix of their
/// operations and the names in their telemetry.
/// </summary>
internal sealed class ClientCore : IDisposable
{
    private static readonly SearchValues<char> OperationNameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

    private readonly HttpClient _http;
    private readonly DynamoDbPipeline _pipeline;
    private readonly Type _clientType;
    private readonly string _targetPrefix;
    private readonly string _serviceName;
    private bool _disposed;

    private ClientCore(HttpClient http, DynamoDbPipeline pipeline, RetryPolicy retries, ArrayPool<byte> pool, Type clientType, AwsService service, TimeProvider timeProvider, DynamoDbClientOptions options, ILogger logger)
    {
        TimeProvider = timeProvider;
        Logger = logger;
        Options = options;
        Retries = retries;
        _http = http;
        _pipeline = pipeline;
        Commands = new CommandPool(this, pool);
        _clientType = clientType;

        // What the operations of the library say for themselves, for the calls that do not come from one (SendRawAsync).
        var streams = service == AwsService.DynamoDbStreams;
        _targetPrefix = streams ? WireProtocol.StreamsTargetPrefix : WireProtocol.TargetPrefix;
        _serviceName = streams ? TelemetryNames.RpcServiceStreamsValue : TelemetryNames.RpcServiceValue;
    }

    /// <summary>Gets the clock of the client, for what waits outside the pipeline.</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>Gets the retry policy of the client, whose backoffs the waits outside a call use too (batches, conflict retries, shards of a stream).</summary>
    public RetryPolicy Retries { get; }

    /// <summary>Gets the logger of the client, for the events outside a single request that the layers above the pipeline report.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets the idle states of the commands of this client.</summary>
    public CommandPool Commands { get; }

    /// <summary>Gets the options as they were resolved, once, when the client was created, see <see cref="DynamoDbClient.Options"/>.</summary>
    public DynamoDbClientOptions Options { get; }

    /// <summary>Gets what the client reports about its operations, see <see cref="DynamoDbPipeline.Telemetry"/>.</summary>
    public OperationTelemetry Telemetry => _pipeline.Telemetry;

    /// <summary>Gets or sets the <see cref="DynamoDbClient"/> this is the core of, which its tables hand out; set once by its constructor, and null for a streams client.</summary>
    public DynamoDbClient? Client { get; set; }

    /// <summary>Resolves the configuration and creates the HTTP client and the pipeline.</summary>
    /// <typeparam name="TClient">The public client, which is the category of its logger.</typeparam>
    /// <param name="service">The service the client talks to.</param>
    /// <param name="options">The options.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="pool">The buffer pool, or <see langword="null"/> for the shared one.</param>
    /// <param name="activitySource">The activity source, or <see langword="null"/> for the library's.</param>
    /// <param name="random">The source of the retry jitter, or <see langword="null"/> for the shared one.</param>
    /// <param name="readFile">Reads the shared AWS files, or <see langword="null"/> to read from disk.</param>
    /// <param name="maxResponseLength">The most bytes a response body may have.</param>
    /// <param name="credentialsHandler">Answers for the credential endpoints of the default chain, or <see langword="null"/> to call them.</param>
    /// <returns>The core.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Owned by the HttpClient (disposeHandler: true).")]
    public static ClientCore Create<TClient>(
        AwsService service,
        DynamoDbClientOptions options,
        Func<string, string?> getEnvironmentVariable,
        ArrayPool<byte>? pool,
        ActivitySource? activitySource,
        Random? random,
        ReadTextFile? readFile,
        int maxResponseLength = DynamoDbPipeline.MaxResponseLength,
        HttpMessageHandler? credentialsHandler = null)
    {
        var configuration = ClientConfiguration.Resolve(options, getEnvironmentVariable, readFile, service, credentialsHandler);
        var logger = configuration.LoggerFactory.CreateLogger<TClient>();
        LogConfiguration(configuration, logger);

        // The client has no overall timeout: the attempt timeout is enforced per attempt.
        var owned = options.HttpHandler is null;
        var handler = options.HttpHandler ?? CreateOwnedHandler();
        var http = new HttpClient(handler, disposeHandler: owned) { Timeout = Timeout.InfiniteTimeSpan };
        var buffers = pool ?? ArrayPool<byte>.Shared;
        var retries = new RetryPolicy(configuration.MaxAttempts, random ?? Random.Shared, new RetryQuota());
        var pipeline = new DynamoDbPipeline(configuration, http, buffers, activitySource ?? DynaSharpTelemetry.ActivitySource, logger, retries, maxResponseLength);
        var resolved = options with { Region = configuration.Region, ServiceUrl = configuration.ChosenEndpoint, Credentials = configuration.Credentials };
        return new ClientCore(http, pipeline, retries, buffers, typeof(TClient), service, configuration.TimeProvider, resolved, logger);
    }

    /// <summary>
    /// Runs a call of any kind on the pipeline, for the layers that build their own (see <see cref="IWireCall{TResponse}"/>). Not
    /// asynchronous itself: a disposed client throws at the call, not in the returned task. The call is released in either case, so a
    /// call that holds a pooled state does not leak it.
    /// </summary>
    /// <typeparam name="TCall">The struct that implements the call.</typeparam>
    /// <typeparam name="TResponse">The response type of the call.</typeparam>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response. A task that the caller awaits exactly once.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    public ValueTask<TResponse> SendAsync<TCall, TResponse>(TCall call, CancellationToken cancellationToken)
        where TCall : struct, IWireCall<TResponse>
    {
        if (_disposed)
        {
            call.Release();
            throw new ObjectDisposedException(_clientType.FullName);
        }

        return _pipeline.SendAsync<TCall, TResponse>(call, cancellationToken);
    }

    /// <summary>Waits on the clock of the client, as the pipeline waits between two attempts, for the layers that wait outside a call: a cancellation throws an <see cref="OperationCanceledException"/> with the caller's token.</summary>
    /// <param name="delay">How long, usually a backoff of <see cref="Retries"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => DynamoDbPipeline.DelayAsync(delay, TimeProvider, cancellationToken);

    /// <summary>Sends an operation by name with a request the caller wrote, and returns the response as JSON. See <see cref="DynamoDbClient.SendRawAsync"/>.</summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="requestJson">The request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty or has a character other than an ASCII letter or digit.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    public Task<byte[]> SendRawAsync(string operation, ReadOnlyMemory<byte> requestJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);

        // The name becomes a header value, a span name and a metric tag: the operations of the service are letters and digits only.
        if (operation.AsSpan().ContainsAnyExcept(OperationNameCharacters))
        {
            throw new ArgumentException("The operation name must consist of ASCII letters and digits, for example 'DescribeLimits'.", nameof(operation));
        }

        return SendAsync<RawCall, byte[]>(new RawCall(operation, requestJson, _targetPrefix, _serviceName), cancellationToken).AsTask();
    }

    /// <summary>Releases the HTTP client, and with it the handler if the client owns it. Calling it again has no effect.</summary>
    public void Dispose()
    {
        _disposed = true;
        Commands.Close();
        _pipeline.Dispose();
        _http.Dispose();
    }

    // Connections are recycled so that DNS changes are noticed; DynamoDB endpoints change addresses regularly.
    // Redirects are never followed: DynamoDB does not redirect, and a redirect would send the signed body elsewhere.
    internal static SocketsHttpHandler CreateOwnedHandler() => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = false,
        UseCookies = false,
    };

    private static void LogConfiguration(ClientConfiguration configuration, ILogger logger)
    {
        ClientLog.RegionResolved(logger, configuration.Region.Name, configuration.RegionSource);
        ClientLog.EndpointResolved(logger, configuration.Endpoint, configuration.EndpointSource);
        if (ClientConfiguration.IsUnencryptedRemote(configuration.Endpoint))
        {
            ClientLog.UnencryptedEndpoint(logger, configuration.Endpoint.Host);
        }

        var credentialsType = configuration.Credentials.GetType();
        ClientLog.CredentialsProviderConfigured(logger, credentialsType);
    }
}
