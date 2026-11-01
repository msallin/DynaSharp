using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

public sealed class RejectedRequestTests
{
    private const string Rejected = "Invalid ConditionExpression: Attribute name is a reserved keyword";

    [Test]
    public async Task SendAsync_ValidationFailure_CarriesTheExpressions()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("a").Set("n", "secret").If(Attr("total").Gt(100)).SendAsync());

        // Assert
        await Assert.That((failure.Expressions!["UpdateExpression"], failure.Expressions["ConditionExpression"])).IsEqualTo(("SET #n0 = :v0", "#n1 > :v1"));
    }

    [Test]
    public async Task SendAsync_ValidationFailure_MapsThePlaceholdersToNamesAndTypes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("a").Set("n", "secret").If(Attr("total").Gt(100)).SendAsync());

        // Assert
        await Assert.That((failure.ExpressionNames!["#n1"], failure.ExpressionValueTypes![":v0"], failure.ExpressionValueTypes[":v1"])).IsEqualTo(("total", "S", "N"));
    }

    [Test]
    public async Task SendAsync_ValidationFailure_MessageGainsOneLineWithoutValues()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("a").Set("n", "secret").If(Attr("total").Gt(100)).SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Rejected + Environment.NewLine + "Expression: #n1 > :v1; SET #n0 = :v0, where #n0 = n, #n1 = total, :v0 (S), :v1 (N)");
    }

    [Test]
    public async Task SendAsync_ProjectionFails_ExplainsTheProjection()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Get("a").Project("x").SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith("Expression: #n0, where #n0 = x");
    }

    [Test]
    public async Task SendAsync_RemoveFails_HasNoValues()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("a").Remove("x").SendAsync());

        // Assert
        await Assert.That(failure.ExpressionValueTypes).IsEmpty();
    }

    [Test]
    public async Task SendAsync_NoExpressions_LeavesTheExceptionAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Get("a").SendAsync());

        // Assert
        await Assert.That((failure.Message, failure.Expressions, failure.ExpressionNames, failure.ExpressionValueTypes)).IsEqualTo((Rejected, (IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, string>?)null));
    }

    [Test]
    public async Task SendAsync_ConditionOnly_HasNoNameWhenThereIsNone()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var name = "status";

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("a").If($"pending = {name} AND other = {"open"}").SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith("Expression: pending = :v0 AND other = :v1, where :v0 (S), :v1 (S)");
    }

    [Test]
    public async Task SendAsync_DeeplyNestedItem_StillExplains()
    {
        // Arrange (DynamoDB allows 32 levels, and each level is two JSON levels)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        AttributeValue nested = "leaf";
        for (var i = 0; i < 32; i++)
        {
            nested = AttributeValue.FromMap([KeyValuePair.Create("m", nested)]);
        }

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Put(new Item { ["pk"] = "a", ["deep"] = nested }).IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.Expressions!["ConditionExpression"]).IsEqualTo("attribute_not_exists(#n0)");
    }

    [Test]
    public async Task SendAsync_RequestApiFailure_StaysAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(() => DynaSharp.Requests.DynamoDbRequests.GetItemAsync(client, TestClient.GetItem()));

        // Assert
        await Assert.That((failure.Message, failure.Expressions)).IsEqualTo((Rejected, (IReadOnlyDictionary<string, string>?)null));
    }

    [Test]
    public async Task SendAsync_TransactionFails_NamesTheExpressionsOfEachAction()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactWrite().Update(shop, "a").Set("n", "secret").Delete(shop, "b").If(Attr("total").Gt(100)).SendAsync());

        // Assert
        await Assert.That((failure.Expressions!["TransactItems[0].UpdateExpression"], failure.Expressions["TransactItems[1].ConditionExpression"])).IsEqualTo(("SET #n0 = :v0", "#n0 > :v0"));
    }

    [Test]
    public async Task SendAsync_TransactionFails_KeepsThePlaceholdersOfTheActionsApart()
    {
        // Arrange (each action has its own #n0 and :v0)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactWrite().Update(shop, "a").Set("n", "secret").Delete(shop, "b").If(Attr("total").Gt(100)).SendAsync());

        // Assert
        await Assert.That((failure.ExpressionNames!["TransactItems[0].#n0"], failure.ExpressionNames["TransactItems[1].#n0"], failure.ExpressionValueTypes!["TransactItems[0].:v0"], failure.ExpressionValueTypes["TransactItems[1].:v0"])).IsEqualTo(("n", "total", "S", "N"));
    }

    [Test]
    public async Task SendAsync_TransactionFails_MessageNamesTheActions()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactWrite().Update(shop, "a").Set("n", "secret").SendAsync());

        // Assert
        await Assert.That(failure.Message).EndsWith("Expression: TransactItems[0]: SET #n0 = :v0, where TransactItems[0].#n0 = n, TransactItems[0].:v0 (S)");
    }

    [Test]
    public async Task SendAsync_TransactionFails_NeverCarriesAValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactWrite().Put(shop, new Item { ["pk"] = "key-secret" }).If(Attr("n").Eq("value-secret")).SendAsync());

        // Assert
        await Assert.That(failure.ToString()).DoesNotContain("secret");
    }

    [Test]
    public async Task SendAsync_TransactionWithoutExpressions_LeavesTheExceptionAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactWrite().Delete(shop, "a").SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo(Rejected);
    }

    [Test]
    public async Task SendAsync_TransactGetFails_LeavesTheExceptionAsItIs()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactGet().Get(shop, "a", default, out _).SendAsync());

        // Assert
        await Assert.That(failure.Expressions).IsNull();
    }

    [Test]
    public async Task SendAsync_BatchGetFails_NamesTheProjectionOfTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.BatchGet().Key("a").Project("status").SendAsync());

        // Assert
        await Assert.That((failure.Expressions!["RequestItems.Shop.ProjectionExpression"], failure.ExpressionNames!["RequestItems.Shop.#n0"])).IsEqualTo(("#n0", "status"));
    }

    [Test]
    public async Task SendAsync_BatchWriteFails_LeavesTheExceptionAsItIs()
    {
        // Arrange (the writes of a batch have no expressions)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.BatchWrite().Delete("a").SendAsync());

        // Assert
        await Assert.That(failure.Expressions).IsNull();
    }

    [Test]
    public async Task SendAsync_ValidationFailure_NeverCarriesAValue()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", Rejected));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await shop.Update("key-secret").Set("n", "value-secret").SendAsync());

        // Assert
        await Assert.That(failure.ToString()).DoesNotContain("secret");
    }
}
