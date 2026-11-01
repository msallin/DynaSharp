using DynaSharp.Http;

namespace DynaSharp.Testing;

/// <summary>
/// A <see cref="DelegatingHandler"/> that passes requests on to an inner handler and records each exchange, so a test can inspect the
/// traffic of a real or emulated DynamoDB: the signed request and the response as they went over the wire. Pass it as
/// <see cref="DynamoDbClientOptions.HttpHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recordings contain data you may not want to keep.</b> Requests carry the items you write and, in the <c>Authorization</c> and
/// <c>X-Amz-Security-Token</c> headers, the access key id and the session token (the signature does not reveal the secret
/// key, but a valid session token is a credential). Responses carry the items you read. Do not log, print or publish recordings
/// of real traffic, and do not attach them to bug reports without removing both.
/// </para>
/// <para>
/// The response body is read into memory before the client sees it and replaced by a copy, so the client reads it as usual. That
/// makes every call wait for the whole body and keeps it in memory: use the handler to look at traffic in a test, not in production
/// code or load tests.
/// </para>
/// <para>
/// The client retries throttling errors and server errors, so one call can be several exchanges: <see cref="Exchanges"/> lists
/// every attempt. The handler is thread-safe; with concurrent calls, exchanges are listed in the order they completed.
/// </para>
/// <para>
/// Disposing this handler disposes the inner handler, as for every <see cref="DelegatingHandler"/>. The client does not dispose a
/// handler it was given, so dispose the recording handler when the test is done.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var recorder = new RecordingHandler(); // sends to the real service with a pooled SocketsHttpHandler
/// using var client = new DynamoDbClient(new DynamoDbClientOptions { HttpHandler = recorder });
/// await client.GetItemAsync(request);
/// var wire = recorder.Exchanges.Single().Response!.BodyText;
/// </code>
/// </example>
public sealed class RecordingHandler : DelegatingHandler
{
    private readonly Lock _gate = new();
    private readonly List<RecordedExchange> _exchanges = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingHandler"/> class that sends requests with a
    /// <see cref="SocketsHttpHandler"/> of its own, configured as the client configures the one it owns. Disposing this handler
    /// disposes it.
    /// </summary>
    public RecordingHandler()
#pragma warning disable CA2000 // The base class takes the handler and disposes it.
        : base(ClientCore.CreateOwnedHandler())
#pragma warning restore CA2000
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordingHandler"/> class that sends requests with <paramref name="innerHandler"/>,
    /// for example a <see cref="StubHandler"/> to look at what the client sends and receives in a test with canned responses.
    /// Disposing this handler disposes <paramref name="innerHandler"/>.
    /// </summary>
    /// <param name="innerHandler">The handler that sends the requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerHandler"/> is <see langword="null"/>.</exception>
    public RecordingHandler(HttpMessageHandler innerHandler)
        : base(Require(innerHandler))
    {
    }

    /// <summary>
    /// Gets the exchanges recorded so far, oldest first. The list is a snapshot; get it again after more requests.
    /// </summary>
    public IReadOnlyList<RecordedExchange> Exchanges
    {
        get
        {
            lock (_gate)
            {
                return [.. _exchanges];
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recordedRequest = await RecordedRequest.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var recordedResponse = await RecordedResponse.BufferAsync(response, cancellationToken).ConfigureAwait(false);
            Add(new RecordedExchange(recordedRequest, recordedResponse, error: null));
            return response;
        }
        catch (Exception exception)
        {
            response?.Dispose();
            Add(new RecordedExchange(recordedRequest, response: null, exception));
            throw;
        }
    }

    private static HttpMessageHandler Require(HttpMessageHandler innerHandler)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        return innerHandler;
    }

    private void Add(RecordedExchange exchange)
    {
        lock (_gate)
        {
            _exchanges.Add(exchange);
        }
    }
}
