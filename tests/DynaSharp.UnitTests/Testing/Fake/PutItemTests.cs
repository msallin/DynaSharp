using System.Net;

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
        var body = FakeCalls.Ok(fake, "PutItem", $$"""{"TableName":"Thread","Item":{{Item}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", body)).IsTrue();
    }

    [Test]
    public async Task Handle_NewItem_GetItemReadsItWithCanonicalNumbers()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$"""{"TableName":"Thread","Item":{{Item}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "GetItem", $$"""{"TableName":"Thread","Key":{{Key}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson($$"""{"Item":{{Item.Replace("0010.0", "10", StringComparison.Ordinal)}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_SameKeyAgain_ReplacesTheItem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", $$"""{"TableName":"Thread","Item":{{Item}}}""");

        // Act
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Thread","Item":{"ForumName":{"S":"Amazon DynamoDB"},"Subject":{"S":"How do I update multiple items?"},"Views":{"N":"11"}}}""");

        // Assert
        var body = FakeCalls.Ok(fake, "GetItem", $$"""{"TableName":"Thread","Key":{{Key}}}""");
        await Assert.That(FakeCalls.SameJson("""{"Item":{"ForumName":{"S":"Amazon DynamoDB"},"Subject":{"S":"How do I update multiple items?"},"Views":{"N":"11"}}}""", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"ConditionExpression":"attribute_not_exists(ForumName)"}""", "the member ConditionExpression in PutItem")]
    [Arguments("""{"ReturnValues":"ALL_OLD"}""", "ReturnValues ALL_OLD in PutItem")]
    [Arguments("""{"ReturnConsumedCapacity":"TOTAL"}""", "ReturnConsumedCapacity TOTAL in PutItem")]
    [Arguments("""{"ReturnItemCollectionMetrics":"SIZE"}""", "ReturnItemCollectionMetrics SIZE in PutItem")]
    [Arguments("""{"Expected":{"ForumName":{"Exists":false}}}""", "the member Expected in PutItem")]
    [Arguments("""{"ConditionalOperator":"AND"}""", "the member ConditionalOperator in PutItem")]

    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupportedAndWritesNothing(string member, string named)
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        var request = $$"""{"TableName":"Thread","Item":{{Item}},{{member[1..^1]}}}""";

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
        var request = $$"""
            {"TableName":"Thread","Item":{{Item}},"ReturnValues":"NONE","ReturnConsumedCapacity":"NONE","ReturnItemCollectionMetrics":"NONE","ReturnValuesOnConditionCheckFailure":"ALL_OLD"}
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
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "PutItem", $$"""{"TableName":"Thread","Item":{{Item}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Cannot do operations on a non-existent table"));
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
