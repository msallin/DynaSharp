using DynaSharp.Expressions;
using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

public sealed partial class DeleteCommandTests
{
    private const string Start = """{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"b"}}""";

    [Test]
    public async Task ToRequestJson_ComposedConditionFirst_DoesNotDoubleItsParentheses()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").If(Attr("n").Eq(0).And(Attr("m").Eq(1))).If(Attr("o").Eq(2)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ConditionExpression":"(#n0 = :v0 AND #n1 = :v1) AND (#n2 = :v2)"""");
    }

    [Test]
    public async Task ToRequestJson_ComposedConditionSecond_DoesNotDoubleItsParentheses()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").If(Attr("o").Eq(2)).If(Attr("n").Eq(0).Or(Attr("m").Eq(1))).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ConditionExpression":"(#n0 = :v0) AND (#n1 = :v1 OR #n2 = :v2)"""");
    }

    [Test]
    public async Task ToRequestJson_TwoGroupsSideBySide_KeepsThemTogether()
    {
        // Arrange (starts and ends with a parenthesis, and is not one group)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").If($"({Attr("n")} = {0}) OR ({Attr("m")} = {1})").If(Attr("o").Exists()).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""((#n0 = :v0) OR (#n1 = :v1)) AND (attribute_exists(#n2))""");
    }

    [Test]
    public async Task ToRequestJson_TextThatOpensAndDoesNotClose_IsWrappedAndLeftToDynamoDb()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk", "sk").Delete("a", "b");

        // Act: the analyzer refuses this text in an interpolated string, so the handler is driven by hand, as the compiler would.
        var condition = new ExpressionHandler(0, 1, command);
        condition.AppendLiteral("(");
        condition.AppendFormatted(Attr("n"));
        var json = command.If(condition).If(Attr("o").Exists()).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""((#n0) AND (attribute_exists(#n1))""");
    }

    [Test]
    public async Task ToRequestJson_Key_WritesTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + "}");
    }

    [Test]
    public async Task ToRequestJson_GeneratedKey_WritesItsAttributes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Customer>("Shop").Delete(Customer.Keys.Of("alice")).ToRequestJson();

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
        var json = client.Table<Customer>("Shop").Delete(new Customer("alice", "Alice", 3)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#alice"},"sk":{"S":"PROFILE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Conditions_WritesThem()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").If(Attr("n").Eq(0)).If($"{Attr("m")} > {1}").IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","ConditionExpression":"(#n0 = :v0) AND (#n1 > :v1) AND (attribute_exists(#n2))","ExpressionAttributeNames":{"#n0":"n","#n1":"m","#n2":"pk"},"ExpressionAttributeValues":{":v0":{"N":"0"},":v1":{"N":"1"}}}""");
    }

    [Test]
    public async Task ToRequestJson_Options_WritesEachMember()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").ReturnOld().ReturnOldOnFailure().ReturnCapacity().ReturnMetrics().MaxAttempts(2).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","ReturnValues":"ALL_OLD","ReturnValuesOnConditionCheckFailure":"ALL_OLD","ReturnConsumedCapacity":"TOTAL","ReturnItemCollectionMetrics":"SIZE"}""");
    }

    [Test]
    public async Task ToRequestJson_HelperOverTheInterfaces_WorksOnADelete()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Delete("a", "b").OnlyIfOwned("me").Once().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ConditionExpression\":\"#n0 = :v0\"");
    }

    [Test]
    public async Task SendAsync_ReturnOld_ReadsTheDeletedItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"a"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk", "sk").Delete("a", "b").ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.Item!.GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task Await_Command_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk", "sk").Delete("a", "b");

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("DeleteItem");
    }

    [Test]
    public async Task MaxAttempts_Zero_ThrowsNamingTheParameter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = shop.Delete("a").MaxAttempts(0));

        // Assert
        await Assert.That((failure as ArgumentOutOfRangeException)?.ParamName).IsEqualTo("attempts");
    }
}
