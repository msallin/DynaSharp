using System.Text;
using DynaSharp.Fluent;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// Which item a failed condition was about. The service does not say, so <see cref="ConditionalCheckFailedException.Key"/> comes from the request, and the expected keys here are written
/// by hand from the key grammar of the entities: <c>CUSTOMER#{Id}</c> over <c>PROFILE</c>, and <c>USER#{UserId}</c> over <c>ORDER#{CreatedAt}#{OrderId}</c>.
/// </summary>
public sealed class FailedConditionTests
{
    private const string UserText = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private const string OrderText = "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b";

    // More than the 64 levels JsonDocument allows by default, and what DynamoDB stores: every map of an item is two JSON objects.
    private const int DeepestNesting = 32;

    private static readonly Item AliceKey = new() { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE" };

    private static readonly KeyedOrder Order = new()
    {
        UserId = Guid.Parse(UserText),
        CreatedAt = new DateTimeOffset(2026, 10, 31, 14, 34, 56, 789, TimeSpan.FromHours(2)),
        OrderId = Guid.Parse(OrderText),
        Status = Plan.Pro,
        Total = 12.5m,
    };

    [Test]
    public async Task SendAsync_UpdateOfAGeneratedKey_NamesTheKeyAsStored()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Update(Customer.Keys.Of("alice")).Set(Customer.Attrs.FullName, "Alice").IgnoreVersion().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_UpdateThatReturnsAnAttribute_NamesTheKey()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Update(Customer.Keys.Of("alice")).Add(Customer.Attrs.OrderCount, 1).IfExists().Return(Customer.Attrs.OrderCount).SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_PutOfAnEntity_NamesTheKeyAttributesOfItsItem()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Put(new Customer("alice", "Alice", 3, "alice@example.com")).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_PutOfAGeneratedEntity_NamesTheKeyAsStored()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var orders = client.Table<KeyedOrder>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await orders.Put(Order).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(new Item { ["pk"] = "USER#" + UserText, ["sk"] = "ORDER#2026-10-31T12:34:56.789Z#" + OrderText });
    }

    [Test]
    public async Task SendAsync_KeyOfATemplate_ParsesBackToTheInputs()
    {
        // Arrange (what the documentation of the property promises)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var orders = client.Table<KeyedOrder>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await orders.Put(Order).IfNotExists().SendAsync());

        // Assert
        var sortKey = KeyedOrder.Keys.Sk.Parse(failure.Key!.GetString("sk")!);
        await Assert.That((KeyedOrder.Keys.Pk.Parse(failure.Key.GetString("pk")!).UserId, sortKey.CreatedAt, sortKey.OrderId)).IsEqualTo((Order.UserId, Order.CreatedAt, Order.OrderId));
    }

    [Test]
    public async Task SendAsync_DeleteOfAGeneratedKey_NamesTheKeyAsStored()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Delete(Customer.Keys.Of("alice")).IfExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_DeleteOnATableWithoutASortKey_NamesThePartitionKey()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.Table("Shop", "id").Delete("a").IfExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(new Item { ["id"] = "a" });
    }

    [Test]
    public async Task SendAsync_PutOfAnItem_NamesOnlyItsKeyAttributes()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await shop.Put(new Item { ["name"] = "Alice", ["sk"] = "PROFILE", ["pk"] = "CUSTOMER#alice" }).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_PutBuiltWithWith_NamesTheKeyOfTheAttributes()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await shop.Put().With("pk", "CUSTOMER#alice").With("name", "Alice").IfNotExists().With("sk", "PROFILE").SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_KeysOfOtherTypes_KeepTheirTypes()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var ticks = client.Table("Ticks", "device", "at");
        var key = new Item { ["device"] = 42, ["at"] = new byte[] { 1, 2, 3 } };

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await ticks.Put(key).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(key);
    }

    [Test]
    public async Task SendAsync_ItemAsDeepAsDynamoDbStores_StillNamesTheKeyAndNotAParseFailure()
    {
        // Arrange (the exception of the service must survive this, or a deep item would lose it to a JsonException)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        AttributeValue nested = "bottom";
        for (var depth = 0; depth < DeepestNesting; depth++)
        {
            nested = AttributeValue.FromMap(new Item { ["down"] = nested });
        }

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await shop.Put(new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE", ["deep"] = nested }).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Key).IsEqualTo(AliceKey);
    }

    [Test]
    public async Task SendAsync_ReturnOldOnFailure_HoldsTheItemAndTheKey()
    {
        // Arrange
        var existing = new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE", ["name"] = "Alice" };
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed(existing));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await shop.Put(new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE", ["name"] = "Bob" }).IfNotExists().ReturnOldOnFailure().SendAsync());

        // Assert
        await Assert.That((failure.Item, failure.Key)).IsEqualTo((existing, AliceKey));
    }

    [Test]
    public async Task SendAsync_FailedCondition_KeepsTheKeyOutOfTheText()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await customers.Delete(Customer.Keys.Of("alice")).IfExists().SendAsync());

        // Assert
        await Assert.That((failure.Key is null, failure.ToString().Contains("alice", StringComparison.Ordinal), failure.Message)).IsEqualTo((false, false, "The conditional request failed"));
    }

    [Test]
    public async Task SendAsync_ConditionFailedInATransaction_HasNoKey()
    {
        // Arrange (a transaction names several items, and TransactionCanceledException.ItemFor says which)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(async () => await client.TransactWrite().Put(shop, new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE" }).SendAsync());

        // Assert
        await Assert.That(failure.Key).IsNull();
    }

    [Test]
    public async Task SendAsync_RejectedRequest_StillExplainsItsExpressions()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.DynamoDbError("ValidationException", "No."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Delete("a").If(Attr("n").Exists()).SendAsync());

        // Assert
        await Assert.That(failure.Expressions!["ConditionExpression"]).IsEqualTo("attribute_exists(#n0)");
    }

    [Test]
    public async Task Describe_BodyWithAKey_TakesItWhateverTheKeyNamesSay()
    {
        // Arrange
        const string Body = """{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"N":"7"}},"ConditionExpression":"attribute_exists(#n0)"}""";

        // Act
        var key = KeyOf(Body, "other");

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "a", ["sk"] = 7 });
    }

    [Test]
    public async Task Describe_ItemWithAnAttributeCalledKey_ReadsTheKeyAttributes()
    {
        // Arrange (only a member of the request is the key of an update or a delete)
        const string Body = """{"TableName":"Shop","Item":{"Key":{"S":"not it"},"pk":{"S":"a"}}}""";

        // Act
        var key = KeyOf(Body, "pk");

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "a" });
    }

    [Test]
    public async Task Describe_EscapedText_ReadsItUnescaped()
    {
        // Arrange
        const string Body = """{"TableName":"Shop","Item":{"pk":{"S":"a\"bé"}}}""";

        // Act
        var key = KeyOf(Body, "pk");

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "a\"bé" });
    }

    [Test]
    public async Task Describe_ItemWithoutItsSortKey_NamesWhatIsThere()
    {
        // Arrange (DynamoDB rejects such a put before any condition, but a stub does not)
        const string Body = """{"TableName":"Shop","Item":{"pk":{"S":"a"},"name":{"S":"b"}}}""";

        // Act
        var key = KeyOf(Body, "pk", "sk");

        // Assert
        await Assert.That(key).IsEqualTo(new Item { ["pk"] = "a" });
    }

    [Test]
    public async Task Describe_ItemWithoutAnyKeyAttribute_LeavesTheKeyNull()
    {
        // Arrange
        const string Body = """{"TableName":"Shop","Item":{"name":{"S":"b"}}}""";

        // Act
        var key = KeyOf(Body, "pk", "sk");

        // Assert
        await Assert.That(key).IsNull();
    }

    [Test]
    public async Task Describe_BodyWithoutKeyOrItem_LeavesTheKeyNull()
    {
        // Arrange
        const string Body = """{"TableName":"Shop"}""";

        // Act
        var key = KeyOf(Body, "pk");

        // Assert
        await Assert.That(key).IsNull();
    }

    private static Item? KeyOf(string body, params string[] keyNames)
    {
        var failure = new ConditionalCheckFailedException("The conditional request failed");
        FailedCondition.Describe(failure, Encoding.UTF8.GetBytes(body), keyNames);
        return failure.Key;
    }
}
