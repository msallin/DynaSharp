namespace DynaSharp.Testing;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers requests with canned responses instead of calling DynamoDB, and records
/// every request. Pass it as <see cref="DynamoDbClientOptions.HttpHandler"/> to test code that uses a <see cref="DynamoDbClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Testing code that uses a <see cref="Table{T}"/>.</b> Give the code under test a client over this handler, queue what DynamoDB answers, and look at <see cref="Requests"/>:
/// <c>StubClient.Create(stub, options => options with { TimeProvider = fake })</c>. <see cref="StubResponse"/> writes the answers from entities and
/// <see cref="RecordedRequest.ReadItem{T}()"/> reads them back from the requests, so a test holds no DynamoDB JSON. There is no network and no container. <see cref="Table{T}"/> is
/// sealed and has no interface on purpose: the handler is the seam, and a test through it covers the mapping and the expressions too. Code that wants to fake its data
/// access altogether puts an interface of its own around it.
/// </para>
/// <para>
/// The handler picks the response for a request in this order: the next response queued with <see cref="Enqueue"/>; the
/// responder function given to the constructor; <see cref="DefaultResponse"/>. If none applies, the request fails with an
/// <see cref="InvalidOperationException"/> that says which request had no response.
/// </para>
/// <para>
/// <b>A response of the wrong operation throws.</b> A <see cref="StubResponse"/> from a factory is the answer to the operations it is written for, and a request of another operation that takes it
/// fails with an <see cref="InvalidOperationException"/> that names both operations and the position of the response in the queue, from wherever the response came. So a test that queued the
/// answers in the wrong order, or one too few, fails at the request instead of passing on a body the client read without complaint. <see cref="StubResponse.Json"/>,
/// <see cref="StubResponse.DynamoDbError"/> and <c>new StubResponse()</c> fit any operation. A response left in the queue is no error, because a test may queue more than one path needs:
/// <see cref="ThrowIfResponsesLeft"/> at the end of a test checks that every one was taken.
/// </para>
/// <para>
/// The client signs and serializes requests as it would for DynamoDB, so the recorded requests show exactly what would go over the
/// wire. The handler does not validate signatures or requests. Responses are returned as they are given, which also lets tests
/// produce errors and broken responses.
/// </para>
/// <para>
/// The client retries throttling errors, server errors (HTTP 500, 502, 503, 504), network failures and timeouts, up to
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> attempts in all. Every attempt is a request to this handler: <see cref="Requests"/>
/// lists them all, and each needs a response, so a test that simulates a failure must queue as many failures as it expects attempts
/// (or set a <see cref="DefaultResponse"/>), followed by the response that ends the retries. <see cref="StubClient"/> sets
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1, which shows exactly one request per call. The client waits a random time up to
/// 20 seconds between attempts on its <see cref="DynamoDbClientOptions.TimeProvider"/>, so give the client a fake time provider to
/// test retries without waiting.
/// </para>
/// <para>
/// <b>Testing unprocessed items.</b> A batch sends what DynamoDB left unprocessed again by itself, also with <see cref="DynamoDbClientOptions.MaxAttempts"/> at 1, which bounds
/// the attempts of a request and not the rounds of a batch. Each round is a request to this handler, so queue
/// <see cref="StubResponse.Unprocessed(string, ReadOnlySpan{Requests.WriteRequest})"/> for it, and what ends the batch after that. Before the next round the batch waits on the same
/// clock, for a random time below 20 seconds, so with a fake clock it stops there until the test moves the clock by that much, as the second example does. A wait can round
/// down to nothing, so a test does not count on one. <c>FakeTimeProvider</c> of <c>Microsoft.Extensions.TimeProvider.Testing</c> is such a clock. A test of what follows a batch that gives up
/// sets <see cref="BatchWriteCommand{T}.MaxStalledRounds"/> to 1 on the command: the first round that gets nothing done then ends the batch, with no wait and no fake clock.
/// </para>
/// <para>
/// The handler is thread-safe. The client does not dispose a handler it was given, so a test can keep using it afterwards.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var handler = new StubHandler();
/// handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"user-1"}}}"""));
/// handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found."));
///
/// using var client = StubClient.Create(handler);
/// // ... call the code under test, then:
/// var operations = handler.Requests.Select(request => request.Operation);
/// </code>
/// A batch that DynamoDB takes in two rounds, tested without waiting for the second one:
/// <code>
/// handler.Enqueue(StubResponse.Unprocessed("Orders", StubResponse.Put(order)));
/// handler.Enqueue(StubResponse.Unprocessed("Orders"));
///
/// var import = service.ImportAsync(orders);
/// while (!import.IsCompleted)
/// {
///     clock.Advance(TimeSpan.FromSeconds(20));
///     await Task.Yield();
/// }
///
/// await import;
/// </code>
/// </example>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly Queue<StubResponse> _queued = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly Func<RecordedRequest, CancellationToken, Task<StubResponse>>? _responder;

    // How many queued responses requests have taken, so that a message gives a response its position in the order queued.
    private int _taken;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHandler"/> class that answers with the responses queued by <see cref="Enqueue"/>.
    /// </summary>
    public StubHandler()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHandler"/> class that asks a function for the response when no response is queued.
    /// </summary>
    /// <param name="responder">Returns the response for a request. It may throw, for example an <see cref="HttpRequestException"/> to simulate a network error.</param>
    /// <exception cref="ArgumentNullException"><paramref name="responder"/> is <see langword="null"/>.</exception>
    public StubHandler(Func<RecordedRequest, StubResponse> responder)
        : this(ToAsync(responder))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHandler"/> class that asks an asynchronous function for the response when no response is queued.
    /// </summary>
    /// <param name="responder">
    /// Returns the response for a request. It receives the cancellation token of the request, so a responder that waits can be used to
    /// test timeouts and cancellation.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="responder"/> is <see langword="null"/>.</exception>
    public StubHandler(Func<RecordedRequest, CancellationToken, Task<StubResponse>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _responder = responder;
    }

    /// <summary>
    /// Gets or sets the response for requests that no queued response and no responder answers. <see langword="null"/> (the default)
    /// makes such a request fail with an <see cref="InvalidOperationException"/>.
    /// </summary>
    public StubResponse? DefaultResponse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether requests are recorded in <see cref="Requests"/>. Defaults to <see langword="true"/>.
    /// Turn it off for benchmarks and load tests: recording copies each request body and keeps it in memory.
    /// </summary>
    public bool RecordRequests { get; set; } = true;

    /// <summary>
    /// Gets the requests received so far, oldest first. The list is a snapshot; get it again after more requests.
    /// </summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Queues a response for a future request. Each queued response answers exactly one request, in the order queued, whatever its operation: a response of a <see cref="StubResponse"/> factory that is
    /// the answer to another operation makes the request throw.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    public void Enqueue(StubResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            _queued.Enqueue(response);
        }
    }

    /// <summary>
    /// Throws if responses queued with <see cref="Enqueue"/> are still waiting for a request, which means that the code under test sent fewer requests than the test expected. Call it at the end of a test
    /// that wants to know; a leftover is no error otherwise, and <see cref="HttpMessageHandler.Dispose()"/> does not check.
    /// </summary>
    /// <exception cref="InvalidOperationException">A queued response is left. The message gives the position of each in the order queued, and the operations it answers.</exception>
    public void ThrowIfResponsesLeft()
    {
        string? message = null;
        lock (_gate)
        {
            if (_queued.Count > 0)
            {
                var left = new List<string>(_queued.Count);
                var position = _taken;
                foreach (var response in _queued)
                {
                    left.Add($"response {++position}, the answer to {response.DescribeOperations()}");
                }

                message = $"StubHandler has {(left.Count == 1 ? "1 queued response" : $"{left.Count} queued responses")} that no request took: {string.Join("; ", left)}. " +
                    "The code under test sent fewer requests than the test queued answers for.";
            }
        }

        if (message is not null)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecordedRequest? recorded = null;
        if (RecordRequests || _responder is not null)
        {
            recorded = await RecordedRequest.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        }

        StubResponse? response;
        var position = 0;
        lock (_gate)
        {
            if (RecordRequests)
            {
                _requests.Add(recorded!);
            }

            if (_queued.TryDequeue(out response))
            {
                position = ++_taken;
            }
        }

        var fromResponder = false;
        if (response is null && _responder is not null)
        {
            response = await _responder(recorded!, cancellationToken).ConfigureAwait(false);
            fromResponder = response is not null;
        }

        response ??= DefaultResponse;
        if (response is null)
        {
            throw new InvalidOperationException(
                $"StubHandler received {request.Method} {request.RequestUri} (operation {RecordedRequest.GetOperation(request) ?? "unknown"}) " +
                "but has no response for it. Queue one with Enqueue before the request, give the handler a responder function, or set DefaultResponse.");
        }

        // A response that fits any operation is not compared, so that a benchmark over Json answers reads no header.
        if (response.Operations is not null)
        {
            var operation = recorded is null ? RecordedRequest.GetOperation(request) : recorded.Operation;
            if (!response.Answers(operation))
            {
                var source = position > 0 ? $"queued response {position}" : fromResponder ? "the response of its responder" : nameof(DefaultResponse);
                var target = operation is null ? "a request without X-Amz-Target" : $"a request to {operation}";
                throw new InvalidOperationException(
                    $"StubHandler took {source} for {target}, but that response is the answer to {response.DescribeOperations()}. " +
                    "A test that went on with it would pass for the wrong reason. Give the request the answer of its own operation, or StubResponse.Json for a body that fits any.");
            }
        }

        return response.CreateMessage();
    }

    private static Func<RecordedRequest, CancellationToken, Task<StubResponse>> ToAsync(Func<RecordedRequest, StubResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        return (request, _) => Task.FromResult(responder(request));
    }
}
