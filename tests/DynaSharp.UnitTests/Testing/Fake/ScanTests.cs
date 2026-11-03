using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Scan as the API reference describes it: Items, Count and ScannedCount, LastEvaluatedKey when the page was cut, Segment and TotalSegments for a parallel scan in which every item
// is in exactly one segment, and the read units of what was read.
public sealed class ScanTests
{
    [Test]
    public async Task Handle_EmptyTable_AnswersNoItems()
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[],"Count":0,"ScannedCount":0}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_Table_AnswersEveryItem()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3), ("b", 2));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","ConsistentRead":true}""");

        // Assert
        await Assert.That((string.Join(",", ReadTables.Keys(body).Order(StringComparer.Ordinal)), body.GetProperty("Count").GetInt32(), body.GetProperty("ScannedCount").GetInt32(), body.TryGetProperty("LastEvaluatedKey", out _)))
            .IsEqualTo(("a/1,a/2,a/3,b/1,b/2", 5, 5, false));
    }

    [Test]
    public async Task Handle_Limit_AnswersThatManyItemsAndTheKeyOfTheLast()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 4));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","Limit":2}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"pk":{"S":"a"},"sk":{"N":"2"}}""", body.GetProperty("LastEvaluatedKey"))).IsTrue();
    }

    [Test]
    public async Task Handle_FollowingLastEvaluatedKey_AnswersEveryItemOnce()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 5), ("b", 5));

        // Act
        var keys = ReadTables.ReadAll(fake, "Scan", """{"TableName":"Shop","Limit":3""");

        // Assert
        await Assert.That(keys.Order(StringComparer.Ordinal)).IsEquivalentTo(Enumerable.Range(1, 5).Select(static i => $"a/{i}").Concat(Enumerable.Range(1, 5).Select(static i => $"b/{i}")));
    }

    [Test]
    public async Task Handle_ParallelSegments_TogetherAnswerEveryItemOnce()
    {
        // Arrange
        var fake = ReadTables.ShopWith([.. Enumerable.Range(0, 10).Select(static p => ($"p{p}", 3))]);

        // Act
        var keys = Enumerable.Range(0, 4).SelectMany(segment => ReadTables.ReadAll(fake, "Scan", $$"""{"TableName":"Shop","Limit":2,"Segment":{{segment}},"TotalSegments":4""")).ToList();

        // Assert
        await Assert.That((keys.Count, keys.Distinct(StringComparer.Ordinal).Count())).IsEqualTo((30, 30));
    }

    [Test]
    public async Task Handle_StartKeyOfAnotherSegment_AnswersValidation()
    {
        // Arrange: of two segments, the one that does not hold partition "a"
        var fake = ReadTables.ShopWith(("a", 1));
        var other = 1 - global::DynaSharp.Testing.Fake.Segments.Of("a", 2);

        // Act
        var answer = FakeCalls.Error(fake, "Scan", $$$$"""{"TableName":"Shop","Segment":{{{{other}}}},"TotalSegments":2,"ExclusiveStartKey":{"pk":{"S":"a"},"sk":{"N":"1"}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "The provided Exclusive start key does not map to the provided Segment and TotalSegments values."));
    }

    [Test]
    public async Task Handle_StartKeyOfItsSegment_GoesOnAfterIt()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3));
        var segment = global::DynaSharp.Testing.Fake.Segments.Of("a", 2);

        // Act
        var body = FakeCalls.Ok(fake, "Scan", $$$$"""{"TableName":"Shop","Segment":{{{{segment}}}},"TotalSegments":2,"ExclusiveStartKey":{"pk":{"S":"a"},"sk":{"N":"1"}}}""");

        // Assert
        await Assert.That(ReadTables.Keys(body)).IsEquivalentTo(["a/2", "a/3"]);
    }

    [Test]
    [Arguments(""","Segment":0""", "The TotalSegments parameter is required but was not present in the request when Segment parameter is present")]
    [Arguments(""","TotalSegments":2""", "The Segment parameter is required but was not present in the request when parameter TotalSegments is present")]
    [Arguments(""","Segment":0,"TotalSegments":0""", "1 validation error detected: Value '0' at 'totalSegments' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments(""","Segment":0,"TotalSegments":1000001""", "1 validation error detected: Value '1000001' at 'totalSegments' failed to satisfy constraint: Member must have value less than or equal to 1000000")]
    [Arguments(""","Segment":-1,"TotalSegments":2""", "1 validation error detected: Value '-1' at 'segment' failed to satisfy constraint: Member must have value greater than or equal to 0")]
    [Arguments(""","Segment":2,"TotalSegments":2""", "The Segment parameter is zero-based and must be less than parameter TotalSegments: Segment: 2 is not less than TotalSegments: 2")]
    [Arguments(""","Limit":0""", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments(""","IndexName":"Nope" """, "The table does not have the specified index: Nope")]
    [Arguments(""","IndexName":"ByCustomer","ConsistentRead":true""", "Consistent reads are not supported on global secondary indexes")]
    [Arguments(""","ExpressionAttributeNames":{"#a":"a"}""", "ExpressionAttributeNames can only be specified when using expressions")]
    [Arguments(""","ExpressionAttributeValues":{":a":{"S":"a"}}""", "ExpressionAttributeValues can only be specified when using expressions")]
    [Arguments(""","Select":"SPECIFIC_ATTRIBUTES" """, "Must specify the AttributesToGet or ProjectionExpression when choosing to get SPECIFIC_ATTRIBUTES")]
    [Arguments(""","ExclusiveStartKey":{}""", "The provided starting key is invalid: The provided key element does not match the schema")]
    [Arguments(""","ScanFilter":{"a":{"ComparisonOperator":"NULL"}},"FilterExpression":"attribute_exists(a)" """, "Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {ScanFilter} Expression parameters: {FilterExpression}")]
    public async Task Handle_RequestDynamoDbRejects_AnswersValidation(string options, string message)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var answer = FakeCalls.Error(fake, "Scan", $$$"""{"TableName":"Shop"{{{options}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments(""","Limit":"2" """)]
    [Arguments(""","Segment":"0","TotalSegments":2""")]
    [Arguments(""","Segment":0,"TotalSegments":true""")]
    [Arguments(""","ConsistentRead":1""")]
    [Arguments(""","IndexName":1""")]
    public async Task Handle_MemberOfAnotherKind_AnswersSerialization(string options)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (status, code, _) = FakeCalls.Error(fake, "Scan", $$$"""{"TableName":"Shop"{{{options}}}}""");

        // Assert
        await Assert.That((status, code)).IsEqualTo((HttpStatusCode.BadRequest, "SerializationException"));
    }

    [Test]
    [Arguments("ScanFilter")]
    [Arguments("AttributesToGet")]
    [Arguments("ConditionalOperator")]
    [Arguments("ScanIndexForward")]
    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupported(string member)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Scan", $$"""{"TableName":"Shop","{{member}}":true}""");

        // Assert
        await Assert.That((code, message.Contains($"the member {member} in Scan", StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "Scan", """{"TableName":"Shop"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Cannot do operations on a non-existent table"));
    }

    [Test]
    public async Task Handle_SelectCount_AnswersTheCountsWithoutItems()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 4));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","Select":"COUNT"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Count":4,"ScannedCount":4}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_GlobalIndex_AnswersTheItemsWithItsKeyAsItHoldsThem()
    {
        // Arrange: the item without a customer is not in the sparse index, and the index holds keys only
        var fake = ReadTables.ShopWith(("a", 1));
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Shop","Item":{"pk":{"S":"a"},"sk":{"N":"9"}}}""");

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","IndexName":"ByCustomer"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[{"pk":{"S":"a"},"sk":{"N":"1"},"customer":{"S":"k"}}],"Count":1,"ScannedCount":1}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_GlobalIndexWithLimit_KeyHoldsTheIndexKeyAndTheTableKey()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 2));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","IndexName":"ByCustomer","Limit":1}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"customer":{"S":"k"},"pk":{"S":"a"},"sk":{"N":"1"}}""", body.GetProperty("LastEvaluatedKey"))).IsTrue();
    }

    [Test]
    public async Task Handle_LocalIndexWithConsistentRead_AnswersItsItems()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 2));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","IndexName":"ByStatus","ConsistentRead":true,"Select":"ALL_ATTRIBUTES"}""");

        // Assert
        await Assert.That(body.GetProperty("Items")[0].GetProperty("v").GetProperty("N").GetString()).IsEqualTo("1");
    }

    [Test]
    [Arguments("TOTAL", "", """{"TableName":"Shop","CapacityUnits":0.5}""")]
    [Arguments("INDEXES", "", """{"TableName":"Shop","CapacityUnits":0.5,"Table":{"CapacityUnits":0.5}}""")]
    [Arguments("INDEXES", ""","IndexName":"ByCustomer" """, """{"TableName":"Shop","CapacityUnits":0.5,"Table":{"CapacityUnits":0.0},"GlobalSecondaryIndexes":{"ByCustomer":{"CapacityUnits":0.5}}}""")]
    [Arguments(
        "INDEXES",
        ""","IndexName":"ByStatus","Select":"ALL_ATTRIBUTES" """,
        """{"TableName":"Shop","CapacityUnits":2.0,"Table":{"CapacityUnits":1.5},"LocalSecondaryIndexes":{"ByStatus":{"CapacityUnits":0.5}}}""")]
    public async Task Handle_ReturnConsumedCapacity_ReportsTheUnitsOfWhatWasRead(string mode, string options, string expected)
    {
        // Arrange: three small items are one read of 4 KB, half a unit eventually consistent; a local index of keys fetches each item from the table for half a unit each
        var fake = ReadTables.ShopWith(("a", 3));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", $$$"""{"TableName":"Shop","ReturnConsumedCapacity":"{{{mode}}}"{{{options}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(expected, body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    public async Task Handle_ReturnConsumedCapacityNone_ReportsNothing()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 1));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","ReturnConsumedCapacity":"NONE"}""");

        // Assert
        await Assert.That(body.TryGetProperty("ConsumedCapacity", out _)).IsFalse();
    }

    [Test]
    public async Task Handle_SelectNotKnown_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Scan", """{"TableName":"Shop","Select":"EVERYTHING"}""");

        // Assert
        await Assert.That((code, message.StartsWith("1 validation error detected: Value 'EVERYTHING' at 'select'", StringComparison.Ordinal))).IsEqualTo(("ValidationException", true));
    }

    [Test]
    public async Task Handle_FilterExpression_CountsTheMatchesAndEverythingRead()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 10));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","FilterExpression":"v >= :low","ExpressionAttributeValues":{":low":{"N":"8"}}}""");

        // Assert
        await Assert.That((string.Join(",", ReadTables.Keys(body)), body.GetProperty("Count").GetInt32(), body.GetProperty("ScannedCount").GetInt32())).IsEqualTo(("a/8,a/9,a/10", 3, 10));
    }

    [Test]
    public async Task Handle_FilterOnAKeyAttribute_AnswersTheMatches()
    {
        // Arrange: unlike a query, a scan may filter on a key attribute
        var fake = ReadTables.ShopWith(("a", 3));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","FilterExpression":"sk > :s","ExpressionAttributeValues":{":s":{"N":"2"}}}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/3");
    }

    [Test]
    public async Task Handle_FilterThatDropsAPage_AnswersAnEmptyPageWithAKey()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 6));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","Limit":2,"FilterExpression":"v > :t","ExpressionAttributeValues":{":t":{"N":"100"}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[],"Count":0,"ScannedCount":2,"LastEvaluatedKey":{"pk":{"S":"a"},"sk":{"N":"2"}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ProjectionExpression_AnswersOnlyWhatItNames()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 1));

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Shop","ProjectionExpression":"#s, v","ExpressionAttributeNames":{"#s":"status"}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"status":{"S":"s1"},"v":{"N":"1"}}]""", body.GetProperty("Items"))).IsTrue();
    }

    [Test]
    public async Task Handle_NameNoExpressionUses_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Scan", """{"TableName":"Shop","ProjectionExpression":"v","ExpressionAttributeNames":{"#s":"status"}}""");

        // Assert
        await Assert.That((code, message.Contains("unused in expressions", StringComparison.Ordinal))).IsEqualTo(("ValidationException", true));
    }
}
