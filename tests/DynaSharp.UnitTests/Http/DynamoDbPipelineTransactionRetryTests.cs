using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Http;

// What the retries do with batches and transactions, and above all that the attempts of one TransactWriteItems call send one token:
// that is what makes a transaction whose response was lost safe to send again. Tests that are not about the wait use a jitter of zero.
public sealed class DynamoDbPipelineTransactionRetryTests
{
    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterANetworkError_SendsTheSameBodyOnEveryAttempt()
    {
        // Arrange (no token in the request: the library makes one for the call)
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) <= 2 ? throw new HttpRequestException("Connection reset") : TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterANetworkError_MakesThreeAttempts()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) <= 2 ? throw new HttpRequestException("Connection reset") : TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterANetworkError_AllAttemptsCarryTheOneGeneratedToken()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) <= 2 ? throw new HttpRequestException("Connection reset") : TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        var tokens = handler.Requests.Select(request => TokenOf(request.BodyText)).Distinct(StringComparer.Ordinal).ToArray();
        await Assert.That((tokens.Length, Guid.TryParseExact(tokens[0], "D", out _))).IsEqualTo((1, true));
    }

    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterATimeout_SendsTheSameBodyOnEveryAttempt()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? throw new TimeoutException("The attempt timed out.") : TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterAServerError_SendsTheSameBodyOnEveryAttempt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_RetriedAfterTransactionInProgress_SendsTheSameTokenAgain()
    {
        // Arrange (the answer of DynamoDB to a second request with a token whose first request is still running)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("TransactionInProgressException", "The transaction with the given request token is already in progress."));
        handler.Enqueue(TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write());

        // Assert
        await Assert.That(handler.Requests.Select(request => TokenOf(request.BodyText)).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_TokenGivenAndRetried_EveryAttemptCarriesIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(TransactionSucceeds());
        using var client = CreateClient(handler);

        // Act
        await client.TransactWriteItemsAsync(Write() with { ClientRequestToken = "my-token" });

        // Assert
        await Assert.That(handler.Requests.Select(request => TokenOf(request.BodyText)).ToArray()).IsEquivalentTo(["my-token", "my-token"]);
    }

    [Test]
    public async Task TransactWriteItemsAsync_TwoRetriedCalls_EachKeepsItsOwnToken()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(TransactionSucceeds());
        handler.Enqueue(ServerError());
        handler.Enqueue(TransactionSucceeds());
        using var client = CreateClient(handler);
        var request = Write();

        // Act
        await client.TransactWriteItemsAsync(request);
        await client.TransactWriteItemsAsync(request);

        // Assert
        var tokens = handler.Requests.Select(sent => TokenOf(sent.BodyText)).ToArray();
        await Assert.That((tokens[0] == tokens[1], tokens[2] == tokens[3], tokens[0] != tokens[2])).IsEqualTo((true, true, true));
    }

    [Test]
    public async Task TransactWriteItemsAsync_TransactionInProgressOnEveryAttempt_SendsEveryAttemptThenThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("TransactionInProgressException", "The transaction with the given request token is already in progress.") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionInProgressException>(() => client.TransactWriteItemsAsync(Write()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("TransactionInProgressException", 3));
    }

    [Test]
    public async Task BatchWriteItemAsync_ReplicatedWriteConflictOnce_ReturnsTheResponseOfTheRetry()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ReplicatedWriteConflictException", "Replicated write conflict"));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);

        // Act
        await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task BatchWriteItemAsync_ReplicatedWriteConflictOnEveryAttempt_SendsEveryAttemptThenThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ReplicatedWriteConflictException", "Replicated write conflict") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ReplicatedWriteConflictException>(() => client.BatchWriteItemAsync(BatchWrite()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ReplicatedWriteConflictException", 3));
    }

    [Test]
    public async Task BatchWriteItemAsync_RetriedAfterAServerError_SendsTheSameBodyOnEveryAttempt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);

        // Act
        await client.BatchWriteItemAsync(BatchWrite());

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task BatchGetItemAsync_ThrottledOnEveryAttempt_SendsEveryAttemptThenThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.BatchGetItemAsync(BatchGet()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ProvisionedThroughputExceededException", 3));
    }

    [Test]
    public async Task TransactWriteItemsAsync_Canceled_IsSentOnceAndThrowsWithTheReasons()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Canceled() };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(Write()));

        // Assert
        await Assert.That((exception.CancellationReasons.Count, handler.Requests.Count)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task TransactGetItemsAsync_Canceled_IsSentOnce()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Canceled() };
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactGetItemsAsync(Get()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_CanceledBecauseOfAConflict_IsNotRepeatedByTheClient()
    {
        // Arrange (the reason may pass, but whether to try again is for the caller to decide)
        using var handler = new StubHandler
        {
            DefaultResponse = StubResponse.Json(
                """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"TransactionConflict","Message":"Transaction is ongoing for the item"}],"Message":"Transaction cancelled"}""",
                System.Net.HttpStatusCode.BadRequest),
        };
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<TransactionCanceledException>(() => client.TransactWriteItemsAsync(Write()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TransactWriteItemsAsync_TokenUsedForAnotherRequest_IsSentOnce()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("IdempotentParameterMismatchException", "The request uses the same client token as a previous, but non-identical request.") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<IdempotentParameterMismatchException>(() => client.TransactWriteItemsAsync(Write()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("IdempotentParameterMismatchException", 1));
    }

    [Test]
    public async Task BatchWriteItemAsync_TransactionConflict_IsSentOnceAndThrowsTheTypedException()
    {
        // Arrange (an item of the batch is part of an ongoing transaction)
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<TransactionConflictException>(() => client.BatchWriteItemAsync(BatchWrite()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("TransactionConflictException", 1));
    }

    private static StubResponse TransactionSucceeds() => StubResponse.Json("{}");

    private static StubResponse Canceled() => StubResponse.Json(
        """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed"},{"Code":"None"}],"Message":"Transaction cancelled, please refer cancellation reasons for specific reasons [ConditionalCheckFailed, None]"}""",
        System.Net.HttpStatusCode.BadRequest);

    private static TransactWriteItemsRequest Write() => new()
    {
        TransactItems =
        [
            new TransactWriteItem { Put = new TransactPut { TableName = "Orders", Item = new Dictionary<string, AttributeValue> { ["pk"] = "order-1" } } },
            new TransactWriteItem { Delete = new TransactDelete { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } },
        ],
    };

    private static TransactGetItemsRequest Get() => new()
    {
        TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } }],
    };

    private static BatchWriteItemRequest BatchWrite() => new()
    {
        RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>
        {
            ["Users"] = [new WriteRequest { PutRequest = new PutRequest { Item = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" } } }],
        },
    };

    private static BatchGetItemRequest BatchGet() => new()
    {
        RequestItems = new Dictionary<string, KeysAndAttributes>
        {
            ["Users"] = new() { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "user-1" }] },
        },
    };
}
