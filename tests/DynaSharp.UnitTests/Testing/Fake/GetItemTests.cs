using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// The answer of GetItem as the API reference shows it: {"Item":{...}} for a hit and {} for a miss, the same with and without ConsistentRead.
public sealed class GetItemTests
{
    private const string Put = """{"TableName":"Orders","Item":{"pk":{"S":"c#1"},"sk":{"N":"1"},"total":{"N":"12.50"},"lines":{"L":[{"M":{"sku":{"S":"KB-01"}}}]}}}""";

    [Test]
    [Arguments("")]
    [Arguments(""","ConsistentRead":true""")]
    [Arguments(""","ConsistentRead":false""")]
    [Arguments(""","ReturnConsumedCapacity":"NONE" """)]
    public async Task Handle_StoredItem_AnswersIt(string options)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", Put);

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", $$$"""{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}}{{{options}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Item":{"pk":{"S":"c#1"},"sk":{"N":"1"},"total":{"N":"12.5"},"lines":{"L":[{"M":{"sku":{"S":"KB-01"}}}]}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_KeyNumberInOtherText_FindsTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", Put);

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1.000"}}}""");

        // Assert
        await Assert.That(body.TryGetProperty("Item", out _)).IsTrue();
    }

    [Test]
    public async Task Handle_NoItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"pk":{"S":"c#1"}}""", "The provided key element does not match the schema")]
    [Arguments("""{"pk":{"S":"c#1"},"sk":{"N":"1"},"x":{"S":"y"}}""", "The provided key element does not match the schema")]
    [Arguments("""{"pk":{"S":"c#1"},"other":{"N":"1"}}""", "The provided key element does not match the schema")]
    [Arguments("""{"pk":{"N":"1"},"sk":{"N":"1"}}""", "The provided key element does not match the schema")]
    [Arguments("""{"pk":{"S":"c#1"},"sk":{"S":"1"}}""", "The provided key element does not match the schema")]
    [Arguments("""{"pk":{"S":""},"sk":{"N":"1"}}""", "One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty string value. Key: pk")]
    [Arguments("""{"pk":{"S":"c#1"},"sk":{"N":"x"}}""", "The parameter cannot be converted to a numeric value: x")]
    [Arguments("""{"pk":{"X":"c#1"},"sk":{"N":"1"}}""", "Supplied AttributeValue is not valid in Key: Unknown DynamoDB type descriptor \"X\" at byte 7.")]
    public async Task Handle_KeyThatDoesNotFit_AnswersValidation(string key, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", $$"""{"TableName":"Orders","Key":{{key}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments(""","ProjectionExpression":"total" """, "the member ProjectionExpression in GetItem")]
    [Arguments(""","ReturnConsumedCapacity":"INDEXES" """, "ReturnConsumedCapacity INDEXES in GetItem")]
    [Arguments(""","AttributesToGet":["total"]""", "the member AttributesToGet in GetItem")]
    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupported(string options, string named)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var (status, code, message) = FakeCalls.Error(fake, "GetItem", $$$"""{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}}{{{options}}}}""");

        // Assert
        await Assert.That((status, code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo((HttpStatusCode.BadRequest, "UnsupportedByFakeException", true));
    }

    [Test]
    public async Task Handle_NamesWithoutAProjection_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ExpressionAttributeNames":{"#t":"total"}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "ExpressionAttributeNames can only be specified when using expressions"));
    }

    [Test]
    public async Task Handle_ConsistentReadNotABoolean_AnswersSerialization()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var (status, code, _) = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ConsistentRead":"yes"}""");

        // Assert
        await Assert.That((status, code)).IsEqualTo((HttpStatusCode.BadRequest, "SerializationException"));
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Cannot do operations on a non-existent table"));
    }
}
