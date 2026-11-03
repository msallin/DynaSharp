using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Bodies after the API reference, BatchWriteItem: up to 25 puts and deletes over several tables, UnprocessedItems in every answer.
public sealed class BatchWriteItemTests
{
    [Test]
    public async Task Handle_PutsAndDeletesOverTwoTables_WritesThemAndAnswersNoUnprocessedItems()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"gone"}}}""");

        // Act
        var body = FakeCalls.Ok(
            fake,
            "BatchWriteItem",
            """
            {"RequestItems":{
              "Users":[{"PutRequest":{"Item":{"pk":{"S":"u#1"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"gone"}}}}],
              "Orders":[{"PutRequest":{"Item":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}}]}}
            """);

        // Assert
        await Assert.That((FakeCalls.SameJson("""{"UnprocessedItems":{}}""", body), fake.Items("Users").Single()["pk"].AsString(), fake.Items("Orders").Count)).IsEqualTo((true, "u#1", 1));
    }

    [Test]
    public async Task Handle_TotalCapacity_ReportsOneElementPerTable()
    {
        // Arrange: the Users item is about 2.5 KB, 3 units; the delete of nothing is 1
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);

        // Act
        var body = FakeCalls.Ok(
            fake,
            "BatchWriteItem",
            $$$$$"""
            {"RequestItems":{
              "Users":[{"PutRequest":{"Item":{"pk":{"S":"u#1"},"bio":{"S":"{{{{{FakeWrites.Text(2500)}}}}}"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"u#2"}}}}],
              "Orders":[{"PutRequest":{"Item":{"pk":{"S":"o#1"},"sk":{"N":"1"}}}}]},
             "ReturnConsumedCapacity":"TOTAL"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"UnprocessedItems":{},"ConsumedCapacity":[{"TableName":"Users","CapacityUnits":4},{"TableName":"Orders","CapacityUnits":1}]}
            """,
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_SizeOnATableWithALocalIndex_ReportsTheItemCollectionOfEachWrite()
    {
        // Arrange
        var fake = FakeCalls.With(FakeWrites.Forum, FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(
            fake,
            "BatchWriteItem",
            """
            {"RequestItems":{"Forum":[{"PutRequest":{"Item":{"pk":{"S":"a"},"sk":{"S":"1"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"b"},"sk":{"S":"1"}}}}],
                             "Users":[{"PutRequest":{"Item":{"pk":{"S":"u#1"}}}}]},
             "ReturnItemCollectionMetrics":"SIZE"}
            """);

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"UnprocessedItems":{},"ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]},{"ItemCollectionKey":{"pk":{"S":"b"}},"SizeEstimateRangeGB":[0,1]}]}}
            """,
            body)).IsTrue();
    }

    [Test]
    public async Task Handle_OneItemTooLarge_WritesNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u#1"},"v":{"S":"before"}}}""");

        // Act
        var answer = FakeCalls.Error(
            fake,
            "BatchWriteItem",
            $$$$$"""
            {"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"u#1"},"v":{"S":"after"}}}},{"PutRequest":{"Item":{"pk":{"S":"u#2"}}}},
                                      {"PutRequest":{"Item":{"pk":{"S":"u#3"},"v":{"S":"{{{{{FakeWrites.Text(410_000)}}}}}"}}}}]}}
            """);

        // Assert
        await Assert.That((answer.Message, fake.Items("Users").Single()["v"].AsString())).IsEqualTo(("Item size has exceeded the maximum allowed size", "before"));
    }

    [Test]
    [Arguments("""{"RequestItems":{}}""", "1 validation error detected: Value at 'requestItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"RequestItems":{"Users":[]}}""", "1 validation error detected: Value at 'requestItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"a"}}}}]}}""", "Provided list of item keys contains duplicates")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}}},"DeleteRequest":{"Key":{"pk":{"S":"b"}}}}]}}""", "Supplied WriteRequest must have exactly one of PutRequest or DeleteRequest")]
    [Arguments("""{"RequestItems":{"Users":[{}]}}""", "Supplied WriteRequest must have exactly one of PutRequest or DeleteRequest")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{}}]}}""", "1 validation error detected: Value null at 'item' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"RequestItems":{"Users":[{"DeleteRequest":{"Key":{"pk":{"N":"1"}}}}]}}""", "The provided key element does not match the schema")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"id":{"S":"a"}}}}]}}""", "The provided key element does not match the schema")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"N":"1"}}}}]}}""", "The provided key element does not match the schema")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"L":[{"S":"a"}]}}}}]}}""", "The provided key element does not match the schema")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":""}}}}]}}""", "One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty string value. Key: pk")]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}}}}]},"ReturnItemCollectionMetrics":"ALL"}""", "1 validation error detected: Value 'ALL' at 'returnItemCollectionMetrics' failed to satisfy constraint: Member must satisfy enum value set: [NONE, SIZE]")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "BatchWriteItem", request);

        // Assert
        await Assert.That((answer, fake.Items("Users").Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", message), 0));
    }

    [Test]
    public async Task Handle_MoreThan25WritesOverTwoTables_AnswersTooManyItems()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users, FakeCalls.Orders);
        var users = string.Join(",", Enumerable.Range(0, 20).Select(i => $$$$$"""{"PutRequest":{"Item":{"pk":{"S":"u#{{{{{i}}}}}"}}}}"""));
        var orders = string.Join(",", Enumerable.Range(0, 6).Select(i => $$$$$"""{"PutRequest":{"Item":{"pk":{"S":"o"},"sk":{"N":"{{{{{i}}}}}"}}}}"""));

        // Act
        var answer = FakeCalls.Error(fake, "BatchWriteItem", $$$$$"""{"RequestItems":{"Users":[{{{{{users}}}}}],"Orders":[{{{{{orders}}}}}]}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "Too many items requested for the BatchWriteItem call"));
    }

    [Test]
    public async Task Handle_26WritesToOneTable_AnswersTheLengthConstraint()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        var users = string.Join(",", Enumerable.Range(0, 26).Select(i => $$$$$"""{"PutRequest":{"Item":{"pk":{"S":"u#{{{{{i}}}}}"}}}}"""));

        // Act
        var answer = FakeCalls.Error(fake, "BatchWriteItem", $$$$$"""{"RequestItems":{"Users":[{{{{{users}}}}}]}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo("1 validation error detected: Value at 'requestItems' failed to satisfy constraint: Member must have length less than or equal to 25");
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "BatchWriteItem", """{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}}}}]}}""");

        // Assert
        await Assert.That(answer.Code).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    [Arguments("""{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"a"}},"ConditionExpression":"x"}}]}}""", "the member ConditionExpression in PutRequest of BatchWriteItem")]
    [Arguments("""{"RequestItems":{"Users":[{"DeleteRequest":{"Key":{"pk":{"S":"a"}},"Expected":{}}}]}}""", "the member Expected in DeleteRequest of BatchWriteItem")]
    [Arguments("""{"RequestItems":{"Users":[{"UpdateRequest":{}}]}}""", "the member UpdateRequest in RequestItems of BatchWriteItem")]
    public async Task Handle_MemberBatchWriteItemDoesNotHave_AnswersUnsupported(string request, string named)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "BatchWriteItem", request);

        // Assert
        await Assert.That((code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }
}
