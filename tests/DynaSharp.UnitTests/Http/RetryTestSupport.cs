using System.Buffers;
using System.Globalization;
using System.Net;
using DynaSharp.Credentials;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Http;

/// <summary>Clients, canned responses and clock handling that the retry tests share.</summary>
internal static class RetryTestSupport
{
    /// <summary>Creates a client that retries (three attempts unless said otherwise) and, unless a jitter is given, never waits.</summary>
    public static DynamoDbClient CreateClient(
        HttpMessageHandler handler,
        TimeProvider? time = null,
        int maxAttempts = 3,
        Random? random = null,
        ArrayPool<byte>? pool = null,
        ILoggerFactory? loggerFactory = null,
        ICredentialsProvider? credentials = null,
        TimeSpan? attemptTimeout = null,
        Uri? serviceUrl = null) =>
        TestClient.Create(
            handler,
            time ?? new ManualTimeProvider(),
            pool,
            attemptTimeout,
            serviceUrl,
            credentials: credentials,
            loggerFactory: loggerFactory,
            maxAttempts: maxAttempts,
            random: random ?? FixedRandom.Zero);

    public static StubResponse Success() => StubResponse.Json(TestClient.GetItemResponseJson);

    public static StubResponse Throttled(string? requestId = null) =>
        StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "The level of configured provisioned throughput for the table was exceeded.", requestId: requestId);

    public static StubResponse ServerError(string? requestId = null) =>
        StubResponse.DynamoDbError("InternalServerError", "The server encountered an internal error trying to fulfill the request.", HttpStatusCode.InternalServerError, requestId);

    /// <summary>The answer of a service that rejects a request as signed with the wrong time and says in the Date header what time it is.</summary>
    public static StubResponse SkewRejection(DateTimeOffset serverTime, string errorCode = "InvalidSignatureException") => new()
    {
        StatusCode = HttpStatusCode.BadRequest,
        Body = StubResponse.DynamoDbError(errorCode, "Signature expired.").Body,
        Headers = new Dictionary<string, string> { ["Date"] = serverTime.ToString("R", CultureInfo.InvariantCulture) },
    };

    /// <summary>The page a proxy in front of DynamoDB answers with when the service is down.</summary>
    public static StubResponse ProxyUnavailable() =>
        StubResponse.Json("<html><body>503 Service Unavailable</body></html>", HttpStatusCode.ServiceUnavailable);

    /// <summary>
    /// Moves the clock to the next retry delay until the call is done, and returns how long each wait was. Only for calls whose
    /// attempts finish at once (a <see cref="StubHandler"/> that answers without waiting). A timer that is due in the timeout of an
    /// attempt or later is the timeout of an attempt and is left alone.
    /// </summary>
    /// <param name="call">The call to run to the end.</param>
    /// <param name="time">The clock of the client.</param>
    /// <param name="attemptTimeout">The attempt timeout of the client, which must be longer than every retry delay.</param>
    /// <returns>How long each wait was.</returns>
    public static async Task<IReadOnlyList<TimeSpan>> AdvanceThroughDelaysAsync(Task call, ManualTimeProvider time, TimeSpan attemptTimeout)
    {
        var delays = new List<TimeSpan>();
        for (var spins = 0; !call.IsCompleted; spins++)
        {
            if (spins > 1_000_000)
            {
                throw new InvalidOperationException("The call neither completed nor started a wait.");
            }

            if (time.NextDueIn is { } dueIn && dueIn < attemptTimeout)
            {
                delays.Add(dueIn);
                time.Advance(dueIn);
            }
            else
            {
                // The continuation after a wait runs on the thread that advanced the clock unless the runtime moved it to the pool.
                await Task.Yield();
            }
        }

        return delays;
    }

    /// <summary>Counts the attempts that reach a handler and lets a test wait for the nth one.</summary>
    internal sealed class AttemptCounter
    {
        private readonly TaskCompletionSource[] _reached;
        private int _count;

        public AttemptCounter(int expected)
        {
            _reached = [.. Enumerable.Range(0, expected).Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))];
        }

        public int Count => Volatile.Read(ref _count);

        public void Reached() => _reached[Interlocked.Increment(ref _count) - 1].TrySetResult();

        public Task WhenReachedAsync(int attempt) => _reached[attempt - 1].Task;
    }

    /// <summary>A credentials provider that counts how often it is asked.</summary>
    internal sealed class CountingCredentialsProvider : ICredentialsProvider
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(TestClient.Credentials);
        }
    }
}
