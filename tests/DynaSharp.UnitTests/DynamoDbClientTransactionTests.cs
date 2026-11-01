using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests;

/// <summary>
/// <c>TransactGetItemsAsync</c> and <c>TransactWriteItemsAsync</c> end to end over a <see cref="StubHandler"/>: what goes over the
/// wire, what comes back, the cancellation reasons of a failed transaction, and that the pooled buffers are returned.
/// </summary>
public sealed class DynamoDbClientTransactionTests
{
    private const string Token = "3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54";

    private const string TransactGetResponseJson = """
        {"Responses":[{"Item":{"pk":{"S":"user-1"},"name":{"S":"Ada"}}},{},{"Item":{"pk":{"S":"order-1"}}}],
         "ConsumedCapacity":[{"TableName":"Users","CapacityUnits":4.0},{"TableName":"Orders","CapacityUnits":2.0}]}
        """;

    private const string TransactWriteResponseJson = """
        {"ConsumedCapacity":[{"TableName":"Users","CapacityUnits":2.0},{"TableName":"Orders","CapacityUnits":2.0}],
         "ItemCollectionMetrics":{"Orders":[{"ItemCollectionKey":{"pk":{"S":"order-1"}},"SizeEstimateRangeGB":[0.0,1.0]}]}}
        """;

    // A real response: the first action failed its condition, the second had no problem.
    private const string TransactionCanceledJson = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed"},{"Code":"None"}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]"}""";

    [Test]
    public async Task TransactGetItemsAsync_Request_SendsTheTransactGetItemsOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactGetResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.TransactGetItemsAsync(TransactGet());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("TransactGetItems", """{"TransactItems":[{"Get":{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}},{"Get":{"TableName":"Users","Key":{"pk":{"S":"user-2"}}}},{"Get":{"TableName":"Orders","Key":{"pk":{"S":"order-1"}},"ProjectionExpression":"pk"}}],"ReturnConsumedCapacity":"TOTAL"}"""));
    }

    [Test]
    public async Task TransactGetItemsAsync_ItemsAndAMissingItem_ReturnsOneResponsePerActionInOrder()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactGetResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.TransactGetItemsAsync(TransactGet());

        // Assert
        await Assert.That(JsonComparer.Differences(TransactGetResponseJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TransactGetItemsAsync_MissingItem_HasNoItemAtItsPosition()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactGetResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.TransactGetItemsAsync(TransactGet());

        // Assert
        await Assert.That((response.Responses[0].Item is not null, response.Responses[1].Item is null, response.Responses[2].Item is not null)).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task TransactGetItemsAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TransactGetResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.TransactGetItemsAsync(TransactGet());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task TransactGetItemsAsync_ResponseWithMalformedItem_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{"Item":{"pk":{"X":"a"}}}]}"""));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.TransactGetItemsAsync(TransactGet()));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    public async Task TransactGetItemsAsync_UnknownReturnConsumedCapacity_ThrowsAndSendsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TransactGetResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.TransactGetItemsAsync(TransactGet() with { ReturnConsumedCapacity = (ReturnConsumedCapacity)99 }));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnConsumedCapacity", StringComparison.Ordinal), handler.Requests.Count, pool.Outstanding)).IsEqualTo((true, 0, 0));
    }

    [Test]
    public async Task TransactGetItemsAsync_ConflictWithAWrite_ThrowsTransactionCanceledExceptionWithTheReasons()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"None"},{"Code":"TransactionConflict","Message":"Transaction is ongoing for the item"},{"Code":"None"}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [None, TransactionConflict, None]"}""",
            System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactGetItemsAsync(TransactGet()));

        // Assert
        await Assert.That(string.Join(',', exception.CancellationReasons.Select(reason => reason.Code))).IsEqualTo("None,TransactionConflict,None");
    }

    [Test]
    public async Task TransactWriteItemsAsync_Request_SendsTheTransactWriteItemsOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactWriteResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("TransactWriteItems", """{"TransactItems":[{"Put":{"TableName":"Orders","Item":{"pk":{"S":"order-1"}},"ConditionExpression":"attribute_not_exists(pk)"}},{"Update":{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"UpdateExpression":"ADD orders :one","ExpressionAttributeValues":{":one":{"N":"1"}}}}],"ReturnConsumedCapacity":"TOTAL","ReturnItemCollectionMetrics":"SIZE","ClientRequestToken":"3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54"}"""));
    }

    [Test]
    public async Task TransactWriteItemsAsync_Success_ReturnsCapacityAndMetricsPerTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactWriteResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That(JsonComparer.Differences(TransactWriteResponseJson, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TransactWriteItemsAsync_EmptyResponse_ReturnsEmptyCollections()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That((response.ConsumedCapacity.Count, response.ItemCollectionMetrics.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task TransactWriteItemsAsync_NoToken_SendsAGeneratedGuid()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.TransactWriteItemsAsync(TransactWrite() with { ClientRequestToken = null });

        // Assert
        await Assert.That(Guid.TryParseExact(TokenOf(handler.Requests.Single().BodyText), "D", out _)).IsTrue();
    }

    [Test]
    public async Task TransactWriteItemsAsync_NoTokenOnTwoCalls_SendsADifferentTokenForEachCall()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var request = TransactWrite() with { ClientRequestToken = null };

        // Act
        await client.TransactWriteItemsAsync(request);
        await client.TransactWriteItemsAsync(request);

        // Assert
        await Assert.That(TokenOf(handler.Requests[0].BodyText)).IsNotEqualTo(TokenOf(handler.Requests[1].BodyText));
    }

    [Test]
    public async Task TransactWriteItemsAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TransactWriteResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task TransactWriteItemsAsync_ConditionFailed_ThrowsTransactionCanceledExceptionWithTheReasonsInActionOrder()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactionCanceledJson, System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(TransactWrite()));

        // Assert
        await Assert.That(string.Join(',', exception.CancellationReasons.Select(reason => reason.Code))).IsEqualTo("ConditionalCheckFailed,None");
    }

    [Test]
    public async Task TransactWriteItemsAsync_ConditionFailed_ExceptionCarriesMessageRequestIdAndStatus()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TransactionCanceledJson, System.Net.HttpStatusCode.BadRequest, new Dictionary<string, string> { ["x-amzn-RequestId"] = "REQ-1" }));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(TransactWrite()));

        // Assert
        await Assert.That((exception.Message, exception.RequestId, exception.StatusCode, exception.ErrorCode)).IsEqualTo((
            "Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]",
            "REQ-1",
            System.Net.HttpStatusCode.BadRequest,
            "TransactionCanceledException"));
    }

    [Test]
    public async Task TransactWriteItemsAsync_ConditionFailedWithAllOld_ExceptionCarriesTheItemOfTheReason()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{"pk":{"S":"order-1"},"version":{"N":"3"}}}],"Message":"Transaction cancelled"}""",
            System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(TransactWrite()));

        // Assert
        await Assert.That(exception.CancellationReasons.Single().Item!["version"].AsNumber<int>()).IsEqualTo(3);
    }

    [Test]
    public async Task TransactWriteItemsAsync_CanceledTransaction_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TransactionCanceledJson, System.Net.HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(TransactWrite()));

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task TransactWriteItemsAsync_TokenUsedForAnotherRequest_ThrowsIdempotentParameterMismatchException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("IdempotentParameterMismatchException", "The request uses the same client token as a previous, but non-identical request."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<IdempotentParameterMismatchException>();
    }

    [Test]
    public async Task TransactWriteItemsAsync_TransactionInProgress_ThrowsTransactionInProgressException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("TransactionInProgressException", "The transaction with the given request token is already in progress."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<TransactionInProgressException>();
    }

    [Test]
    public async Task TransactWriteItemsAsync_UnknownReturnValuesOnConditionCheckFailure_ThrowsAndSendsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, pool: pool);
        var request = TransactWrite() with
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = "Orders", Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ReturnValuesOnConditionCheckFailure = (ReturnValuesOnConditionCheckFailure)99 } }],
        };

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.TransactWriteItemsAsync(request));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnValuesOnConditionCheckFailure", StringComparison.Ordinal), handler.Requests.Count, pool.Outstanding)).IsEqualTo((true, 0, 0));
    }

    [Test]
    public async Task TransactWriteItemsAsync_UnknownEnumWithAListener_StopsTheActivityBeforeThrowing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var capture = new ActivityCapture();
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.TransactWriteItemsAsync(TransactWrite() with { ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 }));

        // Assert
        await Assert.That(capture.Single().OperationName).IsEqualTo("DynamoDB.TransactWriteItems");
    }

    [Test]
    public async Task TransactWriteItemsAsync_UnknownReturnItemCollectionMetrics_ThrowsAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => client.TransactWriteItemsAsync(TransactWrite() with { ReturnItemCollectionMetrics = (ReturnItemCollectionMetrics)99 }));

        // Assert
        await Assert.That((exception.Message.Contains("Not a known ReturnItemCollectionMetrics", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task TransactGetItemsAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.TransactGetItemsAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task TransactWriteItemsAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.TransactWriteItemsAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task TransactGetItemsAsync_DisposedClient_ThrowsObjectDisposedException()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = async () => await client.TransactGetItemsAsync(TransactGet());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task TransactWriteItemsAsync_DisposedClient_ThrowsObjectDisposedException()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var act = async () => await client.TransactWriteItemsAsync(TransactWrite());

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    private static TransactGetItemsRequest TransactGet() => new()
    {
        TransactItems =
        [
            new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } },
            new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-2" } } },
            new TransactGetItem { Get = new TransactGet { TableName = "Orders", Key = new Dictionary<string, AttributeValue> { ["pk"] = "order-1" }, ProjectionExpression = "pk" } },
        ],
        ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
    };

    private static TransactWriteItemsRequest TransactWrite() => new()
    {
        TransactItems =
        [
            new TransactWriteItem
            {
                Put = new TransactPut
                {
                    TableName = "Orders",
                    Item = new Dictionary<string, AttributeValue> { ["pk"] = "order-1" },
                    ConditionExpression = "attribute_not_exists(pk)",
                },
            },
            new TransactWriteItem
            {
                Update = new TransactUpdate
                {
                    TableName = "Users",
                    Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
                    UpdateExpression = "ADD orders :one",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":one"] = AttributeValue.FromNumber(1) },
                },
            },
        ],
        ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        ClientRequestToken = Token,
    };
}
