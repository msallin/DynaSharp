using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// DeleteItem answers {} whether there was an item or not, as the API reference describes it without ReturnValues.
public sealed class DeleteItemTests
{
    private const string Key = """{"pk":{"S":"c#1"},"sk":{"N":"1"}}""";

    [Test]
    public async Task Handle_StoredItem_RemovesIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Orders","Item":{{{{{Key}}}}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}}}""");

        // Assert
        await Assert.That((FakeCalls.SameJson("{}", body), fake.Items("Orders").Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Handle_NoItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ConditionFails_AnswersConditionalCheckFailedAndKeepsTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Orders","Item":{"pk":{"S":"c#1"},"sk":{"N":"1"},"state":{"S":"open"}}}""");

        // Act
        var answer = FakeCalls.Error(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}},"ConditionExpression":"#s = :closed","ExpressionAttributeNames":{"#s":"state"},"ExpressionAttributeValues":{":closed":{"S":"closed"}}}""");

        // Assert
        await Assert.That((answer, fake.Items("Orders").Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ConditionalCheckFailedException", "The conditional request failed"), 1));
    }

    [Test]
    public async Task Handle_ConditionHoldsWithAllOld_RemovesTheItemAndReturnsIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Orders","Item":{{{{{Key}}}}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}},"ConditionExpression":"attribute_exists(pk)","ReturnValues":"ALL_OLD"}""");

        // Assert
        await Assert.That((FakeCalls.SameJson($$$$$"""{"Attributes":{{{{{Key}}}}}}""", body), fake.Items("Orders").Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Handle_TotalCapacityForNoItem_ReportsOneWriteUnit()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}},"ReturnConsumedCapacity":"TOTAL"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"Orders","CapacityUnits":1}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_UpdatedNew_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}},"ReturnValues":"UPDATED_NEW"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "1 validation error detected: ReturnValues can only be ALL_OLD or NONE"));
    }

    [Test]
    public async Task Handle_ExpectedWithAConditionExpression_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "DeleteItem", $$$$$"""{"TableName":"Orders","Key":{{{{{Key}}}}},"Expected":{"total":{"Exists":true}},"ConditionExpression":"attribute_exists(total)"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {Expected} Expression parameters: {ConditionExpression}"));
    }

    [Test]
    public async Task Handle_KeyWithoutTheSortKey_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Orders);

        // Act
        var answer = FakeCalls.Error(fake, "DeleteItem", """{"TableName":"Orders","Key":{"pk":{"S":"c#1"}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "The provided key element does not match the schema"));
    }
}
