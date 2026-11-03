using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the API reference, BatchGetItem: Responses has a list per table of the request, UnprocessedKeys is in every answer.
public sealed class BatchGetItemTests
{
    [Test]
    public async Task Handle_KeysOverTwoTables_AnswersTheItemsThatExist()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"name":{"S":"Ann"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "BatchGetItem",
            """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"u#1"}},{"pk":{"S":"u#2"}}],"ConsistentRead":true},"Orders":{"Keys":[{"pk":{"S":"o#1"},"sk":{"N":"1"}}],"ConsistentRead":false}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Responses":{"Users":[{"pk":{"S":"u#1"},"name":{"S":"Ann"}}],"Orders":[]},"UnprocessedKeys":{}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_TotalCapacity_RoundsEachKeyOnItsOwnAndSumsPerTable()
    {
        // Arrange: each key is a read of its own, at least 1 unit, half of that eventually consistent; a key without an item costs a read of nothing
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"name":{"S":"Ann"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "BatchGetItem",
            """
            {"RequestItems":{"Users":{"Keys":[{"pk":{"S":"u#1"}},{"pk":{"S":"u#2"}}],"ConsistentRead":true},"Orders":{"Keys":[{"pk":{"S":"o#1"},"sk":{"N":"1"}}]}},
             "ReturnConsumedCapacity":"TOTAL"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"TableName":"Users","CapacityUnits":2},{"TableName":"Orders","CapacityUnits":0.5}]""", body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    public async Task Handle_Projection_AnswersOnlyTheNamedAttributes()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"name":{"S":"Ann"},"email":{"S":"a@example.com"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "BatchGetItem", """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"u#1"}}],"ProjectionExpression":"#n","ExpressionAttributeNames":{"#n":"name"}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Responses":{"Users":[{"name":{"S":"Ann"}}]},"UnprocessedKeys":{}}""", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"RequestItems":{}}""", "1 validation error detected: Value at 'requestItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"RequestItems":{"Users":{"Keys":[]}}}""", "1 validation error detected: Value at 'keys' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"RequestItems":{"Users":{}}}""", "1 validation error detected: Value null at 'keys' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"}},{"pk":{"S":"a"}}]}}}""", "Provided list of item keys contains duplicates")]
    [Arguments("""{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"},"x":{"S":"b"}}]}}}""", "The provided key element does not match the schema")]
    [Arguments("""{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"}}],"ExpressionAttributeNames":{"#n":"name"}}}}""", "ExpressionAttributeNames can only be specified when using expressions")]
    [Arguments("""{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"}}],"ProjectionExpression":"#n"}}}""", "Invalid ProjectionExpression: An expression attribute name used in the document path is not defined; attribute name: #n")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "BatchGetItem", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task Handle_MoreThan100KeysOverTwoTables_AnswersTooManyItems()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        var users = string.Join(",", Enumerable.Range(0, 60).Select(i => $$$$$"""{"pk":{"S":"u#{{{{{i}}}}}"}}"""));
        var orders = string.Join(",", Enumerable.Range(0, 41).Select(i => $$$$$"""{"pk":{"S":"o"},"sk":{"N":"{{{{{i}}}}}"}}"""));

        // Act
        var answer = FakeCalls.Error(fake, "BatchGetItem", $$$$$"""{"RequestItems":{"Users":{"Keys":[{{{{{users}}}}}]},"Orders":{"Keys":[{{{{{orders}}}}}]}}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo("Too many items requested for the BatchGetItem call");
    }

    [Test]
    public async Task Handle_LegacyAttributesToGet_AnswersUnsupported()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "BatchGetItem", """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"}}],"AttributesToGet":["pk"]}}}""");

        // Assert
        await Assert.That((code, message.Contains("the member AttributesToGet in RequestItems of BatchGetItem", StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "BatchGetItem", """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"a"}}]}}}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }
}
