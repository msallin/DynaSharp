using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// One client runs thousands of reads at once, each ending in another way: read to the end, retried, filtered, left early, cancelled, rejected, paged by a client,
// counted, in parallel segments, abandoned. A read lasts for several calls on one state, which no write does, so this is where a state released early or
// twice shows up: every read carries a marker that the requests, the items and the keys of its pages all repeat, so a page or a key that reached the wrong read
// is a mismatch. Nothing waits: the handler yields instead of sleeping, and retries have no delay.
public sealed class PagedReadStressTests
{
    private const int Reads = 2000;
    private const int Parallelism = 16;
    private const int Pages = 3;
    private const int ItemsPerPage = 2;

    [Test]
    public async Task Reads_ManyConcurrentWithEveryKindOfEnd_EveryReadSeesItsOwnPages()
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

    private static string Marker(int index) => "marker-" + index.ToString("D6", CultureInfo.InvariantCulture);

    private sealed class Run : IDisposable
    {
        public const int Kinds = 10;

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

        // The partition of a query, or the value of the filter of a scan: the first value of every request is its marker.
        private static string MarkerOf(JsonElement body) => body.GetProperty("ExpressionAttributeValues").GetProperty(":v0").GetProperty("S").GetString()!;

        private static StubResponse Page(string marker, int page, bool empty)
        {
            var items = empty ? string.Empty : string.Join(",", Enumerable.Range(0, ItemsPerPage).Select(i => "{\"pk\":{\"S\":\"" + marker + "\"},\"sk\":{\"S\":\"" + i.ToString(CultureInfo.InvariantCulture) + "\"}}"));
            var key = page < Pages ? ",\"LastEvaluatedKey\":{\"pk\":{\"S\":\"" + marker + "\"},\"sk\":{\"S\":\"" + page.ToString(CultureInfo.InvariantCulture) + "\"}}" : string.Empty;
            var count = (empty ? 0 : ItemsPerPage).ToString(CultureInfo.InvariantCulture);
            return StubResponse.Json("{\"Items\":[" + items + "],\"Count\":" + count + ",\"ScannedCount\":" + count + key + "}");
        }

        private async Task RunOneAsync(int index, int kind)
        {
            var marker = Marker(index);
            switch (kind)
            {
                case 0:
                    await ReadAllAsync(_shop.Query(marker).ItemsAsync(), marker, Pages * ItemsPerPage);
                    break;
                case 1:
                    await ReadAllAsync(_shop.Query(marker).Limit(1).ItemsAsync(), marker, Pages * ItemsPerPage);
                    break;
                case 2:
                    await ReadAllAsync(_shop.Query(marker).Filter(Attr("empty").Exists()).ItemsAsync(), marker, ItemsPerPage);
                    break;
                case 3:
                    await LeftEarlyAsync(marker);
                    break;
                case 4:
                    await CancelledAsync(marker);
                    break;
                case 5:
                    await RejectedAsync(marker);
                    break;
                case 6:
                    await PagedByAClientAsync(marker);
                    break;
                case 7:
                    await CountedAsync(marker);
                    break;
                case 8:
                    await ReadAllAsync(_shop.Scan().Filter(Attr("m").Eq(marker)).Parallel(2).ItemsAsync(), marker, 2 * Pages * ItemsPerPage);
                    break;
                default:
                    await EnumeratedTwiceOrAbandonedAsync(marker);
                    break;
            }
        }

        private async Task ReadAllAsync(IAsyncEnumerable<Item> items, string marker, int expected)
        {
            var count = 0;
            await foreach (var item in items)
            {
                Check(item.GetString("pk"), marker);
                count++;
            }

            Check(count.ToString(CultureInfo.InvariantCulture), expected.ToString(CultureInfo.InvariantCulture));
        }

        private async Task LeftEarlyAsync(string marker)
        {
            await foreach (var item in _shop.Query(marker).ItemsAsync())
            {
                Check(item.GetString("pk"), marker);
                break;
            }
        }

        private async Task CancelledAsync(string marker)
        {
            using var cancellation = new CancellationTokenSource();
            try
            {
                await foreach (var item in _shop.Scan().Filter(Attr("m").Eq(marker)).Parallel(2).ItemsAsync(cancellation.Token))
                {
                    Check(item.GetString("pk"), marker);
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
                await foreach (var item in _shop.Query(marker).Filter(Attr("reject").Exists()).ItemsAsync())
                {
                    Check(item.GetString("pk"), marker);
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
            var pages = 0;
            bool more;
            do
            {
                var page = await _shop.Query(marker).ReturnCapacity().PageAsync(ContinuationToken.Parse(token));
                Check(page.Items[0].GetString("pk"), marker);
                token = page.Next.ToString();
                more = page.HasMore;
                pages++;
            }
            while (more);

            Check(pages.ToString(CultureInfo.InvariantCulture), Pages.ToString(CultureInfo.InvariantCulture));
        }

        private async Task CountedAsync(string marker)
        {
            var count = await _shop.Query(marker).CountAsync();
            Check(count.ToString(CultureInfo.InvariantCulture), (Pages * ItemsPerPage).ToString(CultureInfo.InvariantCulture));
        }

        private async Task EnumeratedTwiceOrAbandonedAsync(string marker)
        {
            var items = _shop.Query(marker).ItemsAsync();
            if (!_abandon)
            {
                await ReadAllAsync(items, marker, Pages * ItemsPerPage);
                try
                {
                    await ReadAllAsync(items, marker, 0);
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

        // What DynamoDB would do: the page follows from the start key, and the start key must be one that this read was given.
        private async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(request.BodyText);
            var body = document.RootElement;
            var marker = MarkerOf(body);
            var page = 1;
            if (body.TryGetProperty("ExclusiveStartKey", out var startKey))
            {
                Check(startKey.GetProperty("pk").GetProperty("S").GetString(), marker);
                page = int.Parse(startKey.GetProperty("sk").GetProperty("S").GetString()!, CultureInfo.InvariantCulture) + 1;
            }

            var segment = body.TryGetProperty("Segment", out var number) ? number.GetInt32() : 0;
            var attempt = _attempts.AddOrUpdate($"{marker}/{segment}/{page}", 1, static (_, count) => count + 1);

            // Suspend, so that reads interleave and continuations run on other threads.
            await Task.Yield();
            if (page == 2 && request.BodyText.Contains("\"reject\"", StringComparison.Ordinal))
            {
                return StubResponse.DynamoDbError("ValidationException", "Rejected.");
            }

            if (page == 2 && attempt == 1 && request.BodyText.Contains("\"Limit\":1,", StringComparison.Ordinal))
            {
                return Throttled();
            }

            return Page(marker, page, empty: page < Pages && request.BodyText.Contains("\"empty\"", StringComparison.Ordinal));
        }
    }
}
