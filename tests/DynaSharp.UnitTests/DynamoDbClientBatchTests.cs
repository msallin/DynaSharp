using System.Text;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Telemetry;

namespace DynaSharp.UnitTests;

/// <summary>
/// <c>BatchGetItemAsync</c> and <c>BatchWriteItemAsync</c> end to end over a <see cref="StubHandler"/>: what goes over the wire,
/// what comes back, that the pooled buffers are returned, and what happens to unprocessed keys and items.
/// </summary>
public sealed class DynamoDbClientBatchTests
{
    private const string BatchGetResponseJson = """
        {"Responses":{"Users":[{"pk":{"S":"user-1"},"name":{"S":"Ada"}},{"pk":{"S":"user-2"},"name":{"S":"Grace"}}],"Orders":[{"pk":{"S":"order-1"}}]},
         "UnprocessedKeys":{"Users":{"Keys":[{"pk":{"S":"user-3"}},{"pk":{"S":"user-4"}}],"ConsistentRead":true,"ProjectionExpression":"#n","ExpressionAttributeNames":{"#n":"name"}}},
         "ConsumedCapacity":[{"TableName":"Users","CapacityUnits":2.0},{"TableName":"Orders","CapacityUnits":1.0}]}
        """;

    private const string BatchWriteResponseJson = """
        {"UnprocessedItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"user-3"},"name":{"S":"Edsger"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"user-4"}}}}]},
         "ItemCollectionMetrics":{"Orders":[{"ItemCollectionKey":{"pk":{"S":"order-1"}},"SizeEstimateRangeGB":[0.0,1.0]}]},
         "ConsumedCapacity":[{"TableName":"Users","CapacityUnits":2.0},{"TableName":"Orders","CapacityUnits":1.0}]}
        """;

    [Test]
    public async Task BatchGetItemAsync_Request_SendsTheBatchGetItemOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(BatchGetResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.BatchGetItemAsync(BatchGet());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("BatchGetItem", """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"user-1"}},{"pk":{"S":"user-2"}},{"pk":{"S":"user-3"}},{"pk":{"S":"user-4"}}]},"Orders":{"Keys":[{"pk":{"S":"order-1"}}]}}}"""));
    }

    [Test]
    public async Task BatchGetItemAsync_PartialResult_ReturnsItemsKeysAndCapacityPerTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(BatchGetResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That(JsonComparer.Differences(BatchGetResponseJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BatchGetItemAsync_UnprocessedKeys_AreSentAsTheyAreWhenPassedToTheNextCall()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(BatchGetResponseJson));
        handler.Enqueue(StubResponse.Json("""{"Responses":{"Users":[{"pk":{"S":"user-3"}},{"pk":{"S":"user-4"}}]},"UnprocessedKeys":{}}"""));
        using var client = TestClient.Create(handler);
        var first = await client.BatchGetItemAsync(BatchGet());

        // Act
        var second = await client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = first.UnprocessedKeys });

        // Assert
        await Assert.That((handler.Requests[1].BodyText, second.UnprocessedKeys.Count)).IsEqualTo((
            """{"RequestItems":{"Users":{"Keys":[{"pk":{"S":"user-3"}},{"pk":{"S":"user-4"}}],"ConsistentRead":true,"ProjectionExpression":"#n","ExpressionAttributeNames":{"#n":"name"}}}}""",
            0));
    }

    [Test]
    public async Task BatchGetItemAsync_NothingFound_ReturnsEmptyCollections()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{},"UnprocessedKeys":{}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That((response.Responses.Count, response.UnprocessedKeys.Count, response.ConsumedCapacity.Count)).IsEqualTo((0, 0, 0));
    }

    [Test]
    public async Task BatchGetItemAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(BatchGetResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task BatchGetItemAsync_ResponseWithMalformedItem_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Users":[{"pk":{"S":"a"}},{"pk":{"X":"b"}}]}}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.BatchGetItemAsync(BatchGet()));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    public async Task BatchGetItemAsync_LargeResponse_ReturnsEveryRentedArray()
    {
        // Arrange (a response far above the initial buffer: the buffer grows and every array it left behind goes back)
        var pool = new CountingArrayPool<byte>();
        var json = new StringBuilder("""{"Responses":{"Users":[""");
        for (var i = 0; i < 2000; i++)
        {
            json.Append(i == 0 ? string.Empty : ",").Append("""{"pk":{"S":"user-1"},"payload":{"S":"0123456789012345678901234567890123456789"}}""");
        }

        json.Append("""]}}""");
        using var handler = new StubHandler(_ => StubResponse.Json(json.ToString()));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var response = await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That((response.Responses["Users"].Count, pool.Outstanding)).IsEqualTo((2000, 0));
    }

    [Test]
    public async Task BatchGetItemAsync_UnknownReturnConsumedCapacity_ThrowsAndSendsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(BatchGetResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.BatchGetItemAsync(BatchGet() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 }));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnConsumedCapacity", StringComparison.Ordinal), handler.Requests.Count, pool.Outstanding)).IsEqualTo((true, 0, 0));
    }

    [Test]
    public async Task BatchGetItemAsync_UnknownEnumWithAListener_StopsTheActivityBeforeThrowing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(BatchGetResponseJson));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.BatchGetItemAsync(BatchGet() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 }));

        // Assert
        await Assert.That(capture.Single().OperationName).IsEqualTo("DynamoDB.BatchGetItem");
    }

    [Test]
    public async Task BatchGetItemAsync_ProvisionedThroughputExceeded_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "The level of configured provisioned throughput for the table was exceeded."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That(act).ThrowsExactly<ProvisionedThroughputExceededException>();
    }

    [Test]
    public async Task BatchGetItemAsync_MoreThanTheAllowedKeys_ThrowsValidationExceptionFromDynamoDb()
    {
        // Arrange (the library does not count: DynamoDB rejects a request of more than 100 keys)
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Too many items requested for the BatchGetItem call."));
        using var client = TestClient.Create(handler);
        var request = new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes> { ["Users"] = new() { Keys = [.. Enumerable.Range(0, 101).Select(OperationVectors.NumberedKey)] } },
        };

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.BatchGetItemAsync(request));

        // Assert
        await Assert.That((exception.Message, handler.Requests.Count)).IsEqualTo(("Too many items requested for the BatchGetItem call.", 1));
    }

    [Test]
    public async Task BatchWriteItemAsync_Request_SendsTheBatchWriteItemOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(BatchWriteResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("BatchWriteItem", """{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"user-3"},"name":{"S":"Edsger"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"user-4"}}}}],"Orders":[{"PutRequest":{"Item":{"pk":{"S":"order-1"}}}}]},"ReturnItemCollectionMetrics":"SIZE"}"""));
    }

    [Test]
    public async Task BatchWriteItemAsync_PartialResult_ReturnsUnprocessedItemsMetricsAndCapacity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(BatchWriteResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(JsonComparer.Differences(BatchWriteResponseJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BatchWriteItemAsync_UnprocessedItems_AreSentAsTheyAreWhenPassedToTheNextCall()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(BatchWriteResponseJson));
        handler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{}}"""));
        using var client = TestClient.Create(handler);
        var first = await client.BatchWriteItemAsync(BatchWrite());

        // Act
        var second = await client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = first.UnprocessedItems });

        // Assert
        await Assert.That((handler.Requests[1].BodyText, second.UnprocessedItems.Count)).IsEqualTo((
            """{"RequestItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"user-3"},"name":{"S":"Edsger"}}}},{"DeleteRequest":{"Key":{"pk":{"S":"user-4"}}}}]}}""",
            0));
    }

    [Test]
    public async Task BatchWriteItemAsync_EverythingProcessed_ReturnsEmptyCollections()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That((response.UnprocessedItems.Count, response.ItemCollectionMetrics.Count, response.ConsumedCapacity.Count)).IsEqualTo((0, 0, 0));
    }

    [Test]
    public async Task BatchWriteItemAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(BatchWriteResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task BatchWriteItemAsync_ResponseWithMalformedItem_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"UnprocessedItems":{"Users":[{"PutRequest":{"Item":{"pk":{"X":"b"}}}}]}}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.BatchWriteItemAsync(BatchWrite()));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    public async Task BatchWriteItemAsync_UnknownReturnItemCollectionMetrics_ThrowsAndSendsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.BatchWriteItemAsync(BatchWrite() with { ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 }));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal), handler.Requests.Count, pool.Outstanding)).IsEqualTo((true, 0, 0));
    }

    [Test]
    public async Task BatchWriteItemAsync_UnknownReturnConsumedCapacity_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.BatchWriteItemAsync(BatchWrite() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 }));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnConsumedCapacity", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task BatchWriteItemAsync_ItemCollectionTooLarge_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ItemCollectionSizeLimitExceededException", "Collection size exceeded"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<ItemCollectionSizeLimitExceededException>();
    }

    [Test]
    public async Task BatchWriteItemAsync_Throttled_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ThrottlingException", "Rate of requests exceeds the allowed throughput."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<ThrottlingException>();
    }

    [Test]
    public async Task BatchGetItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.BatchGetItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task BatchWriteItemAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.BatchWriteItemAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task BatchGetItemAsync_DisposedClient_ThrowsObjectDisposedException()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = async () => await client.BatchGetItemAsync(BatchGet());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task BatchWriteItemAsync_DisposedClient_ThrowsObjectDisposedException()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = async () => await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    private static BatchGetItemRequest BatchGet() => new()
    {
        RequestItems = new Dictionary<string, KeysAndAttributes>
        {
            ["Users"] = new()
            {
                Keys =
                [
                    new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
                    new Dictionary<string, AttributeValue> { ["pk"] = "user-2" },
                    new Dictionary<string, AttributeValue> { ["pk"] = "user-3" },
                    new Dictionary<string, AttributeValue> { ["pk"] = "user-4" },
                ],
            },
            ["Orders"] = new() { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "order-1" }] },
        },
    };

    private static BatchWriteItemRequest BatchWrite() => new()
    {
        RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
        {
            ["Users"] =
            [
                new WriteRequest { PutRequest = new PutRequest { Item = new Dictionary<string, AttributeValue> { ["pk"] = "user-3", ["name"] = "Edsger" } } },
                new WriteRequest { DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-4" } } },
            ],
            ["Orders"] = [new WriteRequest { PutRequest = new PutRequest { Item = new Dictionary<string, AttributeValue> { ["pk"] = "order-1" } } }],
        },
        ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
    };
}
