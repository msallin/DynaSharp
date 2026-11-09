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
    [Arguments(""","AttributesToGet":["total"]""", "the member AttributesToGet in GetItem")]
    [Arguments(""","ExpressionAttributeValues":{":v":{"S":"x"}}""", "the member ExpressionAttributeValues in GetItem")]
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
    public async Task Handle_NamesOfAnotherKindWithoutAnExpression_AnswersSerializationNamingTheMap()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ExpressionAttributeNames":{"#a":1}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo("The member ExpressionAttributeNames must be a string, and it is Number.");
    }

    [Test]
    [Arguments("TOTAL", false, """{"TableName":"Orders","CapacityUnits":0.5}""")]
    [Arguments("TOTAL", true, """{"TableName":"Orders","CapacityUnits":1.0}""")]
    [Arguments("INDEXES", true, """{"TableName":"Orders","CapacityUnits":1.0,"Table":{"CapacityUnits":1.0}}""")]
    public async Task Handle_ReturnConsumedCapacity_ReportsTheUnitsOfTheRead(string mode, bool consistent, string expected)
    {
        // Arrange: the item is far below 4 KB, one read unit, half of it eventually consistent
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", Put);

        // Act
        var body = FakeCalls.Ok(
            fake,
            "GetItem",
            $$$"""{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ConsistentRead":{{{(consistent ? "true" : "false")}}},"ReturnConsumedCapacity":"{{{mode}}}"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(expected, body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    public async Task Handle_ReturnConsumedCapacityForAKeyWithoutAnItem_ReportsAReadUnit()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ConsistentRead":true,"ReturnConsumedCapacity":"TOTAL"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"Orders","CapacityUnits":1.0}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ReturnConsumedCapacityOfALargeItem_ReportsOneUnitPerFourKilobytes()
    {
        // Arrange: 9000 bytes of text are three units of 4 KB, one and a half eventually consistent
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$"""{"TableName":"Orders","Item":{"pk":{"S":"c#1"},"sk":{"N":"1"},"text":{"S":"{{{{new string('x', 9000)}}}}"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ReturnConsumedCapacity":"TOTAL"}""");

        // Assert
        await Assert.That(body.GetProperty("ConsumedCapacity").GetProperty("CapacityUnits").GetDouble()).IsEqualTo(1.5);
    }

    [Test]
    public async Task Handle_ReturnConsumedCapacityNotKnown_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ReturnConsumedCapacity":"ALL"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((
            HttpStatusCode.BadRequest,
            "ValidationException",
            "1 validation error detected: Value 'ALL' at 'returnConsumedCapacity' failed to satisfy constraint: Member must satisfy enum value set: [NONE, TOTAL, INDEXES]"));
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
    public async Task Handle_AttributesToGetWithAProjectionExpression_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"AttributesToGet":["total"],"ProjectionExpression":"total"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {AttributesToGet} Expression parameters: {ProjectionExpression}"));
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

    [Test]
    public async Task Handle_ProjectionExpression_AnswersWhatItNamesWithoutTheKey()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", Put);

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ProjectionExpression":"#t, #l[0].sku","ExpressionAttributeNames":{"#t":"total","#l":"lines"}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Item":{"total":{"N":"12.5"},"lines":{"L":[{"M":{"sku":{"S":"KB-01"}}}]}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ProjectionWithANameItDoesNotUse_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ProjectionExpression":"sk","ExpressionAttributeNames":{"#t":"total"}}""");

        // Assert
        await Assert.That((code, message.Contains("unused in expressions", StringComparison.Ordinal))).IsEqualTo(("ValidationException", true));
    }

    [Test]
    public async Task Handle_ProjectionOfALargeItem_CostsTheWholeItem()
    {
        // Arrange: 9000 bytes of text are three units of 4 KB, whatever the projection takes
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$"""{"TableName":"Orders","Item":{"pk":{"S":"c#1"},"sk":{"N":"1"},"text":{"S":"{{{{new string('x', 9000)}}}}"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"},"sk":{"N":"1"}},"ProjectionExpression":"pk","ConsistentRead":true,"ReturnConsumedCapacity":"TOTAL"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Item":{"pk":{"S":"c#1"}},"ConsumedCapacity":{"TableName":"Orders","CapacityUnits":3.0}}""", body)).IsTrue();
    }
}
