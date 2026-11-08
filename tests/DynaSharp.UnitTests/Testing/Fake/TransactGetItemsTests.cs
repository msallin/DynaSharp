using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the API reference, TransactGetItems: one element of Responses per get, in its order, {} for a key without an item.
public sealed class TransactGetItemsTests
{
    [Test]
    public async Task Handle_TwoGets_AnswersOneElementPerGetInOrder()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o#1"},"sk":{"N":"1"},"total":{"N":"5"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactGetItems",
            """{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"u#1"}}}},{"Get":{"TableName":"Orders","Key":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}}]}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Responses":[{},{"Item":{"pk":{"S":"o#1"},"sk":{"N":"1"},"total":{"N":"5"}}}]}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_TotalCapacity_ReportsTwiceAConsistentReadPerItem()
    {
        // Arrange: the item of Orders is about 5 KB, 2 units read consistently; the missing Users item costs a read of nothing
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Orders","Item":{"pk":{"S":"o#1"},"sk":{"N":"1"},"note":{"S":"{{{{{FakeWrites.Text(5000)}}}}}"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactGetItems",
            """
            {"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"u#1"}}}},{"Get":{"TableName":"Orders","Key":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}}],
             "ReturnConsumedCapacity":"TOTAL"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"TableName":"Users","CapacityUnits":2},{"TableName":"Orders","CapacityUnits":4}]""", body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    public async Task Handle_ProjectionWithoutTheKey_AnswersOnlyWhatItNames()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o#1"},"sk":{"N":"1"},"total":{"N":"5"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "TransactGetItems", """{"TransactItems":[{"Get":{"TableName":"Orders","Key":{"pk":{"S":"o#1"},"sk":{"N":"1"}},"ProjectionExpression":"#t","ExpressionAttributeNames":{"#t":"total"}}}]}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Responses":[{"Item":{"total":{"N":"5"}}}]}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ProjectionMatchingNothing_AnswersAnEmptyElement()
    {
        // Arrange: DynamoDB leaves Item out when the projection matches no attribute of the item
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o#1"},"sk":{"N":"1"},"total":{"N":"5"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "TransactGetItems", """{"TransactItems":[{"Get":{"TableName":"Orders","Key":{"pk":{"S":"o#1"},"sk":{"N":"1"}},"ProjectionExpression":"nowhere"}}]}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Responses":[{}]}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_IndexesCapacity_ReportsReadCapacityUnitsOnTheTable()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Orders","Item":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "TransactGetItems",
            """{"TransactItems":[{"Get":{"TableName":"Orders","Key":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}}],"ReturnConsumedCapacity":"INDEXES"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """[{"TableName":"Orders","CapacityUnits":2,"ReadCapacityUnits":2,"Table":{"CapacityUnits":2,"ReadCapacityUnits":2}}]""",
            body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    [Arguments("""{"TransactItems":[]}""", "1 validation error detected: Value at 'TransactItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"TransactItems":[{}]}""", "1 validation error detected: Value null at 'get' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"a"}}}},{"Get":{"TableName":"Users","Key":{"pk":{"S":"a"}}}}]}""", "Transaction request cannot include multiple operations on one item")]
    [Arguments("""{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"a"}},"ExpressionAttributeNames":{"#a":"a"}}}]}""", "1 validation error detected: ExpressionAttributeNames can only be specified when using expressions")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "TransactGetItems", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments("""{"pk":{"N":"1"}}""")]
    [Arguments("""{"pk":{"S":"b"},"x":{"S":"1"}}""")]
    [Arguments("""{"id":{"S":"b"}}""")]
    public async Task Handle_KeyThatDoesNotMatchTheSchema_CancelsWithValidationErrorForThatGet(string key)
    {
        // Arrange: both emulators cancel the transaction for it, as TransactWriteItems does
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (status, body) = FakeCalls.Send(
            fake,
            "TransactGetItems",
            """{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"a"}}}},{"Get":{"TableName":"Users","Key":""" + key + "}}]}");

        // Assert
        await Assert.That((status, body.GetProperty("__type").GetString(), FakeCalls.SameJson(
            """[{"Code":"None"},{"Code":"ValidationError","Message":"The provided key element does not match the schema"}]""",
            body.GetProperty("CancellationReasons")))).IsEqualTo((HttpStatusCode.BadRequest, "com.amazonaws.dynamodb.v20120810#TransactionCanceledException", true));
    }

    [Test]
    public async Task Handle_101Gets_AnswersTheLengthConstraint()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        var gets = string.Join(",", Enumerable.Range(0, 101).Select(i => $$$$$"""{"Get":{"TableName":"Users","Key":{"pk":{"S":"u#{{{{{i}}}}}"}}}}"""));

        // Act
        var answer = FakeCalls.Error(fake, "TransactGetItems", $$$$$"""{"TransactItems":[{{{{{gets}}}}}]}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo("1 validation error detected: Value at 'TransactItems' failed to satisfy constraint: Member must have length less than or equal to 100");
    }

    [Test]
    [Arguments("""{"TransactItems":[{"Put":{}}]}""", "the member Put in TransactItems of TransactGetItems")]
    [Arguments("""{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"a"}},"ConsistentRead":true}}]}""", "the member ConsistentRead in Get of TransactGetItems")]
    public async Task Handle_MemberTransactGetItemsDoesNotHave_AnswersUnsupported(string request, string named)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "TransactGetItems", request);

        // Assert
        await Assert.That((code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }
}
