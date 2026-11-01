using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Http;

// The pipeline reuses three things between calls: the timeout source of an attempt, and the writer and the buffer objects of a call.
// One client runs thousands of calls at once, every call ending in another way, and each call checks that what it sent and what it got
// is its own. A call that met a cancelled source, a half-written body or a response of another call would fail here. Nothing waits: the
// handler yields instead of sleeping, and the clock is the real one with a timeout no call comes near.
public sealed class DynamoDbPipelineStressTests
{
    private const int Calls = 3000;
    private const int Parallelism = 16;

    [Test]
    public async Task ExecuteAsync_ManyConcurrentCallsWithEveryKindOfOutcome_EveryCallEndsAsItShould()
    {
        // Arrange
        var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.Mismatches).IsEmpty();
    }

    [Test]
    public async Task ExecuteAsync_ManyConcurrentCallsWithEveryKindOfOutcome_ReturnsEveryRentedArrayOnce()
    {
        // Arrange
        var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That((run.Pool.Outstanding, run.Pool.Returned > 0)).IsEqualTo((0, true));
    }

    [Test]
    public async Task ExecuteAsync_ManyConcurrentCallsWithEveryKindOfOutcome_NoCallIsSentTwiceWithoutARetry()
    {
        // Arrange
        var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.UnexpectedExtraAttempts).IsEmpty();
    }

    // One run of the client, the handler and the calls. The kind of outcome of call i is i % 8, so that every kind runs about the same
    // number of times and in every order.
    private sealed class Run
    {
        private readonly ConcurrentDictionary<int, int> _attempts = new();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellations = new();
        private readonly ConcurrentQueue<string> _mismatches = new();

        public CountingArrayPool<byte> Pool { get; } = new(surplus: 7);

        public IReadOnlyCollection<string> Mismatches => _mismatches;

        public IReadOnlyCollection<string> UnexpectedExtraAttempts =>
            [.. _attempts.Where(pair => pair.Value > ExpectedAttempts(pair.Key)).Select(pair => $"call {pair.Key} was sent {pair.Value} times")];

        public async Task ExecuteAsync()
        {
            using var handler = new StubHandler(RespondAsync);
            using var client = TestClient.Create(handler, pool: Pool, maxAttempts: 3, random: FixedRandom.Zero);
            await Parallel.ForEachAsync(
                Enumerable.Range(0, Calls),
                new ParallelOptions { MaxDegreeOfParallelism = Parallelism },
                async (index, _) => await CallAsync(client, index));
        }

        private static int ExpectedAttempts(int index) => index % 8 == 1 ? 2 : 1;

        private static string Pk(int index) => "call-" + index.ToString(CultureInfo.InvariantCulture);

        // Lengths that run from a few bytes to far above the initial buffer, for the request and, differently, for the response.
        private static int RequestPadLength(int index) => (index * 37) % 9000;

        private static int ResponsePadLength(int index) => (index * 53) % 7000;

        private static PutItemRequest CreateRequest(int index) => new()
        {
            TableName = "Users",
            Item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = Pk(index),
                ["n"] = AttributeValue.FromNumber(RequestPadLength(index)),
                ["pad"] = new string('x', RequestPadLength(index)),
            },
        };

        private async Task CallAsync(DynamoDbClient client, int index)
        {
            try
            {
                switch (index % 8)
                {
                    case 0 or 1:
                        Verify(index, await client.PutItemAsync(CreateRequest(index)));
                        break;
                    case 2:
                        await ExpectAsync<InvalidOperationException>(index, () => client.PutItemAsync(CreateRequest(index)));
                        break;
                    case 3:
                        await CancelledCallAsync(client, index);
                        break;
                    case 4:
                        await ExpectAsync<ResourceNotFoundException>(index, () => client.PutItemAsync(CreateRequest(index)));
                        break;
                    case 5:
                        var unserializable = new PutItemRequest { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = default } };
                        await ExpectAsync<ArgumentException>(index, () => client.PutItemAsync(unserializable));
                        break;
                    default:
                        await CancellableCallAsync(client, index);
                        break;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Any end that no kind expects: the call is reported, not the test aborted.
                _mismatches.Enqueue($"call {index} (kind {index % 8}) ended with {exception.GetType().Name}: {exception.Message}");
            }
        }

        // The call succeeds with a token that can be cancelled; the token is cancelled when the call is done, which must reach nothing.
        private async Task CancellableCallAsync(DynamoDbClient client, int index)
        {
            using var cancellation = new CancellationTokenSource();
            Verify(index, await client.PutItemAsync(CreateRequest(index), cancellation.Token));
            await cancellation.CancelAsync();
        }

        // The handler cancels the caller's token during the attempt, so the call must end with the caller's cancellation, once.
        private async Task CancelledCallAsync(DynamoDbClient client, int index)
        {
            using var cancellation = new CancellationTokenSource();
            _cancellations[index] = cancellation;
            try
            {
                await client.PutItemAsync(CreateRequest(index), cancellation.Token);
                _mismatches.Enqueue($"call {index} was not cancelled");
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellation.Token)
            {
                // The expected end.
            }
        }

        private async Task ExpectAsync<TException>(int index, Func<Task> call)
            where TException : Exception
        {
            try
            {
                await call();
                _mismatches.Enqueue($"call {index} did not fail");
            }
            catch (TException)
            {
                // The expected end.
            }
        }

        private void Verify(int index, PutItemResponse response)
        {
            var attributes = response.Attributes;
            var ok = attributes is not null
                && attributes["pk"].AsString() == Pk(index)
                && attributes["pad"].AsString().Length == ResponsePadLength(index);
            if (!ok)
            {
                _mismatches.Enqueue($"call {index} got a response that is not its own");
            }
        }

        // What DynamoDB would do, driven by the number in the request: the handler checks that the body arrived whole before it answers.
        private async Task<StubResponse> RespondAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(request.Body);
            var item = document.RootElement.GetProperty("Item");
            var pk = item.GetProperty("pk").GetProperty("S").GetString()!;
            var index = int.Parse(pk.AsSpan("call-".Length), CultureInfo.InvariantCulture);
            var announced = int.Parse(item.GetProperty("n").GetProperty("N").GetString()!, CultureInfo.InvariantCulture);
            if (item.GetProperty("pad").GetProperty("S").GetString()!.Length != announced || announced != RequestPadLength(index))
            {
                return StubResponse.DynamoDbError("ValidationException", "The body arrived broken: " + pk);
            }

            var attempt = _attempts.AddOrUpdate(index, 1, static (_, count) => count + 1);

            // Suspend, so that the calls interleave and the continuations run on other threads.
            await Task.Yield();
            switch (index % 8)
            {
                case 1 when attempt == 1:
                    return index % 16 == 1
                        ? StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down.")
                        : throw new HttpRequestException("The connection dropped.");
                case 2:
                    throw new InvalidOperationException("The handler broke.");
                case 3:
                    await _cancellations[index].CancelAsync();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Not reached.");
                case 4:
                    return StubResponse.DynamoDbError("ResourceNotFoundException", "No such table.");
                default:
                    return StubResponse.Json("{\"Attributes\":{\"pk\":{\"S\":\"" + pk + "\"},\"pad\":{\"S\":\"" + new string('y', ResponsePadLength(index)) + "\"}}}");
            }
        }
    }
}
