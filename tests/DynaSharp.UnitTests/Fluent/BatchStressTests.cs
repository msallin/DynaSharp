using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// One client runs batches and transactions at once, beside single commands, each ending in another way: done, with unprocessed rounds, given up,
// retried, cancelled, abandoned, used twice. Every action of a command carries its marker, and every request and result is checked against it, so a state
// that reached two commands, an offset that points into the actions of another, or a response read after its buffer was reused shows up as a marker that does
// not belong. Nothing waits: the handler yields instead of sleeping, and the jitter of the retries is zero.
public sealed class BatchStressTests
{
    private const int Commands = 1400;
    private const int Parallelism = 16;

    [Test]
    public async Task SendAsync_ManyConcurrentBatchesWithEveryKindOfEnd_EveryCommandKeepsItsOwnBytes()
    {
        // Arrange
        using var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.Mismatches).IsEmpty();
    }

    [Test]
    public async Task SendAsync_ManyConcurrentBatchesWithEveryKindOfEnd_EveryKindRan()
    {
        // Arrange
        using var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.Ended.Count).IsEqualTo(Run.Kinds);
    }

    [Test]
    public async Task SendAsync_ManyConcurrentBatches_LeavesNoArrayOutstandingAfterDispose()
    {
        // Arrange
        using var run = new Run(abandon: false);

        // Act
        await run.ExecuteAsync();
        run.Client.Dispose();

        // Assert
        await Assert.That(run.Pool.Outstanding).IsEqualTo(0);
    }

    private static string Marker(int index) => "marker-" + index.ToString("D6", CultureInfo.InvariantCulture) + "-";

    private sealed class Run : IDisposable
    {
        public const int Kinds = 9;

        // "marker-000000-"
        private const int MarkerLength = 14;

        private readonly ConcurrentDictionary<string, int> _attempts = new();
        private readonly bool _abandon;
        private readonly StubHandler _handler;
        private readonly Table<Item> _shop;

        public Run(bool abandon = true)
        {
            _abandon = abandon;
            _handler = new StubHandler(AnswerAsync);
            Client = CreateClient(_handler, pool: Pool, maxAttempts: 2);
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

        private static string Key(JsonElement write)
        {
            var attributes = write.TryGetProperty("PutRequest", out var put) ? put.GetProperty("Item") : write.GetProperty("DeleteRequest").GetProperty("Key");
            return attributes.GetProperty("pk").GetProperty("S").GetString()!;
        }

        // {"Put":{"Item":{...}}}, or an action with a key.
        private static string ActionKey(JsonElement action)
        {
            var body = action.EnumerateObject().Single().Value;
            return (body.TryGetProperty("Item", out var item) ? item : body.GetProperty("Key")).GetProperty("pk").GetProperty("S").GetString()!;
        }

        private static StubResponse AnswerSnapshot(string body)
        {
            var gets = JsonElement.Parse(body).GetProperty("TransactItems").EnumerateArray().Select(static action => action.GetProperty("Get").GetProperty("Key").GetRawText()).ToList();
            return StubResponse.Json("{\"Responses\":[" + string.Join(',', gets.Select(static key => "{\"Item\":" + key + "}")) + "]}");
        }

        private async Task RunOneAsync(int index, int kind)
        {
            var marker = Marker(index);
            switch (kind)
            {
                case 0:
                    await WrittenAsync(marker);
                    break;
                case 1:
                    await WrittenWithRoundsAsync(marker);
                    break;
                case 2:
                    await GivenUpAsync(marker);
                    break;
                case 3:
                    await ReadAsync(marker);
                    break;
                case 4:
                    await TransactionAsync(marker);
                    break;
                case 5:
                    await SnapshotAsync(marker);
                    break;
                case 6:
                    await CancelledAsync(marker);
                    break;
                case 7:
                    Abandoned(marker);
                    break;
                default:
                    await SentTwiceAsync(marker);
                    break;
            }
        }

        private BatchWriteCommand<Item> Writes(string marker, int count)
        {
            var command = _shop.BatchWrite();
            for (var i = 0; i < count; i++)
            {
                command = i % 3 == 0 ? command.Delete(marker + i) : command.Put(new Item { ["pk"] = marker + i });
            }

            return command;
        }

        private async Task WrittenAsync(string marker)
        {
            var result = await Writes(marker, 30).SendAsync();
            Check(result.Written.ToString(CultureInfo.InvariantCulture), "30");
        }

        // The body says "rounds" through its marker, so the handler leaves one write in each of the first rounds.
        private async Task WrittenWithRoundsAsync(string marker)
        {
            var result = await Writes(marker + "rounds-", 30).SendAsync();
            Check(result.Written.ToString(CultureInfo.InvariantCulture), "30");
        }

        private async Task GivenUpAsync(string marker)
        {
            try
            {
                await Writes(marker + "stuck-", 3).MaxStalledRounds(2).SendAsync();
                Mismatches.Enqueue($"{marker} did not give up");
            }
            catch (BatchIncompleteException exception)
            {
                foreach (var write in exception.UnprocessedItems["Shop"])
                {
                    var key = write.PutRequest?.Item ?? write.DeleteRequest!.Key;
                    CheckMarker(key["pk"].AsString(), marker);
                }
            }
        }

        private async Task ReadAsync(string marker)
        {
            var keys = Enumerable.Range(0, 120).Select(i => new TextKey(marker + i));
            var result = await _shop.BatchGet().Keys(keys).Consistent().Project("pk").SendAsync();
            Check(result.Items.Count.ToString(CultureInfo.InvariantCulture), "120");
            foreach (var item in result.Items)
            {
                CheckMarker(item.GetString("pk"), marker);
            }
        }

        private async Task TransactionAsync(string marker)
        {
            await Client.TransactWrite()
                .Put(_shop, new Item { ["pk"] = marker + "a" }).IfNotExists()
                .Update(_shop, marker + "b").Add("n", 1).If(Attr("n").Lt(10))
                .Delete(_shop, marker + "c")
                .SendAsync();
        }

        private async Task SnapshotAsync(string marker)
        {
            var (snapshot, slot) = await GetAsync(marker);

            // Other commands reuse every buffer of this one by the time the result is read.
            await Task.Yield();
            CheckMarker(snapshot.Read(slot).Item!.GetString("pk"), marker);
        }

        private async Task<(TransactGetResult Snapshot, TransactSlot<Item> Slot)> GetAsync(string marker)
        {
            var call = Build(marker, out var slot);
            return (await call, slot);
        }

        private ValueTask<TransactGetResult> Build(string marker, out TransactSlot<Item> slot) =>
            Client.TransactGet().Get(_shop, marker + "a", default, out _).Get(_shop, marker + "b", default, out slot).SendAsync();

        private async Task CancelledAsync(string marker)
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            try
            {
                await Writes(marker, 3).SendAsync(cancellation.Token);
                Mismatches.Enqueue($"{marker} was not cancelled");
            }
            catch (OperationCanceledException)
            {
                // That is the point.
            }
        }

        private void Abandoned(string marker)
        {
            if (_abandon)
            {
                _ = Writes(marker, 3);
                _ = Client.TransactWrite().Delete(_shop, marker + "a");
            }
        }

        private async Task SentTwiceAsync(string marker)
        {
            var (first, threw) = SendTwice(marker);
            await first;
            if (!threw)
            {
                Mismatches.Enqueue($"{marker} was sent twice");
            }
        }

        private (Task First, bool SecondThrew) SendTwice(string marker)
        {
            var command = Writes(marker, 3);
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

        private void Check(string? actual, string expected)
        {
            if (actual != expected)
            {
                Mismatches.Enqueue($"expected {expected} but found {actual}");
            }
        }

        private void CheckMarker(string? actual, string marker)
        {
            if (actual is null || !actual.StartsWith(marker, StringComparison.Ordinal))
            {
                Mismatches.Enqueue($"expected {marker} but found {actual}");
            }
        }

        // What DynamoDB would do: the body arrives whole, every action in it holds one marker, and the answer is made of what it holds.
        private async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            var body = request.BodyText;
            await Task.Yield();
            return request.Operation switch
            {
                "BatchWriteItem" => AnswerWrite(request),
                "BatchGetItem" => BatchStubs.AnswerKeys(request),
                "TransactGetItems" => AnswerSnapshot(body),
                _ => AnswerTransaction(body),
            };
        }

        private StubResponse AnswerWrite(RecordedRequest request)
        {
            var writes = JsonElement.Parse(BatchStubs.RequestItems(request)).GetProperty("Shop").EnumerateArray().ToList();
            var keys = writes.Select(Key).ToList();
            CheckOneMarker(keys);
            if (keys[0].Contains("stuck", StringComparison.Ordinal))
            {
                return BatchStubs.LeaveAllWrites(request);
            }

            // Each command takes three rounds per request: one write is left twice, and then the request is done.
            var marker = keys[0][..MarkerLength];
            if (keys[0].Contains("rounds", StringComparison.Ordinal) && _attempts.AddOrUpdate(marker, 1, static (_, count) => count + 1) % 3 != 0)
            {
                return StubResponse.Json("{\"UnprocessedItems\":{\"Shop\":[" + writes[^1].GetRawText() + "]}}");
            }

            return StubResponse.Json("{}");
        }

        private StubResponse AnswerTransaction(string body)
        {
            var actions = JsonElement.Parse(body).GetProperty("TransactItems").EnumerateArray().ToList();
            var token = JsonElement.Parse(body).GetProperty("ClientRequestToken").GetString()!;

            // The first attempt fails, and the second must carry the same token, or this count stays at one for each and it never ends.
            if (_attempts.AddOrUpdate(token, 1, static (_, count) => count + 1) == 1)
            {
                return Throttled();
            }

            CheckOneMarker([.. actions.Select(ActionKey)]);
            return StubResponse.Json("{}");
        }

        private void CheckOneMarker(List<string> keys)
        {
            var marker = keys[0][..MarkerLength];
            foreach (var key in keys)
            {
                CheckMarker(key, marker);
            }
        }
    }

    private sealed class TextKey(string partitionKey) : DynaSharp.Mapping.IKey<Item>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(partitionKey);
        }
    }
}
