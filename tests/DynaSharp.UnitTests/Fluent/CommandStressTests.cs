using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// One client runs thousands of commands at once, each ending in another way: sent, retried, sent twice, built on a stale handle, abandoned,
// cancelled, rejected. Every command carries a marker, and every request and result is checked against it, so a state that reached two
// commands, or a buffer that was reused too early, shows up as a body of the wrong command. Nothing waits: the handler yields instead of
// sleeping, and retries have no delay.
public sealed class CommandStressTests
{
    private const int Commands = 1800;
    private const int Parallelism = 16;

    // The retry quota of a client lasts for a hundred retries.
    private const int RetriedCommands = 40;

    [Test]
    public async Task SendAsync_ManyConcurrentCommandsWithEveryKindOfEnd_EveryCommandKeepsItsOwnBytesAndEveryKindRan()
    {
        // Arrange
        using var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That((string.Join("\n", run.Mismatches), run.Ended.Count)).IsEqualTo((string.Empty, Run.Kinds));
    }

    [Test]
    public async Task SendAsync_ManyConcurrentCommands_LeavesNoArrayOutstandingAfterDispose()
    {
        // Arrange
        using var run = new Run(abandon: false);

        // Act
        await run.ExecuteAsync();
        run.Client.Dispose();

        // Assert
        await Assert.That(run.Pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_RetriedCommand_SendsTheSameBytesEveryTime()
    {
        // Arrange
        var bodies = new ConcurrentDictionary<string, ConcurrentBag<string>>();
        using var handler = new StubHandler(async (request, _) =>
        {
            var body = request.BodyText;
            var seen = bodies.GetOrAdd(MarkerOf(body), static _ => []);
            seen.Add(body);
            await Task.Yield();
            return seen.Count < 3 ? Throttled() : StubResponse.Json("{}");
        });
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await Parallel.ForEachAsync(Enumerable.Range(0, RetriedCommands), new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, async (i, _) =>
            await shop.Update(Marker(i)).Set("n", i).If(Attr("m").Eq(Marker(i))).SendAsync(CancellationToken.None));

        // Assert
        await Assert.That(bodies.Values.Count(seen => seen.Count != 3 || seen.Distinct().Count() != 1)).IsEqualTo(0);
    }

    private static string Marker(int index) => "marker-" + index.ToString("D6", CultureInfo.InvariantCulture);

    // Every body holds its marker as the partition key.
    private static string MarkerOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var attributes = root.TryGetProperty("Key", out var key) ? key : root.GetProperty("Item");
        return attributes.GetProperty("pk").GetProperty("S").GetString()!;
    }

    private sealed class Run : IDisposable
    {
        public const int Kinds = 9;

        private readonly ConcurrentDictionary<string, int> _attempts = new();
        private readonly bool _abandon;
        private readonly StubHandler _handler;
        private readonly Table<Item> _shop;

        public Run(bool abandon = true)
        {
            _abandon = abandon;
            _handler = new StubHandler(AnswerAsync);
            Client = CreateClient(_handler, pool: Pool);
            _shop = Client.Table("Shop", "pk");
        }

        public CountingArrayPool<byte> Pool { get; } = new(surplus: 7);

        public DynamoDbClient Client { get; }

        public ConcurrentQueue<string> Mismatches { get; } = new();

        public ConcurrentDictionary<int, int> Ended { get; } = new();

        public void Dispose()
        {
            Client.Dispose();
            _handler.Dispose();
        }

        public async Task ExecuteAsync() =>
            await Parallel.ForEachAsync(Enumerable.Range(0, Commands), new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, async (i, _) =>
            {
                var kind = i % Kinds;
                await RunOneAsync(i, kind);
                Ended.AddOrUpdate(kind, 1, static (_, count) => count + 1);
            });

        private static string ValueOf(string marker) => "value-of-" + marker;

        private async Task RunOneAsync(int index, int kind)
        {
            var marker = Marker(index);
            switch (kind)
            {
                case 0:
                    await SentAsync(marker);
                    break;
                case 1:
                    await RetriedAsync(marker);
                    break;
                case 2:
                    await RejectedAsync(marker);
                    break;
                case 3:
                    await SentTwiceAsync(marker);
                    break;
                case 4:
                    await ExtendedWhileInFlightAsync(marker);
                    break;
                case 5:
                    Abandoned(marker);
                    break;
                case 6:
                    await CancelledAsync(marker);
                    break;
                case 7:
                    await GetAsync(marker);
                    break;
                default:
                    Rendered(marker);
                    break;
            }
        }

        private async Task SentAsync(string marker)
        {
            var result = await _shop.Update(marker).Set("value", ValueOf(marker)).ReturnNew().SendAsync();
            Check(result.Item!.GetString("value"), ValueOf(marker));
        }

        // The handler fails the first attempt of a command that sets "retry", so its state stays in use across a retry while others come and go.
        private async Task RetriedAsync(string marker)
        {
            var value = await _shop.Update(marker).Set("retry", 1).Set("value", ValueOf(marker)).Return(Attr("value")).SendAsync();
            Check(value.AsString(), ValueOf(marker));
        }

        private async Task RejectedAsync(string marker)
        {
            try
            {
                await _shop.Put(new Item { ["pk"] = marker, ["reject"] = true }).If(Attr("value").Eq(ValueOf(marker))).SendAsync();
                Mismatches.Enqueue($"{marker} was not rejected");
            }
            catch (ValidationException exception)
            {
                Check(exception.ExpressionNames!["#n0"], "value");
            }
        }

        private async Task SentTwiceAsync(string marker)
        {
            var (first, second) = SendTwice(marker);
            await first;
            if (!second)
            {
                Mismatches.Enqueue($"{marker} was sent twice");
            }
        }

        private (Task First, bool SecondThrew) SendTwice(string marker)
        {
            var command = _shop.Delete(marker).ReturnOld();
            var first = command.SendAsync().AsTask();
            try
            {
                var again = command;
                _ = again.SendAsync().AsTask();
                return (first, false);
            }
            catch (InvalidOperationException)
            {
                return (first, true);
            }
        }

        private async Task ExtendedWhileInFlightAsync(string marker)
        {
            var (first, threw) = ExtendWhileInFlight(marker);
            var result = await first;
            Check(result.Item!.GetString("value"), ValueOf(marker));
            if (!threw)
            {
                Mismatches.Enqueue($"{marker} was extended in flight");
            }
        }

        private (Task<WriteResult<Item>> First, bool Threw) ExtendWhileInFlight(string marker)
        {
            var command = _shop.Update(marker).Set("value", ValueOf(marker)).ReturnNew();
            var first = command.SendAsync().AsTask();
            try
            {
                var again = command;
                _ = again.Set("late", "write");
                return (first, false);
            }
            catch (InvalidOperationException)
            {
                return (first, true);
            }
        }

        private void Abandoned(string marker)
        {
            if (_abandon)
            {
                _ = _shop.Update(marker).Set("value", ValueOf(marker));
            }
        }

        private async Task CancelledAsync(string marker)
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            try
            {
                await _shop.Update(marker).Set("value", ValueOf(marker)).SendAsync(cancellation.Token);
                Mismatches.Enqueue($"{marker} was not cancelled");
            }
            catch (OperationCanceledException)
            {
                // That is the point.
            }
        }

        private async Task GetAsync(string marker)
        {
            var result = await _shop.Get(marker).Project("value").SendAsync();
            Check(result.Item!.GetString("value"), ValueOf(marker));
        }

        private void Rendered(string marker)
        {
            var json = _shop.Update(marker).Set("value", ValueOf(marker)).ToRequestJson();
            Check(MarkerOf(json), marker);
        }

        private void Check(string? actual, string expected)
        {
            if (actual != expected)
            {
                Mismatches.Enqueue($"expected {expected} but found {actual}");
            }
        }

        // What DynamoDB would do: the body arrives whole and names its own marker, or the response it gets is not the one the command expects.
        private async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            var body = request.BodyText;
            var marker = MarkerOf(body);
            var attempt = _attempts.AddOrUpdate(marker, 1, static (_, count) => count + 1);

            // Suspend, so that commands interleave and continuations run on other threads.
            await Task.Yield();
            if (body.Contains("\"reject\"", StringComparison.Ordinal))
            {
                return StubResponse.DynamoDbError("ValidationException", "Rejected.");
            }

            if (body.Contains("\"retry\"", StringComparison.Ordinal) && attempt == 1)
            {
                return Throttled();
            }

            var attributes = "{\"value\":{\"S\":\"" + ValueOf(marker) + "\"}}";
            return StubResponse.Json((request.Operation == "GetItem" ? "{\"Item\":" : "{\"Attributes\":") + attributes + "}");
        }
    }
}
