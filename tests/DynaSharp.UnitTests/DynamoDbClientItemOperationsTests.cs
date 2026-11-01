using System.Text;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Telemetry;

namespace DynaSharp.UnitTests;

/// <summary>
/// <c>UpdateItemAsync</c>, <c>DeleteItemAsync</c>, <c>QueryAsync</c> and <c>ScanAsync</c> end to end over a <see cref="StubHandler"/>:
/// what goes over the wire, what comes back, and that the pooled buffers are returned.
/// </summary>
public sealed class DynamoDbClientItemOperationsTests
{
    private const string QueryPageJson = """
        {"Items":[{"pk":{"S":"user-1"},"sk":{"N":"1"},"name":{"S":"Ada"}},{"pk":{"S":"user-1"},"sk":{"N":"2"},"name":{"S":"Grace"}}],
         "Count":2,"ScannedCount":5,"LastEvaluatedKey":{"pk":{"S":"user-1"},"sk":{"N":"5"}}}
        """;

    public static IEnumerable<int> BoundaryLimits()
    {
        yield return 0;
        yield return int.MaxValue;
    }

    [Test]
    public async Task QueryAsync_Request_SendsTheQueryOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(Query());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("Query", """{"TableName":"Users","KeyConditionExpression":"pk = :pk","ExpressionAttributeValues":{":pk":{"S":"user-1"}}}"""));
    }

    [Test]
    public async Task QueryAsync_PageOfItems_ReturnsItemsCountsAndKey()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.QueryAsync(Query());

        // Assert
        await Assert.That(JsonComparer.Differences(QueryPageJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task QueryAsync_EmptyPageWithLastEvaluatedKey_ReturnsNoItemsAndTheKey()
    {
        // Arrange (the filter removed everything that was read; there may be more)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Count":0,"Items":[],"LastEvaluatedKey":{"pk":{"S":"user-1"},"sk":{"N":"100"}},"ScannedCount":100}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.QueryAsync(Query() with { FilterExpression = "age > :age" });

        // Assert
        await Assert.That((response.Items.Count, response.ScannedCount, response.LastEvaluatedKey!["sk"].AsNumber<int>())).IsEqualTo((0, 100, 100));
    }

    [Test]
    public async Task QueryAsync_LastPage_HasNoLastEvaluatedKey()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"user-1"}}],"Count":1,"ScannedCount":1}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.QueryAsync(Query());

        // Assert
        await Assert.That(response.LastEvaluatedKey).IsNull();
    }

    [Test]
    public async Task QueryAsync_ContinuationWithTheReturnedKey_SendsItAsExclusiveStartKey()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(QueryPageJson));
        handler.Enqueue(StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":0}"""));
        using var client = TestClient.Create(handler);
        var first = await client.QueryAsync(Query());

        // Act
        await client.QueryAsync(Query() with { ExclusiveStartKey = first.LastEvaluatedKey });

        // Assert
        await Assert.That(handler.Requests[1].BodyText).Contains(""","ExclusiveStartKey":{"pk":{"S":"user-1"},"sk":{"N":"5"}},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task QueryAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.QueryAsync(Query());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task QueryAsync_ResponseWithMalformedItem_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":[{"pk":{"S":"a"}},{"pk":{"X":"b"}}],"Count":2}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.QueryAsync(Query()));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    public async Task QueryAsync_LargeResponse_ReturnsEveryRentedArray()
    {
        // Arrange (a page far above the initial buffer: the buffer grows and every array it left behind goes back)
        var pool = new CountingArrayPool<byte>();
        var json = new StringBuilder("""{"Items":[""");
        for (var i = 0; i < 2000; i++)
        {
            json.Append(i == 0 ? string.Empty : ",").Append("""{"pk":{"S":"user-1"},"payload":{"S":"0123456789012345678901234567890123456789"}}""");
        }

        json.Append("""],"Count":2000,"ScannedCount":2000}""");
        using var handler = new StubHandler(_ => StubResponse.Json(json.ToString()));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var response = await client.QueryAsync(Query());

        // Assert
        await Assert.That((response.Items.Count, pool.Outstanding)).IsEqualTo((2000, 0));
    }

    [Test]
    [MethodDataSource(nameof(BoundaryLimits))]
    public async Task QueryAsync_BoundaryLimit_IsSentAndDynamoDbDecides(int limit)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.QueryAsync(Query() with { Limit = limit });

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).Contains($$""","Limit":{{limit}},""", StringComparison.Ordinal);
    }

    [Test]
    public async Task QueryAsync_LimitZeroRejectedByDynamoDb_ThrowsValidationException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "1 validation error detected: Value '0' at 'limit' failed to satisfy constraint: Member must have value greater than or equal to 1"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.QueryAsync(Query() with { Limit = 0 });

        // Assert
        await Assert.That(act).ThrowsExactly<ValidationException>();
    }

    [Test]
    public async Task QueryAsync_UnknownSelect_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.QueryAsync(Query() with { Select = (Select)99 });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known Select.", StringComparison.Ordinal);
    }

    [Test]
    public async Task QueryAsync_UnknownSelect_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.QueryAsync(Query() with { Select = (Select)99 }));

        // Assert
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task QueryAsync_UnknownEnumValue_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.QueryAsync(Query() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 }));

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task QueryAsync_UnknownSelectWithAListener_StopsTheActivityBeforeThrowing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.QueryAsync(Query() with { Select = (Select)99 }));

        // Assert
        await Assert.That(capture.Single().OperationName).IsEqualTo("DynamoDB.Query");
    }

    [Test]
    public async Task QueryAsync_ProvisionedThroughputExceeded_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "The level of configured provisioned throughput for the table was exceeded."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.QueryAsync(Query());

        // Assert
        await Assert.That(act).ThrowsExactly<ProvisionedThroughputExceededException>();
    }

    [Test]
    public async Task ScanAsync_Request_SendsTheScanOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.ScanAsync(new ScanRequest { TableName = "Users", Segment = 1, TotalSegments = 4, Limit = 50 });

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("Scan", """{"TableName":"Users","Limit":50,"TotalSegments":4,"Segment":1}"""));
    }

    [Test]
    public async Task ScanAsync_PageOfItems_ReturnsItemsCountsAndKey()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.ScanAsync(new ScanRequest { TableName = "Users" });

        // Assert
        await Assert.That(JsonComparer.Differences(QueryPageJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ScanAsync_ParallelSegments_ContinueEachWithItsOwnKey()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json(request.BodyText.Contains("\"Segment\":0", StringComparison.Ordinal)
            ? """{"Items":[],"Count":0,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"in-segment-0"}}}"""
            : """{"Items":[],"Count":0,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"in-segment-1"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var first = await client.ScanAsync(new ScanRequest { TableName = "Users", Segment = 0, TotalSegments = 2 });
        var second = await client.ScanAsync(new ScanRequest { TableName = "Users", Segment = 1, TotalSegments = 2 });

        // Assert
        await Assert.That((first.LastEvaluatedKey!["pk"].AsString(), second.LastEvaluatedKey!["pk"].AsString())).IsEqualTo(("in-segment-0", "in-segment-1"));
    }

    [Test]
    public async Task ScanAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.ScanAsync(new ScanRequest { TableName = "Users" });

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task ScanAsync_ResponseWithMalformedItems_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Items":"none"}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.ScanAsync(new ScanRequest { TableName = "Users" }));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    [MethodDataSource(nameof(BoundaryLimits))]
    public async Task ScanAsync_BoundaryLimit_IsSentAndDynamoDbDecides(int limit)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.ScanAsync(new ScanRequest { TableName = "Users", Limit = limit });

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo($$"""{"TableName":"Users","Limit":{{limit}}}""");
    }

    [Test]
    public async Task ScanAsync_UnknownSelect_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.ScanAsync(new ScanRequest { TableName = "Users", Select = (Select)99 });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithMessageContaining("Not a known Select.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ScanAsync_UnknownSelect_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(QueryPageJson));
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.ScanAsync(new ScanRequest { TableName = "Users", Select = (Select)99 }));

        // Assert
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task UpdateItemAsync_Request_SendsTheUpdateItemOperationAndReturnsTheAttributes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"visits":{"N":"8"}}}"""));
        using var client = TestClient.Create(handler);
        var request = new UpdateItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
            UpdateExpression = "ADD visits :one",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":one"] = AttributeValue.FromNumber(1) },
            ReturnValues = ReturnValue.UpdatedNew,
        };

        // Act
        var response = await client.UpdateItemAsync(request);

        // Assert
        await Assert.That((handler.Requests.Single().Operation, response.Attributes!["visits"].AsNumber<int>())).IsEqualTo(("UpdateItem", 8));
    }

    [Test]
    public async Task UpdateItemAsync_ConditionFails_ThrowsConditionalCheckFailedExceptionWithTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"S":"user-1"},"version":{"N":"4"}},"message":"The conditional request failed"}""",
            System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);
        var request = new UpdateItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
            UpdateExpression = "SET a = :a",
            ConditionExpression = "version = :v",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":a"] = "x", [":v"] = AttributeValue.FromNumber(3) },
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        };

        // Act
        var exception = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.UpdateItemAsync(request));

        // Assert
        await Assert.That(exception.Item!["version"].AsNumber<int>()).IsEqualTo(4);
    }

    [Test]
    public async Task UpdateItemAsync_ResponseWithMetrics_ReturnsThem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"user-1"}},"SizeEstimateRangeGB":[0.0,1.0]}}"""));
        using var client = TestClient.Create(handler);
        var request = new UpdateItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        };

        // Act
        var response = await client.UpdateItemAsync(request);

        // Assert
        await Assert.That(response.ItemCollectionMetrics!.SizeEstimateRangeGB).IsEquivalentTo([0.0, 1.0]);
    }

    [Test]
    public async Task DeleteItemAsync_Request_SendsTheDeleteItemOperationAndReturnsTheOldItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"user-1"},"name":{"S":"Ada"}}}"""));
        using var client = TestClient.Create(handler);
        var request = new DeleteItemRequest
        {
            TableName = "Users",
            Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
            ReturnValues = ReturnValue.AllOld,
        };

        // Act
        var response = await client.DeleteItemAsync(request);

        // Assert
        await Assert.That((handler.Requests.Single().Operation, response.Attributes!["name"].AsString())).IsEqualTo(("DeleteItem", "Ada"));
    }

    [Test]
    public async Task DeleteItemAsync_NothingToDelete_ReturnsNoAttributes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var request = new DeleteItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "missing" } };

        // Act
        var response = await client.DeleteItemAsync(request);

        // Assert
        await Assert.That(response.Attributes).IsNull();
    }

    [Test]
    public async Task DeleteItemAsync_UnknownReturnValues_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var request = new DeleteItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" }, ReturnValues = (ReturnValue)99 };

        // Act
        var act = async () => await client.DeleteItemAsync(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    private static QueryRequest Query() => new()
    {
        TableName = "Users",
        KeyConditionExpression = "pk = :pk",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = "user-1" },
    };
}
