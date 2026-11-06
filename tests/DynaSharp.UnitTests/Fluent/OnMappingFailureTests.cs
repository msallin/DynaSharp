using System.Collections.Concurrent;
using System.Text.Json;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using DynaSharp.UnitTests.Telemetry;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// A read that skips the items it cannot map, by hand-written responses: which items it skips, what the callback gets, and what a skipped item does to pages, counts, tokens
/// and the merge of a fan-out. An invoice with the status <c>Refunded</c> is the item a new deployment wrote and the old one cannot read.
/// </summary>
public sealed class OnMappingFailureTests
{
    private const string TenantId = "acme";

    [Test]
    public async Task ItemsAsync_QueryWithoutTheClause_EndsAtTheItemThatDoesNotFit()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2), Invoice("acme", 3)]));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(KeyOf("acme", 2));
    }

    [Test]
    public async Task ItemsAsync_QueryWithAnItemThatDoesNotFit_GivesTheOthersAndReportsItsKey()
    {
        // Arrange (the use case: one row of a new deployment in a partition, which a service lists and sends to a dead-letter store)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2), Invoice("acme", 3)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), failures.Single().Key, failures.Single().AttributeName)).IsEqualTo(("1,3", KeyOf("acme", 2), "Status"));
    }

    [Test]
    public async Task ItemsAsync_FirstItemDoesNotFit_GoesOnFromTheSecond()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1), Invoice("acme", 2)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), failures.Single().Key)).IsEqualTo(("2", KeyOf("acme", 1)));
    }

    [Test]
    public async Task ItemsAsync_SeveralItemsDoNotFit_ReportsEachWithItsOwnKey()
    {
        // Arrange (two in a row, then a good one, then the last of the page: each failure is found from where the one before left off)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2), Poison("acme", 3), Invoice("acme", 4), Poison("acme", 5)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), string.Join(",", failures.Select(static failure => failure.Key!.GetString("sk"))))).IsEqualTo(("1,4", "INV#2026-09#000002,INV#2026-09#000003,INV#2026-09#000005"));
    }

    [Test]
    public async Task ItemsAsync_NoItemFails_NeverCallsTheCallback()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Invoice("acme", 2)]));
        var calls = 0;

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(_ => calls++).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), calls)).IsEqualTo(("1,2", 0));
    }

    [Test]
    public async Task ItemsAsync_SkippedItemsOnTwoPages_ReportsThemInOrder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1), Invoice("acme", 2)], next: true));
        handler.Enqueue(Page([Poison("acme", 3)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), string.Join(",", failures.Select(static failure => failure.Key!.GetString("sk"))))).IsEqualTo(("2", "INV#2026-09#000001,INV#2026-09#000003"));
    }

    [Test]
    public async Task ItemsAsync_TakeAfterASkippedItem_ReadsOnUntilItHasThem()
    {
        // Arrange (Take counts what it gets, so the skipped item takes no place of the two)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2)], next: true));
        handler.Enqueue(Page([Invoice("acme", 3), Invoice("acme", 4)]));

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).ItemsAsync().Take(2).Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), handler.Requests.Count)).IsEqualTo(("1,3", 2));
    }

    [Test]
    public async Task PageAsync_SkippedItem_IsOutOfItemsAndStaysInTheCountOfDynamoDb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2)], next: true));

        // Act
        var page = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).PageAsync();

        // Assert (the token is the LastEvaluatedKey, which lies past the skipped item)
        await Assert.That((page.Items.Count, page.Count, page.ScannedCount, page.Next.ToKey()!.GetString("sk")))
            .IsEqualTo((1, 2, 2, "INV#2026-09#000002"));
    }

    [Test]
    public async Task PagesAsync_SkippedItem_IsOutOfItsPage()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1), Invoice("acme", 2)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var pages = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).PagesAsync().ToListAsync();

        // Assert
        await Assert.That((pages.Single().Items.Single().Number, failures.Count)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task ItemAsync_FirstItemDoesNotFit_GivesTheFirstThatDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1)], next: true));
        handler.Enqueue(Page([Invoice("acme", 2)]));

        // Act
        var invoice = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).ItemAsync();

        // Assert
        await Assert.That(invoice!.Number).IsEqualTo(2);
    }

    [Test]
    public async Task ItemsAsync_CallbackThrows_EndsTheReadWithThatExceptionAndNoRetry()
    {
        // Arrange (an InvalidOperationException from inside the call would read as a response that did not parse)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("acme", 2)]));
        handler.DefaultResponse = Page([Invoice("acme", 1)]);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => throw new InvalidOperationException("Stop here.")).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That((failure!.Message, handler.Requests.Count)).IsEqualTo(("Stop here.", 1));
    }

    [Test]
    public async Task OnMappingFailure_SecondCall_ReplacesTheFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1)]));
        var (first, second) = (0, 0);

        // Act
        await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(_ => first++).OnMappingFailure(_ => second++).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((first, second)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task OnMappingFailure_ReadThatEnded_LeavesNothingToTheNextCommand()
    {
        // Arrange (commands come from a pool, so the next read can get the state of this one)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1)]));
        handler.Enqueue(Page([Poison("acme", 1)]));
        var invoices = client.Table<TenantInvoice>("Billing");
        await invoices.Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).ItemsAsync().ToListAsync();

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await invoices.Query(TenantInvoice.Keys.Pk.Of(TenantId)).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(KeyOf("acme", 1));
    }

    [Test]
    public async Task OnMappingFailure_Null_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");

        // Act
        var failures = new[]
        {
            TestClient.Catch(() => invoices.Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(null!).ToRequestJson()),
            TestClient.Catch(() => invoices.Scan().OnMappingFailure(null!).ToRequestJson()),
            TestClient.Catch(() => invoices.Query([TenantInvoice.Keys.Pk.Of(TenantId)]).OnMappingFailure(null!).ToRequestJson()),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException { ParamName: "onFailure" })).IsTrue();
    }

    [Test]
    public async Task ToRequestJson_WithTheClause_SendsWhatItSendsWithout()
    {
        // Arrange (skipping happens on the client, so the request does not change)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");

        // Act
        var json = invoices.Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(invoices.Query(TenantInvoice.Keys.Pk.Of(TenantId)).ToRequestJson());
    }

    [Test]
    public async Task ItemsAsync_ScanWithAnItemThatDoesNotFit_GivesTheOthers()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Invoice("acme", 1), Poison("globex", 2)]));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Scan().OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), failures.Single().Key)).IsEqualTo(("1", KeyOf("globex", 2)));
    }

    [Test]
    public async Task ItemsAsync_IndexQuery_ReportsTheKeyOfTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1)]));
        var failures = new List<DynamoDbMappingException>();
        var byStatus = client.Table<TenantInvoice>("Billing").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        await byStatus.Query(TenantInvoice.Keys.ByStatusPk.Of("acme", InvoiceState.Open)).OnMappingFailure(failures.Add).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That(failures.Single().Key).IsEqualTo(KeyOf("acme", 1));
    }

    [Test]
    public async Task ItemsAsync_ParallelScan_SkipsInEverySegment()
    {
        // Arrange (segments read on threads of their own, so the callback collects into a concurrent bag)
        using var handler = new StubHandler(static _ => Page([Invoice("acme", 1), Poison("acme", 2)]));
        using var client = TestClient.Create(handler);
        var failures = new ConcurrentBag<DynamoDbMappingException>();

        // Act
        var invoices = await client.Table<TenantInvoice>("Billing").Scan().OnMappingFailure(failures.Add).Parallel(3).ItemsAsync().ToListAsync();

        // Assert
        await Assert.That((invoices.Count, failures.Count)).IsEqualTo((3, 3));
    }

    [Test]
    public async Task ValuesAsync_SelectAfterTheClause_SkipsAnItemWithoutTheAttributeAndHasNoKey()
    {
        // Arrange (a read of one attribute brings no key back)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"DueDate":{"S":"2026-09-01"}},{"Note":{"S":"x"}},{"DueDate":{"S":"2026-09-03"}}],"Count":3,"ScannedCount":3}"""));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(failures.Add).Select(TenantInvoice.Attrs.DueDate).ValuesAsync().Select(static day => day.Day).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), failures.Single().AttributeName, failures.Single().Key)).IsEqualTo(("1,3", "DueDate", (Item?)null));
    }

    [Test]
    public async Task ValueAsync_SelectAfterTheClause_GivesTheFirstValueThatCanBeRead()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"Note":{"S":"x"}},{"DueDate":{"S":"2026-09-02"}}],"Count":2,"ScannedCount":2}"""));

        // Act
        var first = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).Select(TenantInvoice.Attrs.DueDate).ValueAsync();

        // Assert
        await Assert.That(first.Value).IsEqualTo(new DateOnly(2026, 9, 2));
    }

    [Test]
    public async Task ItemsAsync_FanOutWithAnItemThatDoesNotFit_KeepsTheMergeInOrder()
    {
        // Arrange (the skipped item takes its key out of the merge, or the item after it would sort as if it were the skipped one)
        using var handler = new StubHandler(static request => PartitionOf(request) == "TENANT#a"
            ? Page([Invoice("a", 1), Poison("a", 2), Invoice("a", 5)])
            : Page([Invoice("b", 3)]));
        using var client = TestClient.Create(handler);
        var failures = new List<DynamoDbMappingException>();

        // Act
        var numbers = await client.Table<TenantInvoice>("Billing").Query([TenantInvoice.Keys.Pk.Of("a"), TenantInvoice.Keys.Pk.Of("b")]).OnMappingFailure(failures.Add).ItemsAsync().Select(static invoice => invoice.Number).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", numbers), failures.Single().Key)).IsEqualTo(("1,3,5", KeyOf("a", 2)));
    }

    [Test]
    public async Task PageAsync_FanOutWithAnItemWithoutItsSortKey_SkipsItAndWritesATokenThatReadsOn()
    {
        // Arrange (the key of such an item fails halfway, and what it wrote of itself must not reach the token)
        using var handler = new StubHandler(static request => (PartitionOf(request), request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)) switch
        {
            ("TENANT#a", false) => Page([Invoice("a", 1), """{"pk":{"S":"TENANT#a"},"_type":{"S":"INVOICE"}}""", Invoice("a", 5)]),
            ("TENANT#a", true) => Page([Invoice("a", 5)]),
            _ => Page([Invoice("b", 3)]),
        });
        using var client = TestClient.Create(handler);
        var failures = new List<DynamoDbMappingException>();
        var invoices = client.Table<TenantInvoice>("Billing");
        var first = await invoices.Query([TenantInvoice.Keys.Pk.Of("a"), TenantInvoice.Keys.Pk.Of("b")]).OnMappingFailure(failures.Add).PageAsync(2);

        // Act
        var second = await invoices.Query([TenantInvoice.Keys.Pk.Of("a"), TenantInvoice.Keys.Pk.Of("b")]).OnMappingFailure(failures.Add).PageAsync(2, ContinuationToken.Parse(first.Next.ToString()));

        // Assert
        await Assert.That((Numbers(first), first.Count, Numbers(second), failures.Single().AttributeName)).IsEqualTo(("1,3", 2, "5", "sk"));
    }

    [Test]
    public async Task ItemsAsync_MixedQueryOfAnItemOfNoType_SkipsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(MixJson.Page(MixJson.AlphaItem, """{"_type":{"S":"OTHER"},"pk":{"S":"a"},"sk":{"S":"9"}}""", MixJson.BetaItem));
        var failures = new List<DynamoDbMappingException>();

        // Act
        var items = await client.Table("Shop", "pk", "sk").Query("a").Of<Alpha, Beta>().OnMappingFailure(failures.Add).ItemsAsync().Select(MixJson.Show).ToListAsync();

        // Assert
        await Assert.That((string.Join(",", items), failures.Single().Key)).IsEqualTo(("A1,B2", new Item { ["pk"] = "a", ["sk"] = "9" }));
    }

    [Test]
    public async Task ItemsAsync_MixedReadsOfEveryArity_SkipWhatTheyCannotRead()
    {
        // Arrange (an Alpha without its sort key, which is required, in each read)
        const string broken = """{"_type":{"S":"ALPHA"},"pk":{"S":"a"}}""";
        using var handler = new StubHandler(static _ => MixJson.Page(broken, MixJson.GammaItem));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        var failures = new ConcurrentBag<DynamoDbMappingException>();

        // Act
        var counts = new[]
        {
            (await shop.Query("a").Of<Alpha, Beta, Gamma>().OnMappingFailure(failures.Add).ItemsAsync().ToListAsync()).Count,
            (await shop.Query("a").Of<Alpha, Beta, Gamma, Delta>().OnMappingFailure(failures.Add).ItemsAsync().ToListAsync()).Count,
            (await shop.Scan().Of<Alpha, Gamma>().OnMappingFailure(failures.Add).ItemsAsync().ToListAsync()).Count,
            (await shop.Scan().Of<Alpha, Beta, Gamma>().OnMappingFailure(failures.Add).ItemsAsync().ToListAsync()).Count,
            (await shop.Scan().Of<Alpha, Beta, Gamma, Delta>().OnMappingFailure(failures.Add).ItemsAsync().ToListAsync()).Count,
        };

        // Assert
        await Assert.That((string.Join(",", counts), failures.Count, failures.All(static failure => failure.EntityType.EndsWith("Alpha", StringComparison.Ordinal)))).IsEqualTo(("1,1,1,1,1", 5, true));
    }

    [Test]
    public async Task ItemsAsync_SkippedItems_CountOneEachWithOperationAndTable()
    {
        // Arrange
        var table = TestClient.UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.SkippedItemsMetric);
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1), Invoice("acme", 2), Poison("acme", 3)]));

        // Act
        await client.Table<TenantInvoice>(table).Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => { }).ItemsAsync().ToListAsync();

        // Assert (no key and no value, only what the other counters carry)
        await Assert.That((counters.All.Count, counters.All[0].Unit, counters.All[0].TagText)).IsEqualTo((
            2,
            "{item}",
            $"db.collection.name={table},db.operation.name=Query,server.address=dynamodb.eu-central-2.amazonaws.com"));
    }

    [Test]
    public async Task ItemsAsync_CallbackThrows_CountsNothing()
    {
        // Arrange (the item was not skipped: the read ended at it)
        var table = TestClient.UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.SkippedItemsMetric);
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(Page([Poison("acme", 1)]));

        // Act
        await TestClient.CaptureAsync<InvalidOperationException>(async () => await client.Table<TenantInvoice>(table).Query(TenantInvoice.Keys.Pk.Of(TenantId)).OnMappingFailure(static _ => throw new InvalidOperationException()).ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(counters.All.Count).IsEqualTo(0);
    }

    private static string Invoice(string tenant, int number) =>
        $$$"""{"pk":{"S":"TENANT#{{{tenant}}}"},"sk":{"S":"INV#2026-09#{{{number:D6}}}"},"_type":{"S":"INVOICE"},"Status":{"S":"Open"},"DueDate":{"S":"2026-09-30"}}""";

    // The key comes after the attribute that fails, which is what a response is free to do.
    private static string Poison(string tenant, int number) =>
        $$$"""{"Status":{"S":"Refunded"},"_type":{"S":"INVOICE"},"sk":{"S":"INV#2026-09#{{{number:D6}}}"},"pk":{"S":"TENANT#{{{tenant}}}"}}""";

    private static Item KeyOf(string tenant, int number) => new() { ["pk"] = "TENANT#" + tenant, ["sk"] = $"INV#2026-09#{number:D6}" };

    private static StubResponse Page(string[] items, bool next = false) =>
        StubResponse.Json("{\"Items\":[" + string.Join(",", items) + "],\"Count\":" + items.Length + ",\"ScannedCount\":" + items.Length
            + (next ? ",\"LastEvaluatedKey\":" + LastKey(items[^1]) : string.Empty) + "}");

    // The key of the last item as DynamoDB writes LastEvaluatedKey: {"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000002"}}.
    private static string LastKey(string item)
    {
        var root = JsonElement.Parse(item);
        return "{\"pk\":" + root.GetProperty("pk").GetRawText() + ",\"sk\":" + root.GetProperty("sk").GetRawText() + "}";
    }

    private static string PartitionOf(RecordedRequest request) =>
        JsonElement.Parse(request.BodyText).GetProperty("ExpressionAttributeValues").GetProperty(":pk").GetProperty("S").GetString()!;

    private static string Numbers(Page<TenantInvoice> page) => string.Join(",", page.Items.Select(static invoice => invoice.Number));
}
