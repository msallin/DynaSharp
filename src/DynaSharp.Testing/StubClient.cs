using DynaSharp.Credentials;
using DynaSharp.Streams;

namespace DynaSharp.Testing;

/// <summary>
/// Creates a client for a test: over a handler of the test, in the fixed region <c>us-east-1</c>, with static fake credentials, one attempt per call and no attempt timeout. It reads nothing from the
/// environment, so a test behaves the same on a machine that has <c>AWS_REGION</c>, <c>AWS_ENDPOINT_URL</c> or a shared AWS file and on one that has none.
/// </summary>
/// <remarks>
/// <para>
/// The second argument adjusts the options the way a variation of <see cref="DynamoDbClientOptions"/> is written, with a <c>with</c> expression:
/// <c>StubClient.Create(stub, options => options with { MaxAttempts = 4, TimeProvider = clock })</c>. The client validates the result as it validates any options, and a <see langword="null"/> from the delegate
/// throws <see cref="ArgumentNullException"/>. Leave <see cref="DynamoDbClientOptions.Credentials"/> set: without it the client makes the default chain, which ends at the metadata endpoints on the network.
/// </para>
/// <para>
/// There is no attempt timeout because <see cref="DynamoDbClientOptions.AttemptTimeout"/> runs on <see cref="DynamoDbClientOptions.TimeProvider"/>: a fake clock that a test moves past it would time out the request
/// the test is answering. So a test of a timeout sets one. A test that asks a responder of a <see cref="StubHandler"/> to wait for the cancellation of a call needs it, or it waits for ever.
/// </para>
/// <para>
/// The timeout of a wait for a table, <c>WaitUntilActiveAsync</c> and <c>WaitUntilActive()</c>, runs on that clock as well, next to the delays between its polls. So a loop that advances a fake clock and calls
/// <see cref="Task.Yield"/> until the wait ends burns the whole timeout, and the wait throws <see cref="TableNotActiveException"/> with answers still in the queue. Give such a wait a timeout that the loop does not reach,
/// or advance the clock once for each poll the handler has seen.
/// </para>
/// <para>The client disposes neither the handler nor anything else it was given. A <see langword="using"/> on the client and one on the handler are both needed.</para>
/// </remarks>
/// <example>
/// <code>
/// using var stub = new StubHandler();
/// using var client = StubClient.Create(stub);
/// stub.Enqueue(StubResponse.Item(order));
/// </code>
/// </example>
public static class StubClient
{
    /// <summary>The region of the clients, which the ARNs in the answers of <see cref="StubResponse"/> name as well.</summary>
    internal const string RegionName = "us-east-1";

    /// <summary>Creates a <see cref="DynamoDbClient"/> over a handler.</summary>
    /// <param name="handler">Answers the requests, normally a <see cref="StubHandler"/>.</param>
    /// <param name="configure">Adjusts the options with a <c>with</c> expression, or <see langword="null"/> to keep them.</param>
    /// <returns>The client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>, or <paramref name="configure"/> returns <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="configure"/> sets an <see cref="DynamoDbClientOptions.AttemptTimeout"/> or a <see cref="DynamoDbClientOptions.MaxAttempts"/> the client rejects.</exception>
    /// <exception cref="ArgumentException"><paramref name="configure"/> sets a <see cref="DynamoDbClientOptions.ServiceUrl"/> the client rejects.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="configure"/> clears the region. The environment does not fill it in.</exception>
    public static DynamoDbClient Create(HttpMessageHandler handler, Func<DynamoDbClientOptions, DynamoDbClientOptions>? configure = null) =>
        new(Options(handler, configure), static _ => null, null, null, readFile: static _ => null);

    /// <summary>Creates a <see cref="DynamoDbStreamsClient"/> over a handler, for the answers of DynamoDB Streams.</summary>
    /// <param name="handler">Answers the requests, normally a <see cref="StubHandler"/>.</param>
    /// <param name="configure">Adjusts the options with a <c>with</c> expression, or <see langword="null"/> to keep them.</param>
    /// <returns>The client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>, or <paramref name="configure"/> returns <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="configure"/> sets an <see cref="DynamoDbClientOptions.AttemptTimeout"/> or a <see cref="DynamoDbClientOptions.MaxAttempts"/> the client rejects.</exception>
    /// <exception cref="ArgumentException"><paramref name="configure"/> sets a <see cref="DynamoDbClientOptions.ServiceUrl"/> the client rejects.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="configure"/> clears the region. The environment does not fill it in.</exception>
    public static DynamoDbStreamsClient CreateStreams(HttpMessageHandler handler, Func<DynamoDbClientOptions, DynamoDbClientOptions>? configure = null) =>
        new(Options(handler, configure), static _ => null, null, null, readFile: static _ => null);

    private static DynamoDbClientOptions Options(HttpMessageHandler handler, Func<DynamoDbClientOptions, DynamoDbClientOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var options = new DynamoDbClientOptions
        {
            Region = AwsRegion.FromName(RegionName),
            Credentials = new StaticCredentialsProvider("test", "test"),
            HttpHandler = handler,
            AttemptTimeout = Timeout.InfiniteTimeSpan,
            MaxAttempts = 1,
        };
        return configure is null ? options : configure(options);
    }
}
