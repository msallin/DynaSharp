using System.Net;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the examples of the API reference, PutItem and GetItem: the table "Thread" keyed by ForumName and Subject.
public sealed class PutItemTests
{
    private const string Thread = """
        {"TableName":"Thread","AttributeDefinitions":[{"AttributeName":"ForumName","AttributeType":"S"},{"AttributeName":"Subject","AttributeType":"S"}],
         "KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"Subject","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"}
        """;

    private const string Item = """
        {"LastPostDateTime":{"S":"201303190422"},"Tags":{"SS":["Update","Multiple Items","HelpMe"]},"ForumName":{"S":"Amazon DynamoDB"},
         "Message":{"S":"I want to update multiple items in a single call. What's the best way to do that?"},"Subject":{"S":"How do I update multiple items?"},
         "LastPostedBy":{"S":"fred@example.com"},"Views":{"N":"0010.0"}}
        """;

    private const string Key = """{"ForumName":{"S":"Amazon DynamoDB"},"Subject":{"S":"How do I update multiple items?"}}""";

    [Test]
    public async Task Handle_NewItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var body = FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_NewItem_GetItemReadsItWithCanonicalNumbers()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", $$$$$"""{"TableName":"Thread","Key":{{{{{Key}}}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$$$$"""{"Item":{{{{{Item.Replace("0010.0", "10", StringComparison.Ordinal).Replace("\"Update\",\"Multiple Items\",\"HelpMe\"", "\"HelpMe\",\"Multiple Items\",\"Update\"", StringComparison.Ordinal)}}}}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_SameKeyAgain_ReplacesTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}}}""");

        // Act
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Thread","Item":{"ForumName":{"S":"Amazon DynamoDB"},"Subject":{"S":"How do I update multiple items?"},"Views":{"N":"11"}}}""");

        // Assert
        var body = FakeCalls.Ok(fake, "GetItem", $$$$$"""{"TableName":"Thread","Key":{{{{{Key}}}}}}""");
        await Assert.That(FakeCalls.SameJson("""{"Item":{"ForumName":{"S":"Amazon DynamoDB"},"Subject":{"S":"How do I update multiple items?"},"Views":{"N":"11"}}}""", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"Expected":{"ForumName":{"Exists":false}}}""", "the member Expected in PutItem")]
    [Arguments("""{"ConditionalOperator":"AND"}""", "the member ConditionalOperator in PutItem")]
    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupportedAndWritesNothing(string member, string named)
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        var request = $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},{{{{{member[1..^1]}}}}}}""";

        // Act
        var (status, code, message) = FakeCalls.Error(fake, "PutItem", request);

        // Assert
        await Assert.That((status, code, message.Contains(named, StringComparison.Ordinal), fake.Items("Thread").Count))
            .IsEqualTo((HttpStatusCode.BadRequest, "UnsupportedByFakeException", true, 0));
    }

    [Test]
    public async Task Handle_ExplicitNones_WritesTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        var request = $$$$$"""
            {"TableName":"Thread","Item":{{{{{Item}}}}},"ReturnValues":"NONE","ReturnConsumedCapacity":"NONE","ReturnItemCollectionMetrics":"NONE","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}
            """;

        // Act
        FakeCalls.Ok(fake, "PutItem", request);

        // Assert
        await Assert.That(fake.Items("Thread").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("""{"ExpressionAttributeNames":{"#f":"ForumName"}}""", "ExpressionAttributeNames can only be specified when using expressions")]
    [Arguments("""{"ExpressionAttributeValues":{":v":{"S":"x"}}}""", "ExpressionAttributeValues can only be specified when using expressions")]
    [Arguments("""{"Item":{"ForumName":{"S":"a"}}}""", "One or more parameter values were invalid: Missing the key Subject in the item")]
    [Arguments("""{"Item":{"ForumName":{"N":"1"},"Subject":{"S":"a"}}}""", "One or more parameter values were invalid: Type mismatch for key ForumName expected: S actual: N")]
    [Arguments("""{"Item":{"ForumName":{"S":"a"},"Subject":{"S":"b"},"Views":{"N":"1e999"}}}""", "Number overflow. Attempting to store a number with magnitude larger than supported range")]
    [Arguments("""{"Item":{"ForumName":{"S":"a"},"Subject":{"S":"b"},"Tags":{"SS":["x","x"]}}}""", "One or more parameter values were invalid: Input collection [x, x] contains duplicates.")]
    [Arguments("""{"ReturnValues":"SOME"}""", "1 validation error detected: Value 'SOME' at 'returnValues' failed to satisfy constraint: Member must satisfy enum value set: [NONE, ALL_OLD, UPDATED_OLD, ALL_NEW, UPDATED_NEW]")]
    [Arguments("""{"ReturnValues":"ALL_NEW"}""", "Return values set to invalid value")]
    [Arguments("""{"ConditionExpression":"#v > :v","ExpressionAttributeNames":{"#v":"Views"}}""", "Invalid ConditionExpression: An expression attribute value used in expression is not defined; attribute value: :v")]
    [Arguments("""{"ConditionExpression":"attribute_exists(ForumName)","ExpressionAttributeValues":{":v":{"N":"1"}}}""", "Value provided in ExpressionAttributeValues unused in expressions: keys: {:v}")]
    [Arguments("""{"Expected":{"ForumName":{"Exists":false}},"ConditionExpression":"attribute_not_exists(ForumName)"}""", "Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {Expected} Expression parameters: {ConditionExpression}")]
    [Arguments("""{"TableName":"ab"}""", "1 validation error detected: Value 'ab' at 'tableName' failed to satisfy constraint: Member must have length greater than or equal to 3")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidation(string change, string message)
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        var members = new Dictionary<string, string> { ["TableName"] = "\"Thread\"", ["Item"] = Item };
        foreach (var member in System.Text.Json.JsonElement.Parse(change).EnumerateObject())
        {
            members[member.Name] = member.Value.GetRawText();
        }

        // Act
        var answer = FakeCalls.Error(fake, "PutItem", "{" + string.Join(",", members.Select(member => $"\"{member.Key}\":{member.Value}")) + "}");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    public async Task ThrowIfLegacyWithExpressions_BodyThatIsNotAnObject_LeavesItToTheParsing()
    {
        // Arrange
        var request = System.Text.Json.JsonElement.Parse("[]");

        // Act
        var act = () => ItemRequests.ThrowIfLegacyWithExpressions(request);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Cannot do operations on a non-existent table"));
    }

    [Test]
    public async Task Handle_ConditionHolds_WritesTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},"ConditionExpression":"attribute_not_exists(ForumName)"}""");

        // Assert
        await Assert.That(fake.Items("Thread").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Handle_ConditionFails_AnswersConditionalCheckFailedAndKeepsTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}}}""");

        // Act
        var (status, body) = FakeCalls.Send(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Key[..^1]}}}}},"Views":{"N":"1"}},"ConditionExpression":"attribute_not_exists(ForumName)"}""");

        // Assert
        await Assert.That((status, FakeCalls.SameJson("""{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed"}""", body), fake.Items("Thread")[0].ContainsKey("Tags")))
            .IsEqualTo((HttpStatusCode.BadRequest, true, true));
    }

    [Test]
    public async Task Handle_ConditionFailsWithAllOldOnFailure_AnswersTheItemInTheError()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Key}}}}}}""");

        // Act
        var (_, body) = FakeCalls.Send(
            fake,
            "PutItem",
            $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},"ConditionExpression":"attribute_not_exists(#f)","ExpressionAttributeNames":{"#f":"ForumName"},"ReturnValuesOnConditionCheckFailure":"ALL_OLD"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$$$$"""{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","message":"The conditional request failed","Item":{{{{{Key}}}}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_AllOldOverAnItem_ReturnsTheItemItReplaced()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Key}}}}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},"ReturnValues":"ALL_OLD"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$$$$"""{"Attributes":{{{{{Key}}}}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_AllOldOfANewItem_AnswersAnEmptyObject()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var body = FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},"ReturnValues":"ALL_OLD"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_TotalCapacity_ReportsOneWriteUnit()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var body = FakeCalls.Ok(fake, "PutItem", $$$$$"""{"TableName":"Thread","Item":{{{{{Item}}}}},"ReturnConsumedCapacity":"TOTAL","ReturnItemCollectionMetrics":"SIZE"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ConsumedCapacity":{"TableName":"Thread","CapacityUnits":1}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_IndexesOnATableWithIndexes_ReportsTheShareOfEachIndexAndTheItemCollection()
    {
        // Arrange
        var fake = FakeCalls.With(FakeWrites.Forum);

        // Act
        var body = FakeCalls.Ok(
            fake,
            "PutItem",
            """{"TableName":"Forum","Item":{"pk":{"S":"a"},"sk":{"S":"1"},"author":{"S":"ann"},"date":{"S":"2026"}},"ReturnConsumedCapacity":"INDEXES","ReturnItemCollectionMetrics":"SIZE"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ConsumedCapacity":{"TableName":"Forum","CapacityUnits":3,"Table":{"CapacityUnits":1},
              "LocalSecondaryIndexes":{"ByDate":{"CapacityUnits":1}},"GlobalSecondaryIndexes":{"ByAuthor":{"CapacityUnits":1}}},
             "ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]}}
            """,
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_NoItem_AnswersValidationForTheMissingMember()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var answer = FakeCalls.Error(fake, "PutItem", """{"TableName":"Thread"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "1 validation error detected: Value null at 'item' failed to satisfy constraint: Member must not be null"));
    }
}
