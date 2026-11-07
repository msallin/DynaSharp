using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// Which item a read could not map, by hand-written responses: <see cref="DynamoDbMappingException.Key"/> holds the key of the failing item, wherever it stands in the JSON, and the
/// message stays without values.
/// </summary>
public sealed class MappingFailureKeyTests
{
    private const string Good = """{"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000001"},"_type":{"S":"INVOICE"},"Status":{"S":"Open"},"DueDate":{"S":"2026-09-30"}}""";

    // The key comes after the attribute that fails, which is what a response is free to do.
    private const string Poison = """{"Status":{"S":"Refunded"},"_type":{"S":"INVOICE"},"DueDate":{"S":"2026-09-30"},"sk":{"S":"INV#2026-09#000002"},"pk":{"S":"TENANT#acme"}}""";

    private static readonly Item PoisonKey = new() { ["pk"] = "TENANT#acme", ["sk"] = "INV#2026-09#000002" };

    private static string Page => $$"""{"Items":[{{Good}},{{Poison}}],"Count":2,"ScannedCount":2}""";

    [Test]
    public async Task SendAsync_GetOfAnItemThatDoesNotFit_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json($$"""{"Item":{{Poison}}}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Get(TenantInvoice.Keys.Of("acme", "2026-09", 2)).SendAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task ItemAsync_GetOnRawKeys_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json($$"""{"Item":{{Poison}}}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Get(TenantInvoice.Keys.Of("acme", "2026-09", 2)).ItemAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task PageAsync_QueryWithAnItemThatDoesNotFit_NamesItsKeyAndNotTheOneBefore()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of("acme")).PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task ItemsAsync_ScanWithAnItemThatDoesNotFit_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Scan().ItemsAsync().ToListAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task PageAsync_QueryOfAnIndex_NamesTheKeyOfTheTable()
    {
        // Arrange (an index can hold several items under one key, so its key would not find the row)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));
        var byStatus = client.Table<TenantInvoice>("Billing").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await byStatus.Query(TenantInvoice.Keys.ByStatusPk.Of("acme", InvoiceState.Open)).PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task PageAsync_FanOutWithAnItemThatDoesNotFit_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Query([TenantInvoice.Keys.Pk.Of("acme")]).PageAsync(10));

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task SendAsync_BatchGetWithAnItemThatDoesNotFit_NamesItsKey()
    {
        // Arrange (the second round starts over in its own array, and a failure after a request went through comes with what was read)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Billing":[""" + Good + """]},"UnprocessedKeys":{"Billing":{"Keys":[{"pk":{"S":"TENANT#acme"},"sk":{"S":"INV#2026-09#000002"}}]}}}"""));
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Billing":[""" + Poison + "]}}"));

        // Act
        var failure = await Assert.ThrowsAsync<BatchGetIncompleteException<TenantInvoice>>(async () => await client.Table<TenantInvoice>("Billing").BatchGet().Get(TenantInvoice.Keys.Of("acme", "2026-09", 1)).Get(TenantInvoice.Keys.Of("acme", "2026-09", 2)).SendAsync());

        // Assert
        await Assert.That((failure!.Items.Count, ((DynamoDbMappingException)failure.InnerException!).Key)).IsEqualTo((1, PoisonKey));
    }

    [Test]
    public async Task PageAsync_MixedQueryWithAnItemThatDoesNotFit_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table("Billing", "pk", "sk").Query("TENANT#acme").Of<TenantInvoice, RoomSlot>().PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(PoisonKey);
    }

    [Test]
    public async Task PageAsync_MixedScanOfAnItemOfNoType_NamesItsKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"_type":{"S":"OTHER"},"pk":{"S":"a"},"sk":{"S":"b"}}],"Count":1,"ScannedCount":1}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table("Billing", "pk", "sk").Scan().Of<TenantInvoice, RoomSlot>().PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(new Item { ["pk"] = "a", ["sk"] = "b" });
    }

    [Test]
    public async Task PageAsync_TableWithoutASortKey_NamesThePartitionKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"Kind":{"N":"1"},"Id":{"N":"7"},"_type":{"S":"COUNTER"}}],"Count":1,"ScannedCount":1}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<NumberedCounter>("Billing").Scan().PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsEqualTo(new Item { ["Id"] = 7 });
    }

    [Test]
    public async Task PageAsync_ItemWithoutAKey_LeavesTheKeyEmpty()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json("""{"Items":[{"Status":{"S":"Refunded"},"_type":{"S":"INVOICE"}}],"Count":1,"ScannedCount":1}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Scan().PageAsync());

        // Assert
        await Assert.That(failure!.Key).IsNull();
    }

    [Test]
    public async Task Message_ItemThatDoesNotFit_HoldsNoValue()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json(Page));

        // Act
        var failure = await Assert.ThrowsAsync<DynamoDbMappingException>(async () => await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of("acme")).PageAsync());

        // Assert
        await Assert.That(failure!.Message).DoesNotContain("acme").And.DoesNotContain("Refunded").And.DoesNotContain("INV#");
    }

    [Test]
    public async Task PageAsync_ItemsThatFit_AreNotTouched()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        handler.Enqueue(StubResponse.Json($$"""{"Items":[{{Good}}],"Count":1,"ScannedCount":1}"""));

        // Act
        var page = await client.Table<TenantInvoice>("Billing").Query(TenantInvoice.Keys.Pk.Of("acme")).PageAsync();

        // Assert
        await Assert.That(page.Items.Single().Number).IsEqualTo(1);
    }
}
