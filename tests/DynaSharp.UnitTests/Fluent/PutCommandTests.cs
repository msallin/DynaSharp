using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

public sealed partial class PutCommandTests
{
    [Test]
    public async Task ToRequestJson_Item_WritesItsAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Put(new Item { ["pk"] = "a", ["sk"] = "b", ["n"] = 1 }).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"},"sk":{"S":"b"},"n":{"N":"1"}}}""");
    }

    [Test]
    public async Task ToRequestJson_ItemChangedAfterTheCall_KeepsWhatItWas()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var item = new Item { ["pk"] = "a" };
        var command = client.Table("Shop", "pk").Put(item);

        // Act
        item["pk"] = "changed";
        var json = command.ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Entity_WritesItThroughItsMapping()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Put(new Customer("alice", "Alice")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"},"orderCount":{"N":"0"}}}""");
    }

    [Test]
    public async Task ToRequestJson_AttributeByAttribute_WritesAnItem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Put().With("pk", "a").With("sk", "b").With("n", 1).With("price", 1.5m).With(Attr<long>("big"), 2).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"},"sk":{"S":"b"},"n":{"N":"1"},"price":{"N":"1.5"},"big":{"N":"2"}}}""");
    }

    [Test]
    public async Task ToRequestJson_WithAfterAnItem_AddsToTheItem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).With("extra", true).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"},"extra":{"BOOL":true}}}""");
    }

    [Test]
    public async Task ToRequestJson_ConditionBetweenAttributes_WritesAWholeItem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put().With("pk", "a").IfNotExists().With("n", 1).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"},"n":{"N":"1"}},"ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk"}}""");
    }

    [Test]
    [Arguments(true, "attribute_exists(#n0)")]
    [Arguments(false, "attribute_not_exists(#n0)")]
    public async Task ToRequestJson_ExistenceOfTheItem_ChecksThePartitionKey(bool exists, string expected)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = exists ? shop.Put(new Item { ["pk"] = "a" }).IfExists().ToRequestJson() : shop.Put(new Item { ["pk"] = "a" }).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).Contains($"\"ConditionExpression\":\"{expected}\"");
    }

    [Test]
    public async Task ToRequestJson_KeyNameWithADot_IsOneName()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "user.id").Put(new Item { ["user.id"] = "a" }).IfNotExists().ToRequestJson();

        // Assert
        await Assert.That(json).EndsWith(""","ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"user.id"}}""");
    }

    [Test]
    public async Task ToRequestJson_InterpolatedCondition_WritesTheCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).If($"contains({Attr("tags")}, {"x"})").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"contains(#n0, :v0)\"");
    }

    [Test]
    public async Task ToRequestJson_StructCondition_WritesTheCondition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).If(Attr("status").Ne("x")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"#n0 <> :v0\"");
    }

    [Test]
    public async Task ToRequestJson_Options_WritesEachMember()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).ReturnOld().ReturnOldOnFailure().ReturnCapacity().ReturnMetrics().MaxAttempts(2).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}},"ReturnValues":"ALL_OLD","ReturnValuesOnConditionCheckFailure":"ALL_OLD","ReturnConsumedCapacity":"TOTAL","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task ToRequestJson_HelperOverTheInterfaces_WorksOnAPut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).OnlyIfOwned("me").Once().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"#n0 = :v0\"");
    }

    [Test]
    public async Task ToRequestJson_LargeItem_WritesAllOfIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var text = new string('x', 300_000);

        // Act
        var json = client.Table("Shop", "pk").Put(new Item { ["pk"] = "a", ["large"] = text }).ToRequestJson();

        // Assert
        await Assert.That(json.Length).IsEqualTo(300_000 + """{"TableName":"Shop","Item":{"pk":{"S":"a"},"large":{"S":""}}}""".Length);
    }

    [Test]
    public async Task Put_NullItem_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            _ = shop.Put((Item)null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("item");
    }

    [Test]
    public async Task SendAsync_ReturnOld_ReadsTheReplacedEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"CUSTOMER#alice"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Old"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table<Customer>("Shop").Put(new Customer("alice", "New")).ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.Item).IsEqualTo(new Customer("alice", "Old"));
    }

    [Test]
    public async Task SendAsync_NothingAskedFor_HasNoAttributes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" }).SendAsync();

        // Assert
        await Assert.That((result.HasItem, result.Item, result.ConsumedCapacity, result.ItemCollectionMetrics)).IsEqualTo((false, (Item?)null, (ConsumedCapacity?)null, (ItemCollectionMetrics?)null));
    }

    [Test]
    public async Task SendAsync_ReturnedItemOfAnotherShape_ThrowsTheMappingExceptionWithTheWriteDone()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"fullName":{"S":"x"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Customer>("Shop").Put(new Customer("alice", "New")).ReturnOld().SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("pk");
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").Put(new Item { ["pk"] = "a" });

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("PutItem");
    }

    [Test]
    public async Task MaxAttempts_Zero_ThrowsNamingTheParameter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.Put(new Item { ["pk"] = "a" }).MaxAttempts(0));

        // Assert
        await Assert.That((failure as ArgumentOutOfRangeException)?.ParamName).IsEqualTo("attempts");
    }
}
