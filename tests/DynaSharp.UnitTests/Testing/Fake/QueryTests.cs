using System.Net;

namespace DynaSharp.UnitTests.Testing.Fake;

// Query as the API reference describes it: one partition by a key condition, the table or an index, in sort key order either way, cut into pages as a Scan is.
public sealed class QueryTests
{
    private const string ByPartition = """{"TableName":"Shop","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"a"}}""";

    [Test]
    [Arguments("", "Either the KeyConditions or KeyConditionExpression parameter must be specified in the request.")]
    [Arguments(""","KeyConditionExpression":"pk = :pk","Limit":0""", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments(""","KeyConditionExpression":"pk = :pk","IndexName":"Nope" """, "The table does not have the specified index: Nope")]
    [Arguments(""","KeyConditionExpression":"customer = :c","IndexName":"ByCustomer","ConsistentRead":true""", "Consistent reads are not supported on global secondary indexes")]
    [Arguments(""","KeyConditions":{"pk":{"ComparisonOperator":"EQ","AttributeValueList":[{"S":"a"}]}},"KeyConditionExpression":"pk = :pk" """, "Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {KeyConditions} Expression parameters: {KeyConditionExpression}")]
    public async Task Handle_RequestDynamoDbRejectsBeforeTheKeyCondition_AnswersValidation(string options, string message)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var answer = FakeCalls.Error(fake, "Query", $$$"""{"TableName":"Shop"{{{options}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments(""","ScanIndexForward":"no" """)]
    [Arguments(""","KeyConditionExpression":1""")]
    public async Task Handle_MemberOfAnotherKind_AnswersSerialization(string options)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (status, code, _) = FakeCalls.Error(fake, "Query", $$$"""{"TableName":"Shop"{{{options}}}}""");

        // Assert
        await Assert.That((status, code)).IsEqualTo((HttpStatusCode.BadRequest, "SerializationException"));
    }

    [Test]
    [Arguments(""","IndexName":1""", "The member IndexName must be a string, and it is Number.")]
    [Arguments(""","Limit":"1" """, "The member Limit must be a number, and it is String.")]
    [Arguments(""","ConsistentRead":"yes" """, "The member ConsistentRead must be a boolean, and it is String.")]
    [Arguments(""","ReturnConsumedCapacity":1""", "The member ReturnConsumedCapacity must be a string, and it is Number.")]
    [Arguments(""","ExpressionAttributeNames":[]""", "The member ExpressionAttributeNames must be an object, and it is Array.")]
    [Arguments(""","ExpressionAttributeValues":[]""", "The member ExpressionAttributeValues must be an object, and it is Array.")]
    [Arguments(""","ExpressionAttributeValues":{":pk":{"S":"a"}},"ProjectionExpression":1""", "The member ProjectionExpression must be a string, and it is Number.")]
    public async Task Handle_MemberOfAnotherKind_AnswersSerializationNamingIt(string options, string message)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var answer = FakeCalls.Error(fake, "Query", $$$"""{"TableName":"Shop","KeyConditionExpression":"pk = :pk"{{{options}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "SerializationException", message));
    }

    [Test]
    [Arguments("KeyConditions")]
    [Arguments("QueryFilter")]
    [Arguments("AttributesToGet")]
    [Arguments("ConditionalOperator")]
    [Arguments("Segment")]
    public async Task Handle_MemberTheFakeDoesNotImplement_AnswersUnsupported(string member)
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Query", $$"""{"TableName":"Shop","{{member}}":true}""");

        // Assert
        await Assert.That((code, message.Contains($"the member {member} in Query", StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task Handle_NoTable_AnswersResourceNotFound()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "Query", """{"TableName":"Shop","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"a"}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Cannot do operations on a non-existent table"));
    }

    [Test]
    public async Task Handle_Partition_AnswersItsItemsInSortKeyOrder()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 5), ("other", 3));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + ""","ConsistentRead":true}""");

        // Assert
        await Assert.That((string.Join(",", ReadTables.Keys(body)), body.GetProperty("Count").GetInt32(), body.GetProperty("ScannedCount").GetInt32(), body.TryGetProperty("LastEvaluatedKey", out _)))
            .IsEqualTo(("a/1,a/2,a/3,a/4,a/5", 5, 5, false));
    }

    [Test]
    public async Task Handle_ScanIndexForwardFalse_AnswersTheItemsTheOtherWayRound()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 5));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + ""","ScanIndexForward":false}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/5,a/4,a/3,a/2,a/1");
    }

    [Test]
    public async Task Handle_SortKeyBetween_AnswersTheRangeWithBothEnds()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 10));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","KeyConditionExpression":"pk = :pk AND sk BETWEEN :low AND :high","ExpressionAttributeValues":{":pk":{"S":"a"},":low":{"N":"3"},":high":{"N":"6"}}}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/3,a/4,a/5,a/6");
    }

    [Test]
    public async Task Handle_BeginsWithOnALocalIndex_AnswersTheEntriesInIndexOrder()
    {
        // Arrange: the index orders by status, a string, so "s10" comes before "s2"
        var fake = ReadTables.ShopWith(("a", 12));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","IndexName":"ByStatus","KeyConditionExpression":"pk = :pk AND begins_with(#s, :p)","ExpressionAttributeNames":{"#s":"status"},"ExpressionAttributeValues":{":pk":{"S":"a"},":p":{"S":"s1"}}}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/1,a/10,a/11,a/12");
    }

    [Test]
    [Arguments("sk <= :end", 5, 3, 3, false)]
    [Arguments("sk <= :end", 5, 3, 2, true)]
    [Arguments("sk <= :end", 2, 3, 2, false)]
    [Arguments("sk BETWEEN :end AND :end", 5, 3, 1, false)]
    public async Task Handle_LimitAtTheLastItemOfTheSortKeyCondition_LeavesOutLastEvaluatedKey(string sortKeyCondition, int items, int end, int limit, bool expected)
    {
        // Arrange: real DynamoDB ends such a page without a key, also with items of the partition beyond the condition (FidelityTests, run 42356511937)
        var fake = ReadTables.ShopWith(("a", items));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            $$$$$"""
            {"TableName":"Shop","KeyConditionExpression":"pk = :pk AND {{{{{sortKeyCondition}}}}}","ExpressionAttributeValues":{":pk":{"S":"a"},":end":{"N":"{{{{{end}}}}}"}},"Limit":{{{{{limit}}}}}}
            """);

        // Assert
        await Assert.That(body.TryGetProperty("LastEvaluatedKey", out _)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("ALL_ATTRIBUTES")]
    [Arguments("COUNT")]
    public async Task Handle_LimitAtTheLastItemOfThePartition_SendsLastEvaluatedKey(string select)
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 2));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + $$$$$""","Limit":2,"Select":"{{{{{select}}}}}"}""");

        // Assert
        await Assert.That(body.TryGetProperty("LastEvaluatedKey", out _)).IsTrue();
    }

    [Test]
    public async Task Handle_FollowingLastEvaluatedKey_AnswersEveryItemOnce()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 7));

        // Act
        var keys = ReadTables.ReadAll(fake, "Query", ByPartition + ""","Limit":3""");

        // Assert
        await Assert.That(string.Join(",", keys)).IsEqualTo("a/1,a/2,a/3,a/4,a/5,a/6,a/7");
    }

    [Test]
    public async Task Handle_DescendingWithAStartKey_GoesOnBelowIt()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 5));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + ""","Limit":2,"ScanIndexForward":false,"ExclusiveStartKey":{"pk":{"S":"a"},"sk":{"N":"4"}}}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/3,a/2");
    }

    [Test]
    public async Task Handle_FilterThatDropsAPage_AnswersAnEmptyPageWithAKey()
    {
        // Arrange: Limit counts the items read, and only sk 5 passes the filter
        var fake = ReadTables.ShopWith(("a", 5));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","KeyConditionExpression":"pk = :pk","FilterExpression":"v > :t","ExpressionAttributeValues":{":pk":{"S":"a"},":t":{"N":"4"}},"Limit":2}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[],"Count":0,"ScannedCount":2,"LastEvaluatedKey":{"pk":{"S":"a"},"sk":{"N":"2"}}}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_SelectCount_AnswersTheCountsWithoutItems()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 4));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + ""","Select":"COUNT"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Count":4,"ScannedCount":4}""", body)).IsTrue();
    }

    [Test]
    public async Task Handle_ProjectionExpression_AnswersOnlyWhatItNames()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 1));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + ""","ProjectionExpression":"sk, v"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"sk":{"N":"1"},"v":{"N":"1"}}]""", body.GetProperty("Items"))).IsTrue();
    }

    [Test]
    public async Task Handle_GlobalIndexWithAnInclude_AnswersItsEntriesByItsSortKey()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3), ("b", 1));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","IndexName":"ByTotal","KeyConditionExpression":"customer = :c AND #t >= :t","ExpressionAttributeNames":{"#t":"total"},"ExpressionAttributeValues":{":c":{"S":"k"},":t":{"N":"20"}},"ScanIndexForward":false}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            [{"pk":{"S":"a"},"sk":{"N":"3"},"customer":{"S":"k"},"total":{"N":"30"},"v":{"N":"3"}},
             {"pk":{"S":"a"},"sk":{"N":"2"},"customer":{"S":"k"},"total":{"N":"20"},"v":{"N":"2"}}]
            """,
            body.GetProperty("Items"))).IsTrue();
    }

    [Test]
    public async Task Handle_GlobalIndexWithLimit_KeyHoldsTheIndexKeyAndTheTableKey()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","IndexName":"ByTotal","KeyConditionExpression":"customer = :c","ExpressionAttributeValues":{":c":{"S":"k"}},"Limit":1}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"customer":{"S":"k"},"total":{"N":"10"},"pk":{"S":"a"},"sk":{"N":"1"}}""", body.GetProperty("LastEvaluatedKey"))).IsTrue();
    }

    [Test]
    public async Task Handle_PartitionWithoutItems_AnswersNoItems()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("b", 2));

        // Act
        var body = FakeCalls.Ok(fake, "Query", ByPartition + "}");

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"Items":[],"Count":0,"ScannedCount":0}""", body)).IsTrue();
    }

    [Test]
    [Arguments("""{"pk":{"S":"b"},"sk":{"N":"1"}}""", "The provided starting key is outside query boundaries based on provided conditions")]
    [Arguments("""{"pk":{"S":"a"},"sk":{"N":"9"}}""", "The provided starting key does not match the range key predicate")]
    [Arguments("""{"pk":{"S":"a"}}""", "The provided starting key is invalid: The provided key element does not match the schema")]
    public async Task Handle_StartKeyOutsideTheKeyCondition_AnswersValidation(string start, string message)
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3));

        // Act
        var answer = FakeCalls.Error(
            fake,
            "Query",
            $$$"""{"TableName":"Shop","KeyConditionExpression":"pk = :pk AND sk < :s","ExpressionAttributeValues":{":pk":{"S":"a"},":s":{"N":"5"}},"ExclusiveStartKey":{{{start}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments("TOTAL", "", """{"TableName":"Shop","CapacityUnits":0.5}""")]
    [Arguments(
        "INDEXES",
        ""","IndexName":"ByTotal" """,
        """{"TableName":"Shop","CapacityUnits":0.5,"Table":{"CapacityUnits":0.0},"GlobalSecondaryIndexes":{"ByTotal":{"CapacityUnits":0.5}}}""")]
    public async Task Handle_ReturnConsumedCapacity_ReportsTheUnitsOfWhatWasRead(string mode, string index, string expected)
    {
        // Arrange: five small items are one read of 4 KB, half a unit eventually consistent
        var fake = ReadTables.ShopWith(("a", 5));
        var condition = string.IsNullOrEmpty(index) ? "pk = :v" : "customer = :v";
        var value = string.IsNullOrEmpty(index) ? "a" : "k";

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            $$$"""{"TableName":"Shop","KeyConditionExpression":"{{{condition}}}","ExpressionAttributeValues":{":v":{"S":"{{{value}}}"}},"ReturnConsumedCapacity":"{{{mode}}}"{{{index}}}}""");

        // Assert
        await Assert.That(FakeCalls.SameJson(expected, body.GetProperty("ConsumedCapacity"))).IsTrue();
    }

    [Test]
    public async Task Handle_ValueNoExpressionUses_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Query", """{"TableName":"Shop","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"a"},":x":{"S":"b"}}}""");

        // Assert
        await Assert.That((code, message.Contains("unused in expressions", StringComparison.Ordinal))).IsEqualTo(("ValidationException", true));
    }

    [Test]
    [Arguments("", "#k > :s", "sk")]
    [Arguments(""","IndexName":"ByTotal" """, "v > :s AND #k > :s", "total")]
    public async Task Handle_FilterOnAKeyAttributeOfTheQuery_AnswersValidation(string index, string filter, string key)
    {
        // Arrange: a filter may not read a key attribute of the table or the index queried
        var fake = FakeCalls.With(ReadTables.Shop);
        var condition = string.IsNullOrEmpty(index) ? "pk = :v" : "customer = :v";

        // Act
        var answer = FakeCalls.Error(
            fake,
            "Query",
            $$$"""{"TableName":"Shop","KeyConditionExpression":"{{{condition}}}","FilterExpression":"{{{filter}}}","ExpressionAttributeNames":{"#k":"{{{key}}}"},"ExpressionAttributeValues":{":v":{"S":"a"},":s":{"N":"1"}}{{{index}}}}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", $"Filter Expression can only contain non-primary key attributes: Primary key attribute: {key}"));
    }

    [Test]
    public async Task Handle_MalformedKeyCondition_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(ReadTables.Shop);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "Query", """{"TableName":"Shop","KeyConditionExpression":"pk ==","ExpressionAttributeValues":{":pk":{"S":"a"}}}""");

        // Assert
        await Assert.That((code, message.Contains("Invalid KeyConditionExpression", StringComparison.Ordinal))).IsEqualTo(("ValidationException", true));
    }

    [Test]
    [Arguments("ByCustomer", "v", "", "Global secondary index ByCustomer does not project [v]")]
    [Arguments("ByCustomer", "#s.a, v, #t.x, pk, #s.b", "\"ExpressionAttributeNames\":{\"#s\":\"status\",\"#t\":\"total\"},", "Global secondary index ByCustomer does not project [status, v, total]")]
    [Arguments("ByTotal", "v, sk, #s[0]", "\"ExpressionAttributeNames\":{\"#s\":\"status\"},", "Global secondary index ByTotal does not project [status]")]
    public async Task Handle_ProjectionOfAGlobalIndexOfAnAttributeItDoesNotProject_AnswersValidation(string index, string projection, string names, string message)
    {
        // Arrange: DynamoDB Local 3.3.2 answers this for an attribute the index does not hold; the fake lists the attributes in the order of the expression.
        var fake = ReadTables.ShopWith(("a", 1));

        // Act
        var answer = FakeCalls.Error(
            fake,
            "Query",
            $$$$"""{"TableName":"Shop","IndexName":"{{{{index}}}}","KeyConditionExpression":"customer = :c",{{{{names}}}}"ExpressionAttributeValues":{":c":{"S":"k"}},"ProjectionExpression":"{{{{projection}}}}"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", "One or more parameter values were invalid: " + message));
    }

    [Test]
    public async Task Handle_ProjectionOfAGlobalIndexOfWhatItIncludes_AnswersIt()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 1));

        // Act
        var body = FakeCalls.Ok(fake, "Query", """{"TableName":"Shop","IndexName":"ByTotal","KeyConditionExpression":"customer = :c","ExpressionAttributeValues":{":c":{"S":"k"}},"ProjectionExpression":"v, pk"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"v":{"N":"1"},"pk":{"S":"a"}}]""", body.GetProperty("Items"))).IsTrue();
    }

    [Test]
    [Arguments("ByCustomer", "customer = :c", "v = :v AND attribute_exists(#s) AND sk = :v AND size(#t.x[0]) > :v", "\"ExpressionAttributeNames\":{\"#s\":\"status\",\"#t\":\"total\"},", "[status, total, v]")]
    [Arguments("ByTotal", "customer = :c", "v = :v OR #s = :c", "\"ExpressionAttributeNames\":{\"#s\":\"status\"},", "[status]")]
    [Arguments("ByStatus", "pk = :c", "v = :v", "", "[v]")]
    public async Task Handle_FilterOfAnIndexOnAnAttributeItDoesNotProject_AnswersValidation(string index, string keyCondition, string filter, string names, string attributes)
    {
        // Arrange: DynamoDB Local 3.3.2 answers this for a global and a local index alike, also with Select ALL_ATTRIBUTES, and lists the attributes in ordinal order.
        var fake = ReadTables.ShopWith(("k", 1));

        // Act
        var answer = FakeCalls.Error(
            fake,
            "Query",
            $$$$"""
            {"TableName":"Shop","IndexName":"{{{{index}}}}","KeyConditionExpression":"{{{{keyCondition}}}}","FilterExpression":"{{{{filter}}}}","Select":"ALL_ATTRIBUTES",
             {{{{names}}}}"ExpressionAttributeValues":{":c":{"S":"k"},":v":{"N":"1"}}}
            """);

        // Assert
        await Assert.That(answer).IsEqualTo(
            (HttpStatusCode.BadRequest, "ValidationException", $"One or more parameter values were invalid: Secondary index {index} does not project one or more filter attributes: {attributes}"));
    }

    [Test]
    public async Task Handle_FilterOfAGlobalIndexOnWhatItIncludes_AnswersTheMatches()
    {
        // Arrange
        var fake = ReadTables.ShopWith(("a", 3));

        // Act
        var body = FakeCalls.Ok(
            fake,
            "Query",
            """{"TableName":"Shop","IndexName":"ByTotal","KeyConditionExpression":"customer = :c","FilterExpression":"v = :v","ExpressionAttributeValues":{":c":{"S":"k"},":v":{"N":"2"}}}""");

        // Assert
        await Assert.That(string.Join(",", ReadTables.Keys(body))).IsEqualTo("a/2");
    }
}
