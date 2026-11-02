using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

public sealed class GetCommandTests
{
    [Test]
    public async Task ToRequestJson_KeyOnly_WritesTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Get("a", "b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}}}""");
    }

    [Test]
    public async Task ToRequestJson_NumberKeys_WritesThemAsNumbers()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Get(1, 2.5m).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"N":"1"},"sk":{"N":"2.5"}}}""");
    }

    [Test]
    public async Task ToRequestJson_BinaryKey_WritesBase64()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Get(new byte[] { 1, 2 }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"B":"AQI="}}}""");
    }

    [Test]
    public async Task ToRequestJson_KeyThatNeedsEscaping_EscapesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Get("a\"b", "é").ToRequestJson();

        // Assert
        await Assert.That(JsonComparerResult(json, """{"TableName":"Shop","Key":{"pk":{"S":"a\"b"},"sk":{"S":"é"}}}""")).IsEmpty();
    }

    [Test]
    public async Task ToRequestJson_ConsistentAndProjection_WritesThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Get("a", "b").Consistent().Project("fullName", "address.city").Project("lines[0]").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}},"ConsistentRead":true,"ProjectionExpression":"#n0, #n1.#n2, #n3[0]","ExpressionAttributeNames":{"#n0":"fullName","#n1":"address","#n2":"city","#n3":"lines"}}""");
    }

    [Test]
    public async Task ToRequestJson_TypedProjection_AddsTheDiscriminatorAndTheKeys()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Get("a", "b").Project(Customer.Attrs.FullName).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}},"ProjectionExpression":"#n0, #n1, #n2, #n3","ExpressionAttributeNames":{"#n0":"fullName","#n1":"_type","#n2":"pk","#n3":"sk"}}""");
    }

    [Test]
    public async Task ToRequestJson_TypedWithoutProjection_AddsNoProjection()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Get("a", "b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}}}""");
    }

    [Test]
    public async Task ItemAsync_TypedProjection_FindsAnItemOfAnotherType()
    {
        // Arrange (the discriminator comes back because the projection asked for it, which is how a read tells the item it found from what it wanted)
        using var handler = new StubHandler(request => request.BodyText.Contains("_type", StringComparison.Ordinal)
            ? StubResponse.Json("""{"Item":{"_type":{"S":"ORDER"},"fullName":{"S":"a"},"pk":{"S":"CUSTOMER#a"}}}""")
            : StubResponse.Json("""{"Item":{"fullName":{"S":"a"},"pk":{"S":"CUSTOMER#a"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Customer>("Shop").Get("a", "b").Project(Customer.Attrs.FullName).ItemAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ToRequestJson_GeneratedKey_WritesItsAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Entity_TakesTheKeyFromIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Get(new Customer("alice", "Alice")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Capacity_WritesTheOption()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Get("a").ReturnCapacity().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task SendAsync_ItemExists_ReadsItAsAnItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"a"},"n":{"N":"1"}},"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":0.5}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Get("a").ReturnCapacity().SendAsync();

        // Assert
        await Assert.That((result.Found, result.Item!.GetNumber<int>("n"), result.ConsumedCapacity!.CapacityUnits)).IsEqualTo((true, 1, (double?)0.5));
    }

    [Test]
    public async Task SendAsync_NoItem_IsNotFound()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Get("a").SendAsync();

        // Assert
        await Assert.That((result.Found, result.Item)).IsEqualTo((false, (Item?)null));
    }

    [Test]
    public async Task SendAsync_TypedTable_ReadsTheEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"CUSTOMER#alice"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"unknown":{"S":"x"}},"Unknown":1}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).SendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(new Customer("alice", "Alice"));
    }

    [Test]
    public async Task SendAsync_ItemOfAnotherType_ThrowsTheMappingException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"CUSTOMER#alice"},"_type":{"S":"ORDER"},"fullName":{"S":"x"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task SendAsync_StructEntity_FoundTellsAMissingItemFromADefaultOne()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Session>("Sessions").Get("s").SendAsync();

        // Assert
        await Assert.That(result.Found).IsFalse();
    }

    [Test]
    public async Task SendAsync_MaxAttempts_Applies()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "Slow down."));
        using var client = DynaSharp.UnitTests.Http.RetryTestSupport.CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.Table("Shop", "pk").Get("a").MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").Get("a");

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("GetItem");
    }

    [Test]
    public async Task ItemAsync_ItemExists_ReturnsTheEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"CUSTOMER#alice"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var customer = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync();

        // Assert
        await Assert.That(customer).IsEqualTo(new Customer("alice", "Alice"));
    }

    [Test]
    public async Task ItemAsync_NoItem_ReturnsNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var customer = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync();

        // Assert
        await Assert.That(customer).IsNull();
    }

    [Test]
    public async Task ItemAsync_StructEntityWithNoItem_ReturnsItsDefaultValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var session = await client.Table<Session>("Sessions").Get("s").ItemAsync();

        // Assert
        await Assert.That(session).IsEqualTo(default(Session));
    }

    [Test]
    public async Task ItemAsync_Chain_SendsWhatSendAsyncSends()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        await shop.Get("a", "b").Consistent().Project("n").ReturnCapacity().SendAsync();

        // Act
        await shop.Get("a", "b").Consistent().Project("n").ReturnCapacity().ItemAsync();

        // Assert
        await Assert.That((handler.Requests[1].BodyText, handler.Requests[1].Operation)).IsEqualTo((handler.Requests[0].BodyText, "GetItem"));
    }

    [Test]
    public async Task ItemAsync_ItemOfAnotherType_ThrowsTheMappingException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"CUSTOMER#alice"},"_type":{"S":"ORDER"},"fullName":{"S":"x"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("_type");
    }

    [Test]
    public async Task ItemAsync_StaleCommand_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            var command = shop.Get("a");
            _ = command.Consistent();
            var again = command;
            return again.ItemAsync().AsTask();
        });

        // Assert
        await Assert.That((failure.Message.Contains("already sent, extended or rendered", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ItemAsync_CancelledToken_ThrowsAndGivesTheStateBack()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(async () => await shop.Get("a").ItemAsync(cancelled.Token));
        var json = shop.Get("b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"b"}}}""");
    }

    [Test]
    public async Task Project_InvalidPath_ThrowsAnArgumentException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var path = "a..b";

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get("a").Project(path);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("a..b");
    }

    private static string JsonComparerResult(string actual, string expected) => DynaSharp.UnitTests.Operations.JsonComparer.Differences(expected, actual);
}
