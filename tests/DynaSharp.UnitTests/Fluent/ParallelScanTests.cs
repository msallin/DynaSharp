using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;
using static DynaSharp.UnitTests.Fluent.PageJson;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

// Every handler here tells segments apart by the Segment member of the request, so that the responses do not depend on how the tasks interleave. Nothing waits for
// time: what must happen at once is arranged with gates that the handler opens.
public sealed class ParallelScanTests
{
    [Test]
    public async Task ItemsAsync_EverySegment_IsRead()
    {
        // Arrange
        using var handler = new StubHandler(request => Last(SegmentOf(request).ToString(CultureInfo.InvariantCulture)));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Parallel(4).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("sk")).Order(StringComparer.Ordinal))).IsEqualTo("0,1,2,3");
    }

    [Test]
    public async Task ItemsAsync_OneSegment_ReadsItAll()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(Last("2"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Parallel(1).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.SortKeys(), handler.Requests[0].BodyText)).IsEqualTo(("1,2", """{"TableName":"Shop","Segment":0,"TotalSegments":1}"""));
    }

    [Test]
    public async Task ItemsAsync_PagesOfASegment_AreFollowedWithTheKeyOfThatSegment()
    {
        // Arrange
        var bodies = new ConcurrentBag<string>();
        using var handler = new StubHandler(request =>
        {
            var segment = SegmentOf(request);
            var startKey = StartKeyOf(request);
            bodies.Add($"{segment}:{startKey}");
            return startKey is null ? More($"s{segment}") : Last($"e{segment}");
        });
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Parallel(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, string.Join(",", bodies.Order(StringComparer.Ordinal)))).IsEqualTo((6, "0:,0:s0,1:,1:s1,2:,2:s2"));
    }

    [Test]
    public async Task ItemsAsync_FilteredEmptyPages_AreFollowed()
    {
        // Arrange
        using var handler = new StubHandler(request => StartKeyOf(request) is null ? FilteredEmpty($"s{SegmentOf(request)}") : Last($"e{SegmentOf(request)}"));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Parallel(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(string.Join(",", items.Select(static item => item.GetString("sk")).Order(StringComparer.Ordinal))).IsEqualTo("e0,e1");
    }

    [Test]
    public async Task ItemsAsync_OptionsOfTheScan_ReachEverySegment()
    {
        // Arrange
        var bodies = new ConcurrentBag<string>();
        using var handler = new StubHandler(request =>
        {
            bodies.Add(request.BodyText);
            return Last();
        });
        using var client = TestClient.Create(handler);

        // Act
#pragma warning disable DYNA208 // No item comes back, and what goes out is what this test is about.
        await client.Table<Customer>("Shop").Scan().Filter(Attr("x").Eq(1)).Project(Attr("y")).Limit(5).Consistent().ReturnCapacity().MaxAttempts(1).Parallel(2).ItemsAsync().ToListAsync();
#pragma warning restore DYNA208

        // Assert
        await Assert.That(string.Join("\n", bodies.Order(StringComparer.Ordinal))).IsEqualTo(
            """
            {"TableName":"Shop","ConsistentRead":true,"Limit":5,"Segment":0,"TotalSegments":2,"FilterExpression":"(#n0 = :v0) AND (#n1 = :v1)","ProjectionExpression":"#n2, #n0, #n3, #n4","ExpressionAttributeNames":{"#n0":"_type","#n1":"x","#n2":"y","#n3":"pk","#n4":"sk"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"},":v1":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}
            {"TableName":"Shop","ConsistentRead":true,"Limit":5,"Segment":1,"TotalSegments":2,"FilterExpression":"(#n0 = :v0) AND (#n1 = :v1)","ProjectionExpression":"#n2, #n0, #n3, #n4","ExpressionAttributeNames":{"#n0":"_type","#n1":"x","#n2":"y","#n3":"pk","#n4":"sk"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"},":v1":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}
            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task ItemsAsync_Index_ReadsTheIndex()
    {
        // Arrange
        var bodies = new ConcurrentBag<string>();
        using var handler = new StubHandler(request =>
        {
            bodies.Add(request.BodyText);
            return Last();
        });
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").Index("ByStatus", "status").Scan().Parallel(1).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(bodies.Single()).IsEqualTo("""{"TableName":"Shop","IndexName":"ByStatus","Segment":0,"TotalSegments":1}""");
    }

    [Test]
    public async Task ItemsAsync_Entity_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json("""{"Items":[{"id":{"S":"s"}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var items = await client.Table<Session>("Sessions").Scan().Parallel(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ItemsAsync_Segments_RunAtOnceUpToTheirNumber()
    {
        // Arrange: no answer comes until every segment has its request out, which only works if they run at once
        const int Segments = 8;
        var counter = new AttemptCounter(Segments);
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var most = 0;
        using var handler = new StubHandler(async (_, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref most, now);
            counter.Reached();
            await open.Task;
            Interlocked.Decrement(ref inFlight);
            return Last();
        });
        using var client = TestClient.Create(handler);

        // Act
        var reading = client.Table("Shop", "pk", "sk").Scan().Parallel(Segments).ItemsAsync().ToListAsync();
        await counter.WhenReachedAsync(Segments);
        open.SetResult();
        await reading;

        // Assert
        await Assert.That(most).IsEqualTo(Segments);
    }

    [Test]
    public async Task ItemsAsync_SlowConsumer_HoldsBackTheNextPages()
    {
        // Arrange: a segment with endless pages. It must stop at the page the consumer has not taken.
        using var handler = new StubHandler(_ => More("1"));
        using var client = TestClient.Create(handler);
        var enumerator = client.Table("Shop", "pk", "sk").Scan().Parallel(1).ItemsAsync().GetAsyncEnumerator();

        // Act
        await enumerator.MoveNextAsync();
        await SettleAsync(handler);
        var sent = handler.Requests.Count;
        await enumerator.DisposeAsync();

        // Assert (the page in hand, the one in the handover, and the one that waits for room)
        await Assert.That(sent).IsLessThanOrEqualTo(3);
    }

    [Test]
    public async Task ItemsAsync_Done_ReleasesEveryState()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => Last("1"));
        using var client = TestClient.Create(handler, pool: pool);
        var read = Start(client.Table("Shop", "pk", "sk"), 3);

        // Act
        await read.Read.ToListAsync();
        var released = read.ReleasedOnce;
        client.Dispose();

        // Assert
        await Assert.That((released, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ItemsAsync_EnumeratedTwice_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => Last());
        using var client = TestClient.Create(handler);
        var items = client.Table("Shop", "pk", "sk").Scan().Parallel(2).ItemsAsync();
        await items.ToListAsync();

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => items.ToListAsync());

        // Assert
        await Assert.That((failure.Message.StartsWith("This Scan on Shop was enumerated already.", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task ItemsAsync_DisposedBeforeTheFirstMove_ReleasesAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 4);
        var enumerator = read.Read.GetAsyncEnumerator();

        // Act
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That((read.ReleasedOnce, handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ItemsAsync_BreakOutEarly_StopsEverySegmentAndWaitsForThem()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var stopped = 0;
        using var handler = new StubHandler(async (request, token) =>
        {
            if (SegmentOf(request) == 0)
            {
                return More("1");
            }

            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: pool);
        var read = Start(client.Table("Shop", "pk", "sk"), 4);

        // Act
        await foreach (var unused in read.Read)
        {
            break;
        }

        var seenWhenDisposed = Volatile.Read(ref stopped);
        var released = read.ReleasedOnce;
        client.Dispose();

        // Assert (all waiting requests were cancelled by the time the enumerator was gone, so nothing is left running)
        await Assert.That((seenWhenDisposed, released, pool.Outstanding)).IsEqualTo((3, true, 0));
    }

    [Test]
    public async Task ItemsAsync_SegmentThatCannotStart_StopsWhatRunsAndThrows()
    {
        // Arrange (the first segment is inside its request when the second fails to start)
        using var firstIsRunning = new ManualResetEventSlim();
        var stopped = 0;
        using var handler = new StubHandler(async (_, token) =>
        {
            firstIsRunning.Set();
            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: new SecondSegmentFailsPool(firstIsRunning));
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() => shop.Scan().Filter($"attribute_exists(a)").Parallel(3).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That((failure.Message, Volatile.Read(ref stopped))).IsEqualTo(("No buffer for this segment.", 1));
    }

    [Test]
    public async Task ItemsAsync_Cancelled_StopsEverySegmentAndThrowsTheTokenOfTheCaller()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var counter = new AttemptCounter(4);
        var stopped = 0;
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(async (_, token) =>
        {
            counter.Reached();
            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: pool);
        var read = Start(client.Table("Shop", "pk", "sk"), 4, cancellation.Token);

        // Act
        var reading = read.Read.ToListAsync();
        await counter.WhenReachedAsync(4);
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => reading);
        var released = read.ReleasedOnce;
        client.Dispose();

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, stopped, released, pool.Outstanding)).IsEqualTo((true, 4, true, 0));
    }

    [Test]
    public async Task ItemsAsync_CancelledBeforeItStarted_ThrowsAndSendsNothing()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 3, cancellation.Token);

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((true, 0, true));
    }

    [Test]
    public async Task ItemsAsync_CancelledBetweenItems_StopsAtTheNextPage()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler(_ => More("1"));
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 1, cancellation.Token);
        var seen = 0;

        // Act
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in read.Read)
            {
                seen++;
                await cancellation.CancelAsync();
            }
        });

        // Assert
        await Assert.That((seen, failure.CancellationToken == cancellation.Token, read.ReleasedOnce)).IsEqualTo((1, true, true));
    }

    [Test]
    public async Task ItemsAsync_WithCancellation_StopsEverySegment()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 2);

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in read.Read.WithCancellation(cancellation.Token))
            {
                // Nothing arrives.
            }
        });

        // Assert
        await Assert.That((handler.Requests.Count, read.ReleasedOnce)).IsEqualTo((0, true));
    }

    [Test]
    public async Task ItemsAsync_TwoTokensThatStayQuiet_ReadsToTheEnd()
    {
        // Arrange
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var handler = new StubHandler(_ => Last("1"));
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 2, first.Token);

        // Act
        var items = await read.Read.WithCancellation(second.Token).ToListAsync();

        // Assert
        await Assert.That((items.Count, read.ReleasedOnce)).IsEqualTo((2, true));
    }

    [Test]
    public async Task ItemsAsync_OneSegmentFails_SurfacesThatFailureOnceAndStopsTheOthers()
    {
        // Arrange
        var waiting = new AttemptCounter(3);
        var stopped = 0;
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(async (request, token) =>
        {
            if (SegmentOf(request) == 3)
            {
                await waiting.WhenReachedAsync(3);
                return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone.");
            }

            waiting.Reached();
            await WaitUntilCancelledAsync(() => Interlocked.Increment(ref stopped), token);
            return Last();
        });
        using var client = TestClient.Create(handler, pool: pool);
        var read = Start(client.Table("Shop", "pk", "sk"), 4);

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(() => read.Read.ToListAsync());
        var released = read.ReleasedOnce;
        client.Dispose();

        // Assert
        await Assert.That((failure.Message.StartsWith("Gone.", StringComparison.Ordinal), stopped, released, pool.Outstanding)).IsEqualTo((true, 3, true, 0));
    }

    [Test]
    public async Task ItemsAsync_SeveralSegmentsFail_SurfacesOneFailureAsItIs()
    {
        // Arrange
        var counter = new AttemptCounter(4);
        using var handler = new StubHandler(async (request, _) =>
        {
            counter.Reached();
            await counter.WhenReachedAsync(4);
            return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone " + SegmentOf(request).ToString(CultureInfo.InvariantCulture));
        });
        using var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 4);

        // Act (the exception of the type it is, never an aggregate)
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That((failure.Message.StartsWith("Gone ", StringComparison.Ordinal), read.ReleasedOnce)).IsEqualTo((true, true));
    }

    [Test]
    public async Task ItemsAsync_ItemThatCannotBeRead_SurfacesTheMappingException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"CUSTOMER#a"},"fullName":{"S":"Alice"},"_type":{"S":"ORDER"}}]}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(() => customers.Scan().Parallel(2).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ItemsAsync_RetryInASegment_RepeatsItsPage()
    {
        // Arrange
        var attempts = new ConcurrentDictionary<int, int>();
        using var handler = new StubHandler(request =>
            attempts.AddOrUpdate(SegmentOf(request), 1, static (_, count) => count + 1) == 1 ? Throttled() : Last("1"));
        using var client = CreateClient(handler);

        // Act
        var items = await client.Table("Shop", "pk", "sk").Scan().Parallel(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((items.Count, handler.Requests.Count)).IsEqualTo((3, 6));
    }

    [Test]
    public async Task ItemsAsync_ClientDisposed_SurfacesItOnceAndLeavesNoState()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var read = Start(client.Table("Shop", "pk", "sk"), 3);
        client.Dispose();

        // Act
        await TestClient.CaptureAsync<ObjectDisposedException>(() => read.Read.ToListAsync());

        // Assert
        await Assert.That(read.ReleasedOnce).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_InvalidOption_SurfacesFromEveryWay()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => shop.Scan().ReturnCapacity((ReturnConsumedCapacity)99).Parallel(2).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task ItemsAsync_ManyNames_CopiesThemForEverySegment()
    {
        // Arrange: more than the arrays of slices start with, which a tag of the span reads back
        var bodies = new ConcurrentBag<string>();
        using var handler = new StubHandler(request =>
        {
            bodies.Add(request.BodyText);
            return Last();
        });
        using var client = TestClient.Create(handler);
        var paths = Enumerable.Range(0, 40).Select(static i => "a" + i.ToString(CultureInfo.InvariantCulture)).ToArray();

        // Act
        await client.Table("Shop", "pk", "sk").Scan().Project(paths).Filter(Attr("v").Eq(1)).Parallel(2).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(bodies.Select(static body => JsonElement.Parse(body).GetProperty("ExpressionAttributeNames").EnumerateObject().Count()).Distinct().Single()).IsEqualTo(41);
    }

    [Test]
    public async Task DebuggerText_Command_NamesOperationAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = client.Table("Shop", "pk").Scan().Parallel(2).DebuggerText;

        // Assert
        await Assert.That(text).Contains("Scan on Shop: {\"TableName\":\"Shop\"");
    }

    [Test]
    public async Task ItemsAsync_SegmentFailsWhileTheCallerHasAPageToRead_StopsTheOthersAtOnce()
    {
        // Arrange (the first segment handed a page over and waits for its next one when the second fails)
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, token) =>
        {
            if (SegmentOf(request) == 1)
            {
                return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone.");
            }

            if (StartKeyOf(request) is null)
            {
                return More("1");
            }

            await WaitUntilCancelledAsync(stopped.SetResult, token);
            return Last();
        });
        using var client = TestClient.Create(handler);
        var enumerator = client.Table("Shop", "pk", "sk").Scan().Parallel(2).ItemsAsync().GetAsyncEnumerator();

        // Act
        await enumerator.MoveNextAsync();
        var stoppedFirst = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(static wait => wait.IsCompletedSuccessfully, TaskScheduler.Default);
        await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();

        // Assert
        await Assert.That(stoppedFirst).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_PageThatComesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the reader waits for the handover, and the segment for its response)
        var context = new CountingSynchronizationContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, _) =>
        {
            if (SegmentOf(request) == 0)
            {
                await release.Task.ConfigureAwait(false);
            }

            return Last("1");
        });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var reading = context.Start(() => ReadAllAsync(shop.Scan().Parallel(2).ItemsAsync()));

        // Act
        release.SetResult();
        await reading;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_HandoverThatIsFull_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the second page waits for room, which the reader makes)
        var context = new CountingSynchronizationContext();
        using var handler = new StubHandler();
        handler.Enqueue(More("1"));
        handler.Enqueue(More("2"));
        handler.Enqueue(Last("3"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await context.Start(() => ReadAllAsync(shop.Scan().Parallel(1).ItemsAsync()));

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_BreakOutEarly_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (stopping the segment that still waits takes a moment)
        var context = new CountingSynchronizationContext();
        using var handler = new StubHandler(async (request, token) =>
        {
            if (SegmentOf(request) == 1)
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }

            return Last("1");
        });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await context.Start(() => ReadFirstAsync(shop.Scan().Parallel(2).ItemsAsync()));

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_CancelledWhileASegmentFinishesItsRequest_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the second segment is in a request that does not end at the cancellation, and the read waits for it)
        var context = new CountingSynchronizationContext();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler(async (request, _) =>
        {
            if (SegmentOf(request) == 1)
            {
                await release.Task.ConfigureAwait(false);
            }

            return Last("1");
        });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var reading = context.Start(() => ReadFirstAsync(shop.Scan().Parallel(2).ItemsAsync(cancellation.Token), cancellation));

        // Act
        release.SetResult();
        await reading;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ItemsAsync_SegmentFailsWhileAnotherWaits_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the failure stops the first segment, which takes a moment)
        var context = new CountingSynchronizationContext();
        using var handler = new StubHandler(async (request, token) =>
        {
            if (SegmentOf(request) == 1)
            {
                return StubResponse.DynamoDbError("ResourceNotFoundException", "Gone.");
            }

            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return Last();
        });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await TestClient.CaptureAsync<ResourceNotFoundException>(() => context.Start(() => ReadAllAsync(shop.Scan().Parallel(2).ItemsAsync())));

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    private static async Task ReadAllAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var _ in items.ConfigureAwait(false))
        {
        }
    }

    private static async Task ReadFirstAsync<T>(IAsyncEnumerable<T> items, CancellationTokenSource? cancellation = null)
    {
        await foreach (var _ in items.ConfigureAwait(false))
        {
            // In place, so that the read stops on the context of the caller.
#pragma warning disable CA1849
            cancellation?.Cancel();
#pragma warning restore CA1849
            break;
        }
    }

    private static int SegmentOf(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        return document.RootElement.GetProperty("Segment").GetInt32();
    }

    private static string? StartKeyOf(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        return document.RootElement.TryGetProperty("ExclusiveStartKey", out var key) ? key.GetProperty("sk").GetProperty("S").GetString() : null;
    }

    // Gives the segment every chance to run ahead of the consumer: as long as the requests keep coming it has not hit the bound yet.
    private static async Task SettleAsync(StubHandler handler)
    {
        var seen = -1;
        for (var quiet = 0; quiet < 200;)
        {
            await Task.Yield();
            var now = handler.Requests.Count;
            quiet = now == seen ? quiet + 1 : 0;
            seen = now;
        }
    }

    private static Tracked<IAsyncEnumerable<Item>> Start(Table<Item> shop, int segments, CancellationToken cancellationToken = default)
    {
        var command = shop.Scan();
        var handle = command.Handle;
        return new(command.Parallel(segments).ItemsAsync(cancellationToken), handle.State!, handle.Version + 3);
    }

    // Fails the copy of the expressions of the second segment, once the first is running. That copy is one rent, because the filter of the test has no names and no values.
    private sealed class SecondSegmentFailsPool(ManualResetEventSlim firstIsRunning) : ArrayPool<byte>
    {
        private int _copies;

        public override byte[] Rent(int minimumLength)
        {
            if (Environment.StackTrace.Contains(nameof(ExpressionWriter.CopyFrom), StringComparison.Ordinal) && Interlocked.Increment(ref _copies) == 2)
            {
                firstIsRunning.Wait(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("No buffer for this segment.");
            }

            return Shared.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false) => Shared.Return(array, clearArray);
    }
}
