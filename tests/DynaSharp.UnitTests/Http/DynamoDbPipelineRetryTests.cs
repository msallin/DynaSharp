using System.Diagnostics;
using System.Net;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

// The retry policy is internal and only used by the pipeline, so these tests reach it through the client. The policy itself is tested in
// DynaSharp.UnitTests.Retries; here the question is what the client does with it. Tests that are not about the wait use a jitter of zero,
// which makes every wait zero, so nothing in this class sleeps or needs a clock that moves.
public sealed class DynamoDbPipelineRetryTests
{
    [Test]
    public async Task ExecuteAsync_ThrottledOnce_ReturnsTheResponseOfTheRetry()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["name"].AsString()).IsEqualTo("Alice");
    }

    [Test]
    public async Task ExecuteAsync_ThrottledOnce_SendsTwoRequests()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments("ThrottlingException")]
    [Arguments("ProvisionedThroughputExceededException")]
    [Arguments("RequestLimitExceeded")]
    [Arguments("LimitExceededException")]
    [Arguments("TransactionInProgressException")]
    public async Task ExecuteAsync_ThrottlingError_SendsEveryAttemptThenThrowsTheError(string errorCode)
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError(errorCode, "Slow down.") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo((errorCode, 3));
    }

    [Test]
    [Arguments(500)]
    [Arguments(502)]
    [Arguments(503)]
    [Arguments(504)]
    public async Task ExecuteAsync_TransientStatusFromAProxy_SendsEveryAttemptThenThrowsUnrecognizedResponse(int statusCode)
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("<html><body>Error</body></html>", (HttpStatusCode)statusCode) };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode, handler.Requests.Count)).IsEqualTo(("UnrecognizedResponse", (HttpStatusCode)statusCode, 3));
    }

    [Test]
    public async Task ExecuteAsync_ProxyAnswers503Once_ReturnsTheResponseOfTheRetry()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ProxyUnavailable());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ExecuteAsync_InternalServerErrorOnce_ReturnsTheResponseOfTheRetry()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ExecuteAsync_DifferentErrorsPerAttempt_ThrowsTheErrorOfTheLastAttempt()
    {
        // Arrange (the error that surfaces is the one of the last attempt, with its own request id)
        using var handler = new StubHandler();
        handler.Enqueue(Throttled("request-1"));
        handler.Enqueue(ServerError("request-2"));
        handler.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Slow down.", requestId: "request-3"));
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ThrottlingException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo("request-3");
    }

    [Test]
    public async Task ExecuteAsync_NetworkFailureOnce_ReturnsTheResponseOfTheRetry()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? throw new HttpRequestException("Connection refused") : Success());
        using var client = CreateClient(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ExecuteAsync_NetworkFailureOnEveryAttempt_ThrowsHttpRequestExceptionAfterTheLastAttempt()
    {
        // Arrange
        var calls = 0;
        using var handler = new StubHandler(_ => throw new HttpRequestException($"Connection refused {Interlocked.Increment(ref calls)}"));
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("Connection refused 3");
    }

    [Test]
    public async Task ExecuteAsync_ConnectTimeoutOfTheHandlerOnEveryAttempt_ThrowsTimeoutExceptionAfterTheLastAttempt()
    {
        // Arrange (the connect timeout runs on the real clock: a connection that never comes up fails each attempt after 200 ms)
        var connects = 0;
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(200),
            ConnectCallback = async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref connects);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new UnreachableException();
            },
        };
        using var client = CreateClient(handler, maxAttempts: 2, serviceUrl: new Uri("http://dynamodb.test"));

        // Act
        await TestClient.CaptureAsync<TimeoutException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(connects).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_ResponseEndsBeforeTheBodyIsComplete_ReturnsTheResponseOfTheRetry()
    {
        // Arrange (the first response promises more bytes than it delivers)
        var calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            if (Interlocked.Increment(ref calls) == 1)
            {
                content.Headers.ContentLength = TestClient.GetItemResponseJson.Length + 10;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = CreateClient(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((response.Item!["pk"].AsString(), calls)).IsEqualTo(("user-1", 2));
    }

    [Test]
    public async Task ExecuteAsync_AttemptTimesOut_RetriesWithAFreshTimeout()
    {
        // Arrange (the first attempt hangs until its timeout; the second answers)
        var time = new ManualTimeProvider();
        var attempts = new AttemptCounter(2);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            attempts.Reached();
            if (attempts.Count == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Success();
        });
        using var client = CreateClient(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await attempts.WhenReachedAsync(1);

        // Act
        time.Advance(TimeSpan.FromSeconds(30));
        var response = await call;

        // Assert
        await Assert.That((response.Item!["pk"].AsString(), time.TimersCreated, time.ActiveTimers)).IsEqualTo(("user-1", 2, 0));
    }

    [Test]
    public async Task ExecuteAsync_EveryAttemptTimesOut_ThrowsTimeoutExceptionAfterTheLastAttempt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var attempts = new AttemptCounter(3);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            attempts.Reached();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success();
        });
        using var client = CreateClient(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await attempts.WhenReachedAsync(1);
        time.Advance(TimeSpan.FromSeconds(30));
        await attempts.WhenReachedAsync(2);
        time.Advance(TimeSpan.FromSeconds(30));
        await attempts.WhenReachedAsync(3);

        // Act
        time.Advance(TimeSpan.FromSeconds(30));
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => call);

        // Assert
        await Assert.That((exception.Message, attempts.Count)).IsEqualTo(("DynamoDB GetItem did not complete within the attempt timeout of 00:00:30.", 3));
    }

    [Test]
    [Arguments("ValidationException", 400)]
    [Arguments("ConditionalCheckFailedException", 400)]
    [Arguments("AccessDeniedException", 400)]
    [Arguments("AccessDeniedException", 403)]
    [Arguments("ResourceNotFoundException", 400)]
    [Arguments("UnrecognizedClientException", 400)]
    [Arguments("ItemCollectionSizeLimitExceededException", 400)]
    [Arguments("TransactionConflictException", 400)]
    [Arguments("TransactionCanceledException", 400)]
    [Arguments("IdempotentParameterMismatchException", 400)]
    [Arguments("SomeNewException", 400)]
    public async Task ExecuteAsync_ErrorThatRepeatingCannotFix_IsSentOnce(string errorCode, int statusCode)
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError(errorCode, "Not your day.", (HttpStatusCode)statusCode) };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo((errorCode, 1));
    }

    [Test]
    [Arguments(404)]
    [Arguments(429)]
    [Arguments(501)]
    public async Task ExecuteAsync_OtherStatusFromAProxy_IsSentOnce(int statusCode)
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("<html></html>", (HttpStatusCode)statusCode) };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("UnrecognizedResponse", 1));
    }

    [Test]
    public async Task ExecuteAsync_SuccessfulResponseThatCannotBeParsed_IsSentOnce()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("""{"Item":5}""") };
        using var client = CreateClient(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("UnrecognizedResponse", 1));
    }

    [Test]
    public async Task ExecuteAsync_CredentialsUnavailable_AsksOnceAndSendsNothing()
    {
        // Arrange
        var credentials = new MissingCredentialsProvider();
        using var handler = new StubHandler { DefaultResponse = Success() };
        using var client = CreateClient(handler, credentials: credentials);

        // Act
        await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((credentials.Count, handler.Requests.Count)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task ExecuteAsync_CredentialsProviderCancelsItself_ThrowsCredentialsUnavailableOnceAndSendsNothing()
    {
        // Arrange
        var credentials = new SelfCancellingCredentialsProvider();
        using var handler = new StubHandler { DefaultResponse = Success() };
        using var client = CreateClient(handler, credentials: credentials);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.InnerException is TaskCanceledException, credentials.Count, handler.Requests.Count)).IsEqualTo((true, 1, 0));
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsWhileAnAttemptIsPending_DoesNotRetry()
    {
        // Arrange
        var attempts = new AttemptCounter(2);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            attempts.Reached();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success();
        });
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await attempts.WhenReachedAsync(1);

        // Act
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_HandlerCancelsOnItsOwn_IsRetriedAsATimeout()
    {
        // Arrange (a cancellation that neither the caller nor the attempt timeout asked for is a timeout of the handler)
        using var handler = new StubHandler(_ => throw new OperationCanceledException("The handler gave up."));
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<TimeoutException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ExecuteAsync_RequestCannotBeSerialized_SendsNothingAndDoesNotRetry()
    {
        // Arrange (a default AttributeValue has no type)
        using var handler = new StubHandler { DefaultResponse = Success() };
        using var client = CreateClient(handler);
        var request = new PutItemRequest { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = default } };

        // Act
        await TestClient.CaptureAsync<ArgumentException>(() => client.PutItemAsync(request));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_MaxAttemptsIsOne_SendsOnceAndThrowsTheError()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: 1);

        // Act
        var exception = await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ProvisionedThroughputExceededException", 1));
    }

    [Test]
    public async Task ExecuteAsync_MaxAttemptsIsOneAndTheAttemptTimesOut_ThrowsTimeoutExceptionWithoutRetry()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var attempts = new AttemptCounter(2);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            attempts.Reached();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success();
        });
        using var client = CreateClient(handler, time, maxAttempts: 1, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await attempts.WhenReachedAsync(1);

        // Act
        time.Advance(TimeSpan.FromSeconds(30));
        await TestClient.CaptureAsync<TimeoutException>(() => call);

        // Assert
        await Assert.That(attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_MaxAttemptsIsTwo_SendsTwoRequests()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: 2);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_MaxAttemptsIsIntMaxValue_RetriesUntilTheCallSucceeds()
    {
        // Arrange
        using var handler = new StubHandler();
        for (var i = 0; i < 10; i++)
        {
            handler.Enqueue(Throttled());
        }

        handler.Enqueue(Success());
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((response.Item!["pk"].AsString(), handler.Requests.Count)).IsEqualTo(("user-1", 11));
    }

    [Test]
    public async Task ExecuteAsync_DefaultOptions_SendsFourAttempts()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        var options = new DynamoDbClientOptions
        {
            Region = TestClient.Region,
            Credentials = new StaticCredentialsProvider(TestClient.Credentials),
            HttpHandler = handler,
            TimeProvider = new ManualTimeProvider(),
        };
        using var client = new DynamoDbClient(options, static _ => null, null, null, FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(4);
    }

    [Test]
    public async Task ExecuteAsync_DefaultRandomSource_RetriesWithoutAnyInjection()
    {
        // Arrange (a wait of at most 100 ms on the clock of the test, which only moves when told, so the call stays pending)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        var options = new DynamoDbClientOptions
        {
            Region = TestClient.Region,
            Credentials = new StaticCredentialsProvider(TestClient.Credentials),
            HttpHandler = handler,
            TimeProvider = time,
        };
        using var client = new DynamoDbClient(options, static _ => null, null, null);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_RetryOfAWrite_SendsTheSameBody()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);

        // Act
        await client.PutItemAsync(TestClient.PutItem());

        // Assert
        var sent = handler.Requests;
        await Assert.That(sent[1].BodyText).IsEqualTo(sent[0].BodyText);
    }

    [Test]
    public async Task ExecuteAsync_Retry_SignsEveryAttemptWithTheTimeOfThatAttempt()
    {
        // Arrange (the clock moves two seconds while the first attempt is out)
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                time.Advance(TimeSpan.FromSeconds(2));
                return ServerError();
            }

            return Success();
        });
        using var client = CreateClient(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', handler.Requests.Select(request => request.Headers["X-Amz-Date"]))).IsEqualTo("20261031T123456Z,20261031T123458Z");
    }

    [Test]
    public async Task ExecuteAsync_Retry_SendsADifferentSignatureForTheNewTime()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                time.Advance(TimeSpan.FromSeconds(2));
                return ServerError();
            }

            return Success();
        });
        using var client = CreateClient(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests[1].Headers["Authorization"]).IsNotEqualTo(handler.Requests[0].Headers["Authorization"]);
    }

    [Test]
    public async Task ExecuteAsync_Retry_LooksUpTheCredentialsAgain()
    {
        // Arrange (credentials can rotate between attempts)
        var credentials = new CountingCredentialsProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, credentials: credentials);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(credentials.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_Retry_StartsANewTimeoutForEveryAttempt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((time.TimersCreated, time.ActiveTimers)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task ExecuteAsync_ThrottledWithMaximumJitter_WaitsFiftyMilliseconds()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("50");
    }

    [Test]
    public async Task ExecuteAsync_ServerErrorWithMaximumJitter_WaitsAHundredMilliseconds()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("100");
    }

    [Test]
    public async Task ExecuteAsync_NetworkFailureWithMaximumJitter_WaitsAHundredMilliseconds()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? throw new HttpRequestException("Connection reset") : Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("100");
    }

    [Test]
    public async Task ExecuteAsync_ThrottledTwiceWithMaximumJitter_DoublesTheWait()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("50,100");
    }

    [Test]
    public async Task ExecuteAsync_ManyRetriesWithMaximumJitter_WaitsGrowUntilTheCap()
    {
        // Arrange (eleven throttled attempts: the ceiling 50 ms * 2^(n - 1) passes 20 s at the tenth)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        for (var i = 0; i < 11; i++)
        {
            handler.Enqueue(Throttled());
        }

        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, maxAttempts: 12, random: FixedRandom.One);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("50,100,200,400,800,1600,3200,6400,12800,20000,20000");
    }

    [Test]
    public async Task ExecuteAsync_JitterOfZero_DoesNotWaitAtAll()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.Zero);

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());

        // Assert (finished before anyone moved the clock; the only timers were the two attempt timeouts)
        await Assert.That((call.IsCompletedSuccessfully, time.TimersCreated)).IsEqualTo((true, 2));
    }

    [Test]
    public async Task ExecuteAsync_JitterInTheMiddle_WaitsHalfTheCeiling()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: new FixedRandom(0.5));

        // Act
        var call = client.GetItemAsync(TestClient.GetItem());
        var delays = await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);

        // Assert
        await Assert.That(Milliseconds(delays)).IsEqualTo("25");
    }

    [Test]
    public async Task ExecuteAsync_WaitHasNotElapsed_DoesNotSendTheNextAttempt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        var call = client.GetItemAsync(TestClient.GetItem());

        // Act
        time.Advance(TimeSpan.FromMilliseconds(50) - TimeSpan.FromTicks(1));

        // Assert
        await Assert.That((call.IsCompleted, handler.Requests.Count)).IsEqualTo((false, 1));
    }

    [Test]
    public async Task ExecuteAsync_WaitElapses_SendsTheNextAttempt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        var call = client.GetItemAsync(TestClient.GetItem());
        time.Advance(TimeSpan.FromMilliseconds(50) - TimeSpan.FromTicks(1));

        // Act
        time.Advance(TimeSpan.FromTicks(1));
        await call;

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsDuringTheWait_ThrowsOperationCanceledExceptionWithTheCallersToken()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That((exception.GetType(), exception.CancellationToken)).IsEqualTo((typeof(OperationCanceledException), cancellation.Token));
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsDuringTheWait_EndsTheWaitAtOnceAndSendsNothingMore()
    {
        // Arrange (the clock never moves, so only the cancellation can end the wait)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That((handler.Requests.Count, time.ActiveTimers)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsDuringTheWait_StartsNoFurtherAttempt()
    {
        // Arrange (the clock never moves, so only the cancellation can end the wait; an attempt starts by asking for credentials)
        var time = new ManualTimeProvider();
        var credentials = new CountingCredentialsProvider();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, time, random: FixedRandom.One, credentials: credentials);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await cancellation.CancelAsync();

        // Act
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(credentials.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsDuringTheWait_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, random: FixedRandom.One, pool: pool);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_RetriedThenSucceeded_ReturnsEveryRentedArray()
    {
        // Arrange (the request body once, and one response buffer per attempt)
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, pool: pool);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 4));
    }

    [Test]
    public async Task ExecuteAsync_AttemptsExhausted_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, pool: pool);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var (outstanding, returned) = (pool.Outstanding, pool.Returned);

        // Assert
        await Assert.That((outstanding, returned)).IsEqualTo((0, 4));
    }

    [Test]
    public async Task ExecuteAsync_NetworkFailuresExhaustAttempts_ReturnsTheRequestBody()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused"));
        using var client = CreateClient(handler, pool: pool);
        await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var (outstanding, returned) = (pool.Outstanding, pool.Returned);

        // Assert
        await Assert.That((outstanding, returned)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task ExecuteAsync_Retry_KeepsTheRequestBodyRentedUntilTheLastAttempt()
    {
        // Arrange (when an attempt reaches the handler, the body is the only array out: earlier response buffers are back)
        var pool = new CountingArrayPool<byte>();
        var outstandingPerAttempt = new List<int>();
        using var handler = new StubHandler(_ =>
        {
            outstandingPerAttempt.Add(pool.Outstanding);
            return outstandingPerAttempt.Count < 3 ? ServerError() : Success();
        });
        using var client = CreateClient(handler, pool: pool);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(string.Join(',', outstandingPerAttempt)).IsEqualTo("1,1,1");
    }

    [Test]
    public async Task ExecuteAsync_QuotaRunsOut_StopsAfterTheTokensAreSpent()
    {
        // Arrange (500 tokens pay for 100 retries at 5 each)
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);

        // Act
        var exception = await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("ProvisionedThroughputExceededException", 101));
    }

    [Test]
    public async Task ExecuteAsync_QuotaIsEmpty_SendsLaterCallsOnce()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));
        var before = handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count - before).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_TimeoutsOnEveryAttempt_SpendTheQuotaTwiceAsFast()
    {
        // Arrange (a timeout costs 10 tokens: 500 pay for 50 retries)
        using var handler = new StubHandler(_ => throw new TimeoutException("The attempt timed out."));
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);

        // Act
        await TestClient.CaptureAsync<TimeoutException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(51);
    }

    [Test]
    public async Task ExecuteAsync_SuccessAfterRetries_GivesBackTheCostOfTheLastRetry()
    {
        // Arrange (two retries take 10 tokens, the success gives 5 back: 495 are left, which pay for 99 retries)
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        handler.Enqueue(Throttled());
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);
        await client.GetItemAsync(TestClient.GetItem());
        var before = handler.Requests.Count;

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count - before).IsEqualTo(100);
    }

    [Test]
    public async Task ExecuteAsync_ManyCallsFailAndRecover_KeepTheQuotaFromRunningOut()
    {
        // Arrange (each call fails once and succeeds on the retry: 5 tokens out, 5 back)
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: int.MaxValue);
        for (var i = 0; i < 200; i++)
        {
            handler.Enqueue(Throttled());
            handler.Enqueue(Success());
        }

        for (var i = 0; i < 200; i++)
        {
            await client.GetItemAsync(TestClient.GetItem());
        }

        // Act
        var before = handler.Requests.Count;
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(handler.Requests.Count - before).IsEqualTo(101);
    }

    private static string Milliseconds(IReadOnlyList<TimeSpan> delays) =>
        string.Join(',', delays.Select(delay => delay.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    // A provider whose own internal timeout fires: the caller cancelled nothing and the attempt timeout did not run out.
    private sealed class SelfCancellingCredentialsProvider : ICredentialsProvider
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            throw new TaskCanceledException("The credentials endpoint did not answer.");
        }
    }

    private sealed class MissingCredentialsProvider : ICredentialsProvider
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            throw new CredentialsUnavailableException("No credentials.");
        }
    }
}
