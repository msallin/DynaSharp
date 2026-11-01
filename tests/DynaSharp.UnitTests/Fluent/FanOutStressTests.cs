using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// One client runs thousands of fan-outs at once, each ending in another way: read to the end, left early, cancelled, rejected, paged by a client, counted, abandoned. A fan-out
// holds several states for as long as it runs, and copies them from a template, so this is where a state that was released early, twice, or to the wrong read shows up: every
// read carries a marker that the partitions, the items and the keys all repeat, so a page or a key that reached the wrong read is a mismatch. Nothing waits: the handler yields
// instead of sleeping, and retries have no delay.
public sealed class FanOutStressTests
{
    private const int Reads = 2000;
    private const int Parallelism = 16;
    private const int Partitions = 3;
    private const int Pages = 3;
    private const int ItemsPerPage = 2;
    private const int ItemsPerPartition = Pages * ItemsPerPage;

    [Test]
    public async Task Reads_ManyConcurrentWithEveryKindOfEnd_EveryReadSeesItsOwnPartitions()
    {
        // Arrange
        using var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.Mismatches).IsEmpty();
    }

    [Test]
    public async Task Reads_ManyConcurrentWithEveryKindOfEnd_EveryKindRan()
    {
        // Arrange
        using var run = new Run();

        // Act
        await run.ExecuteAsync();

        // Assert
        await Assert.That(run.Ended.Count).IsEqualTo(Run.Kinds);
    }

    [Test]
    public async Task Reads_ManyConcurrent_LeaveNoArrayOutstandingAfterDispose()
    {
        // Arrange
        using var run = new Run(abandon: false);

        // Act
        await run.ExecuteAsync();
        run.Client.Dispose();

        // Assert
        await Assert.That(run.Pool.Outstanding).IsEqualTo(0);
    }

    private sealed class Run : IDisposable
    {
        public const int Kinds = 8;

        private readonly ConcurrentDictionary<string, int> _attempts = new();
        private readonly bool _abandon;
        private readonly StubHandler _handler;
        private readonly Table<Item> _shop;

        public Run(bool abandon = true)
        {
            _abandon = abandon;
            _handler = new StubHandler(AnswerAsync);
            Client = CreateClient(_handler, pool: Pool);
            _shop = Client.Table("Shop", "pk", "sk");
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
            await Parallel.ForEachAsync(Enumerable.Range(0, Reads), new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, async (i, _) =>
            {
                var kind = i % Kinds;
                await RunOneAsync(i, kind);
                Ended.AddOrUpdate(kind, 1, static (_, count) => count + 1);
            });

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Marker(int index) => "marker-" + index.ToString("D6", CultureInfo.InvariantCulture);

        private static AttributeValue[] KeysOf(string marker) => [.. Enumerable.Range(0, Partitions).Select(position => (AttributeValue)(marker + "/" + Number(position)))];

        private static string MarkerOf(string partition) => partition[..partition.IndexOf('/', StringComparison.Ordinal)];

        // Sort keys are strides over the partitions, so the merge alternates between them and has to follow all their pages.
        private static string SortKey(int position, int index) => ((index * Partitions) + position).ToString("D4", CultureInfo.InvariantCulture);

        private static StubResponse Page(string partition, int first, bool lookAhead)
        {
            var position = int.Parse(partition[(partition.IndexOf('/', StringComparison.Ordinal) + 1)..], CultureInfo.InvariantCulture);
            var count = Math.Min(ItemsPerPage, ItemsPerPartition - first);
            var items = string.Join(",", Enumerable.Range(first, count).Select(i => "{\"pk\":{\"S\":\"" + partition + "\"},\"sk\":{\"S\":\"" + SortKey(position, i) + "\"}}"));
            var key = first + count < ItemsPerPartition || !lookAhead ? ",\"LastEvaluatedKey\":{\"pk\":{\"S\":\"" + partition + "\"},\"sk\":{\"S\":\"" + SortKey(position, first + count - 1) + "\"}}" : string.Empty;
            return StubResponse.Json("{\"Items\":[" + items + "],\"Count\":" + Number(count) + ",\"ScannedCount\":" + Number(count) + key + "}");
        }

        private async Task RunOneAsync(int index, int kind)
        {
            var marker = Marker(index);
            switch (kind)
            {
                case 0:
                    await ReadAllAsync(_shop.Query(KeysOf(marker)).ItemsAsync(), marker);
                    break;
                case 1:
                    await ReadAllAsync(_shop.Query(KeysOf(marker)).Descending().MaxConcurrency(2).ItemsAsync(), marker);
                    break;
                case 2:
                    await LeftEarlyAsync(marker);
                    break;
                case 3:
                    await CancelledAsync(marker);
                    break;
                case 4:
                    await RejectedAsync(marker);
                    break;
                case 5:
                    await PagedByAClientAsync(marker);
                    break;
                case 6:
                    await CountedAsync(marker);
                    break;
                default:
                    await EnumeratedTwiceOrAbandonedAsync(marker);
                    break;
            }
        }

        private async Task ReadAllAsync(IAsyncEnumerable<Item> items, string marker)
        {
            var count = 0;
            await foreach (var item in items)
            {
                Check(MarkerOf(item.GetString("pk")!), marker);
                count++;
            }

            Check(Number(count), Number(Partitions * ItemsPerPartition));
        }

        private async Task LeftEarlyAsync(string marker)
        {
            await foreach (var item in _shop.Query(KeysOf(marker)).ItemsAsync())
            {
                Check(MarkerOf(item.GetString("pk")!), marker);
                break;
            }
        }

        private async Task CancelledAsync(string marker)
        {
            using var cancellation = new CancellationTokenSource();
            try
            {
                await foreach (var item in _shop.Query(KeysOf(marker)).ItemsAsync(cancellation.Token))
                {
                    Check(MarkerOf(item.GetString("pk")!), marker);
                    await cancellation.CancelAsync();
                }

                Mismatches.Enqueue($"{marker} was not cancelled");
            }
            catch (OperationCanceledException)
            {
                // That is the point.
            }
        }

        private async Task RejectedAsync(string marker)
        {
            try
            {
                await foreach (var item in _shop.Query(KeysOf(marker)).Filter(Attr("reject").Exists()).ItemsAsync())
                {
                    Check(MarkerOf(item.GetString("pk")!), marker);
                }

                Mismatches.Enqueue($"{marker} was not rejected");
            }
            catch (ValidationException exception)
            {
                Check(exception.ExpressionNames!["#n1"], "reject");
            }
        }

        private async Task PagedByAClientAsync(string marker)
        {
            var token = string.Empty;
            var total = 0;
            var pages = 0;
            bool more;
            do
            {
                var page = await _shop.Query(KeysOf(marker)).PageAsync(5, ContinuationToken.Parse(token));
                foreach (var item in page.Items)
                {
                    Check(MarkerOf(item.GetString("pk")!), marker);
                }

                total += page.Count;
                token = page.Next.ToString();
                more = page.HasMore;
                pages++;
            }
            while (more && pages < Partitions * ItemsPerPartition);

            Check(Number(total), Number(Partitions * ItemsPerPartition));
        }

        private async Task CountedAsync(string marker)
        {
            var count = await _shop.Query(KeysOf(marker)).Filter(Attr("count").NotExists()).CountAsync();
            Check(Number(count), Number(Partitions * ItemsPerPartition));
        }

        private async Task EnumeratedTwiceOrAbandonedAsync(string marker)
        {
            var items = _shop.Query(KeysOf(marker)).ItemsAsync();
            if (!_abandon)
            {
                await ReadAllAsync(items, marker);
                try
                {
                    await ReadAllAsync(items, marker);
                    Mismatches.Enqueue($"{marker} was enumerated twice");
                }
                catch (InvalidOperationException)
                {
                    // That is the point.
                }
            }
        }

        private void Check(string? actual, string expected)
        {
            if (actual != expected)
            {
                Mismatches.Enqueue($"expected {expected} but found {actual}");
            }
        }

        // What DynamoDB would do: the page follows from the start key, and the start key must be one that this partition of this read was given.
        private async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(request.BodyText);
            var body = document.RootElement;
            var partition = body.GetProperty("ExpressionAttributeValues").GetProperty(":pk").GetProperty("S").GetString()!;
            var first = 0;
            if (body.TryGetProperty("ExclusiveStartKey", out var startKey))
            {
                Check(startKey.GetProperty("pk").GetProperty("S").GetString(), partition);
                first = (int.Parse(startKey.GetProperty("sk").GetProperty("S").GetString()!, CultureInfo.InvariantCulture) / Partitions) + 1;
            }

            var attempt = _attempts.AddOrUpdate($"{partition}/{first}", 1, static (_, count) => count + 1);

            // Suspend, so that reads interleave and continuations run on other threads.
            await Task.Yield();
            if (request.BodyText.Contains("\"reject\"", StringComparison.Ordinal) && first == 0 && partition.EndsWith("/1", StringComparison.Ordinal))
            {
                return StubResponse.DynamoDbError("ValidationException", "Rejected.");
            }

            if (first == ItemsPerPage && attempt == 1 && partition.EndsWith("/2", StringComparison.Ordinal))
            {
                return Throttled();
            }

            return first < ItemsPerPartition
                ? Page(partition, first, lookAhead: partition.EndsWith("/0", StringComparison.Ordinal))
                : StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":0}""");
        }
    }
}
