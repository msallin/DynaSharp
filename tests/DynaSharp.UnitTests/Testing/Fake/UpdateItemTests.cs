using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the API reference, UpdateItem: an update creates the item it names when there is none, and ReturnValues takes all five values.
public sealed class UpdateItemTests
{
    private const string Key = """{"pk":{"S":"u#1"}}""";

    [Test]
    public async Task Handle_SetOnANewItem_CreatesItWithItsKeyAndReturnsAllNew()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET email = :e","ExpressionAttributeValues":{":e":{"S":"a@example.com"}},"ReturnValues":"ALL_NEW"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Attributes":{"pk":{"S":"u#1"},"email":{"S":"a@example.com"}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_AddEmptyStringToAStringSet_ReadsBackTheEmptyStringFirst()
    {
        // Arrange: captured from DynamoDB, which answers set members sorted.
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"ADD tags :a","ExpressionAttributeValues":{":a":{"SS":["a"]}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"ADD tags :e","ExpressionAttributeValues":{":e":{"SS":[""]}},"ReturnValues":"UPDATED_NEW"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Attributes":{"tags":{"SS":["","a"]}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_AddTwice_AddsTheNumbers()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        var request = $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"ADD visits :one","ExpressionAttributeValues":{":one":{"N":"1"}},"ReturnValues":"UPDATED_NEW"}""";
        FakeCalls.Ok(fake, "UpdateItem", request);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", request);

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Attributes":{"visits":{"N":"2"}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_UpdatedOldOnAnItem_ReturnsTheChangedAttributesAsTheyWere()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"email":{"S":"old@example.com"},"name":{"S":"Ann"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET email = :e","ExpressionAttributeValues":{":e":{"S":"new@example.com"}},"ReturnValues":"UPDATED_OLD"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Attributes":{"email":{"S":"old@example.com"}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_UpdatedOldOnANewItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET email = :e","ExpressionAttributeValues":{":e":{"S":"a@example.com"}},"ReturnValues":"UPDATED_OLD"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_AllOld_ReturnsTheWholeItemAsItWas()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"name":{"S":"Ann"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"REMOVE #n","ExpressionAttributeNames":{"#n":"name"},"ReturnValues":"ALL_OLD"}""");

        // Assert
        await Assert.That((FakeCalls.SameJson("""{"Attributes":{"pk":{"S":"u#1"},"name":{"S":"Ann"}}}""", body), fake.Items("Users")[0].Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task Handle_NoUpdateExpressionAndNoItem_CreatesTheItemWithItsKey()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"ReturnValues":"ALL_NEW"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$$$$"""{"Attributes":{{{{{Key}}}}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_NoUpdateExpressionOnAnItem_LeavesItAsItIs()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"name":{"S":"Ann"}}}""");

        // Act
        FakeCalls.Ok(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}}}""");

        // Assert
        await Assert.That(fake.Items("Users")[0]["name"].AsString()).IsEqualTo("Ann");
    }

    [Test]
    public async Task Handle_ConditionFailsWithAllOldOnFailure_AnswersTheItemAndChangesNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"version":{"N":"2"}}}""");

        // Act
        var (status, body) = FakeCalls.Send(
            fake,
            "UpdateItem",
            $$$$$"""
            {"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET version = :next","ConditionExpression":"version = :expected",
             "ExpressionAttributeValues":{":next":{"N":"2"},":expected":{"N":"1"}},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}
            """);

        // Assert
        await Assert.That((status, FakeCalls.SameJson(
            """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed","Item":{"pk":{"S":"u#1"},"version":{"N":"2"}}}""",
            body))).IsEqualTo((HttpStatusCode.BadRequest, true));
    }

    [Test]
    public async Task Handle_ConditionHolds_AppliesTheUpdate()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"version":{"N":"1"}}}""");

        // Act
        FakeCalls.Ok(
            fake,
            "UpdateItem",
            $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET version = :next","ConditionExpression":"version = :expected","ExpressionAttributeValues":{":next":{"N":"2"},":expected":{"N":"1"}}}""");

        // Assert
        await Assert.That(fake.Items("Users")[0]["version"].AsNumber()).IsEqualTo("2");
    }

    [Test]
    [Arguments("""{"UpdateExpression":"SET pk = :v","ExpressionAttributeValues":{":v":{"S":"x"}}}""", "One or more parameter values were invalid: Cannot update attribute pk. This attribute is part of the key")]
    [Arguments("""{"UpdateExpression":"SET a = :v","ExpressionAttributeValues":{":v":{"S":"x"},":w":{"S":"y"}}}""", "Value provided in ExpressionAttributeValues unused in expressions: keys: {:w}")]
    [Arguments("""{"ExpressionAttributeNames":{"#a":"a"}}""", "ExpressionAttributeNames can only be specified when using expressions")]
    [Arguments("""{"UpdateExpression":"ADD a :v","ExpressionAttributeValues":{":v":{"S":"x"}}}""", "Invalid UpdateExpression: Incorrect operand type for operator or function; operator: ADD, operand type: STRING")]
    [Arguments("""{"ReturnValues":"SOME"}""", "1 validation error detected: Value 'SOME' at 'returnValues' failed to satisfy constraint: Member must satisfy enum value set: [NONE, ALL_OLD, UPDATED_OLD, ALL_NEW, UPDATED_NEW]")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidationAndWritesNothing(string members, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},{{{{{members[1..^1]}}}}}}""");

        // Assert
        await Assert.That((answer, fake.Items("Users").Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", message), 0));
    }

    [Test]
    public async Task Handle_UpdateMakesTheItemTooLarge_AnswersValidationAndKeepsTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Users","Item":{"pk":{"S":"u#1"},"a":{"S":"{{{{{FakeWrites.Text(300_000)}}}}}"}}}""");

        // Act
        var answer = FakeCalls.Error(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"UpdateExpression":"SET b = :v","ExpressionAttributeValues":{":v":{"S":"{{{{{FakeWrites.Text(200_000)}}}}}"}}}""");

        // Assert
        await Assert.That((answer.Message, fake.Items("Users")[0].ContainsKey("b"))).IsEqualTo(("Item size has exceeded the maximum allowed size", false));
    }

    [Test]
    public async Task Handle_IndexKeyChanges_ReportsTheDeleteAndThePutOfTheIndex()
    {
        // Arrange
        var fake = FakeCalls.With(FakeWrites.Forum);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Forum","Item":{"pk":{"S":"a"},"sk":{"S":"1"},"author":{"S":"ann"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "UpdateItem",
            """{"TableName":"Forum","Key":{"pk":{"S":"a"},"sk":{"S":"1"}},"UpdateExpression":"SET author = :b","ExpressionAttributeValues":{":b":{"S":"bob"}},"ReturnConsumedCapacity":"INDEXES"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":{"TableName":"Forum","CapacityUnits":3,"Table":{"CapacityUnits":1},
              "GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":2}}}}
            """,
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_LegacyAttributeUpdates_AnswersUnsupported()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "UpdateItem", $$$$$"""{"TableName":"Users","Key":{{{{{Key}}}}},"AttributeUpdates":{"a":{"Action":"DELETE"}}}""");

        // Assert
        await Assert.That((code, message.Contains("the member AttributeUpdates in UpdateItem", StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }
}
