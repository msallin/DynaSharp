using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

public sealed class ReturnCommandTests
{
    [Test]
    public async Task ToRequestJson_Attribute_AsksForTheUpdatedAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").Add("count", 1).Return(Attr<int>("count")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"UpdateExpression":"ADD #n0 :v0","ExpressionAttributeNames":{"#n0":"count"},"ExpressionAttributeValues":{":v0":{"N":"1"}},"ReturnValues":"UPDATED_NEW"}""");
    }

    [Test]
    public async Task SendAsync_Number_ReturnsItTyped()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"other":{"S":"x"},"count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk").Update("a").Add("count", 1).Return(Attr<int>("count")).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_GeneratedHandle_ReturnsItTyped()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"orderCount":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table<Customer>("Shop").Update(Customer.Keys.Of("alice")).Add(Customer.Attrs.OrderCount, 1).MaxAttempts(1).Return(Customer.Attrs.OrderCount).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_UntypedAttribute_ReturnsTheValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"tags":{"SS":["a"]}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var tags = await client.Table("Shop", "pk").Update("a").Add("tags", AttributeValue.FromStringSet(["a"])).Return(Attr("tags")).SendAsync();

        // Assert
        await Assert.That(tags.AsStringSet().Single()).IsEqualTo("a");
    }

    [Test]
    public async Task SendAsync_NestedPath_FindsItInTheMaps()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"before":{"S":"x"},"stats":{"M":{"skipped":{"N":"1"},"count":{"N":"7"},"after":{"N":"2"}}},"later":{"S":"y"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk").Update("a").Add("stats.count", 1).Return(Attr<int>("stats.count")).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task SendAsync_AttributeMissing_ThrowsNamingIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"other":{"N":"1"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Remove("count").Return(Attr<int>("count")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("count");
    }

    [Test]
    public async Task SendAsync_NoAttributesAtAll_ThrowsRatherThanReadingZero()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Remove("count").Return(Attr<int>("count")).SendAsync());

        // Assert
        await Assert.That(failure.EntityType).IsEqualTo("System.Int32");
    }

    [Test]
    public async Task SendAsync_ParentIsNoMap_ThrowsAsMissing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"stats":{"S":"flat"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Set("stats", "flat").Return(Attr<int>("stats.count")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("stats.count");
    }

    [Test]
    public async Task SendAsync_NestedAttributeMissing_ThrowsAsMissing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"stats":{"M":{"other":{"N":"1"}}}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Add("stats.other", 1).Return(Attr<int>("stats.count")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("stats.count");
    }

    [Test]
    public async Task SendAsync_ValueOfAnotherType_ThrowsNamingTheAttribute()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"count":{"S":"seven"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Set("count", "seven").Return(Attr<int>("count")).SendAsync());

        // Assert
        await Assert.That((failure.AttributeName, failure.InnerException is System.Text.Json.JsonException)).IsEqualTo(("count", true));
    }

    [Test]
    public async Task SendAsync_NullValue_ThrowsRatherThanReadingZero()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"count":{"NULL":true}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table("Shop", "pk").Update("a").Set("count", AttributeValue.Null).Return(Attr<int>("count")).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("count");
    }

    [Test]
    public async Task SendAsync_ResponseWithCapacityAndUnknownMembers_ReadsTheValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Unknown":1,"ConsumedCapacity":{"TableName":"Shop","CapacityUnits":1.0},"Attributes":{"count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk").Update("a").Add("count", 1).ReturnCapacity().Return(Attr<int>("count")).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task Await_Command_ReturnsTheValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"count":{"N":"7"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var count = await client.Table("Shop", "pk").Update("a").Add("count", 1).Return(Attr<int>("count"));

        // Assert
        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    public async Task Return_ListElement_ThrowsAnArgumentException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            shop.Update("a").Set("tags[1]", "x").Return(Attr("tags[1]"));
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That((failure.Message.Contains("tags[1]", StringComparison.Ordinal), failure.ParamName)).IsEqualTo((true, "attribute"));
    }
}
