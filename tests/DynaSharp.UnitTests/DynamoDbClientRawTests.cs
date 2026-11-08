using System.Text;
using System.Text.RegularExpressions;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests;

/// <summary>
/// <see cref="DynamoDbClient.SendRawAsync"/>: any operation by name, JSON in and JSON out, through the pipeline of every other call.
/// DescribeLimits and DescribeEndpoints are the operations of the service that the library has no request type for.
/// </summary>
public sealed partial class DynamoDbClientRawTests
{
    private const string DescribeLimitsResponse = """{"AccountMaxReadCapacityUnits":80000,"AccountMaxWriteCapacityUnits":80000,"TableMaxReadCapacityUnits":40000,"TableMaxWriteCapacityUnits":40000}""";

    [Test]
    public async Task SendRawAsync_DescribeLimits_ReturnsTheResponseAsDynamoDbSentIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(DescribeLimitsResponse));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(Encoding.UTF8.GetString(response)).IsEqualTo(DescribeLimitsResponse);
    }

    [Test]
    public async Task SendRawAsync_ConditionFails_HasNoKey()
    {
        // Arrange (the JSON of the caller is not parsed, even if it holds a key)
        using var handler = new StubHandler(static _ => StubResponse.ConditionalCheckFailed());
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.SendRawAsync("DeleteItem", """{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}"""u8.ToArray()));

        // Assert
        await Assert.That(failure.Key).IsNull();
    }

    [Test]
    public async Task SendRawAsync_DescribeEndpoints_ReturnsTheEndpoints()
    {
        // Arrange
        const string Endpoints = """{"Endpoints":[{"Address":"dynamodb.eu-central-2.amazonaws.com","CachePeriodInMinutes":1440}]}""";
        using var handler = new StubHandler(_ => StubResponse.Json(Endpoints));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.SendRawAsync("DescribeEndpoints", "{}"u8.ToArray());

        // Assert
        await Assert.That(Encoding.UTF8.GetString(response)).IsEqualTo(Endpoints);
    }

    [Test]
    public async Task SendRawAsync_Request_SendsTheJsonUnchangedAsTheBody()
    {
        // Arrange
        const string Json = """{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ConsistentRead":true,"SomeParameterOfTheFuture":[1, 2,  3]}""";
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("GetItem", Encoding.UTF8.GetBytes(Json));

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(Json);
    }

    [Test]
    public async Task SendRawAsync_Request_SendsTheOperationInTheTargetHeader()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(DescribeLimitsResponse));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDB_20120810.DescribeLimits");
    }

    [Test]
    public async Task SendRawAsync_Request_SendsTheDynamoDbJsonContentType()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(DescribeLimitsResponse));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Content-Type"]).IsEqualTo("application/x-amz-json-1.0");
    }

    [Test]
    public async Task SendRawAsync_Request_IsSignedForTheOperationAndTheBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(DescribeLimitsResponse));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(SignatureShape().IsMatch(handler.Requests.Single().Headers["Authorization"])).IsTrue();
    }

    [Test]
    public async Task SendRawAsync_SameBodyOnTwoOperations_SignsThemDifferently()
    {
        // Arrange (the target header is part of what the signature covers)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());
        await client.SendRawAsync("DescribeEndpoints", "{}"u8.ToArray());

        // Act
        var signatures = handler.Requests.Select(request => request.Headers["Authorization"]).Distinct().Count();

        // Assert
        await Assert.That(signatures).IsEqualTo(2);
    }

    [Test]
    public async Task SendRawAsync_BodyLargerThanTheInitialBuffer_SendsEveryByte()
    {
        // Arrange
        var json = "{\"Item\":\"" + new string('x', 50_000) + "\"}";
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("PutItem", Encoding.UTF8.GetBytes(json));

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(json);
    }

    [Test]
    public async Task SendRawAsync_SliceOfALargerArray_SendsOnlyTheSlice()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes("garbage{}garbage");
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("DescribeLimits", bytes.AsMemory(7, 2));

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo("{}");
    }

    [Test]
    public async Task SendRawAsync_EmptyRequest_SendsAnEmptyBodyAndLeavesTheComplaintToDynamoDb()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync("DescribeLimits", default);

        // Assert
        await Assert.That(handler.Requests.Single().Body.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SendRawAsync_SuccessResponseThatIsNotJson_ReturnsItUnchanged()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("not json at all"));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(Encoding.UTF8.GetString(response)).IsEqualTo("not json at all");
    }

    [Test]
    public async Task SendRawAsync_ResponseLargerThanTheInitialBuffer_ReturnsEveryByte()
    {
        // Arrange
        var json = "{\"Payload\":\"" + new string('y', 50_000) + "\"}";
        using var handler = new StubHandler(_ => StubResponse.Json(json));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(Encoding.UTF8.GetString(response)).IsEqualTo(json);
    }

    [Test]
    public async Task SendRawAsync_ErrorResponse_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found."));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.SendRawAsync("DescribeTable", """{"TableName":"Missing"}"""u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task SendRawAsync_ErrorResponse_ExceptionNamesTheOperationAndNoTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("AccessDeniedException", "Not allowed."));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<AccessDeniedException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Assert
        await Assert.That((exception.OperationName, exception.TableName)).IsEqualTo(("DescribeLimits", null));
    }

    [Test]
    public async Task SendRawAsync_ProxyAnswersWithHtml_ThrowsUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => ProxyUnavailable());
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Assert
        await Assert.That(exception.OperationName).IsEqualTo("DescribeLimits");
    }

    [Test]
    public async Task SendRawAsync_ThrottledOnce_SendsAgainAndReturnsTheResponse()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json(DescribeLimitsResponse));
        using var client = CreateClient(handler);

        // Act
        var response = await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That((handler.Requests.Count, Encoding.UTF8.GetString(response))).IsEqualTo((2, DescribeLimitsResponse));
    }

    [Test]
    public async Task SendRawAsync_RetryAfterAServerError_SendsTheSameBody()
    {
        // Arrange
        const string Json = """{"TableName":"Users","SomeParameterOfTheFuture":true}""";
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);

        // Act
        await client.SendRawAsync("DescribeTable", Encoding.UTF8.GetBytes(Json));

        // Assert
        await Assert.That(handler.Requests.Select(request => request.BodyText).ToArray()).IsEquivalentTo([Json, Json]);
    }

    [Test]
    public async Task SendRawAsync_ThrottledOnEveryAttempt_ThrowsTheThrottlingError()
    {
        // Arrange
        using var handler = new StubHandler(_ => Throttled());
        using var client = CreateClient(handler, maxAttempts: 2);

        // Act
        var act = async () => await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ProvisionedThroughputExceededException>();
    }

    [Test]
    public async Task SendRawAsync_CallerCancels_ThrowsOperationCanceledExceptionWithTheCallersToken()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray(), cancellation.Token));

        // Assert
        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task SendRawAsync_AttemptTakesLongerThanTheTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json("{}");
        });
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());
        await arrived.Task;

        // Act
        time.Advance(TimeSpan.FromSeconds(30));

        // Assert
        await Assert.That(async () => await call).ThrowsExactly<TimeoutException>();
    }

    [Test]
    public async Task SendRawAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(DescribeLimitsResponse));
        using var client = TestClient.Create(handler, pool: pool);
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SendRawAsync_ErrorResponse_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Bad."));
        using var client = TestClient.Create(handler, pool: pool);
        await TestClient.CaptureAsync<ValidationException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SendRawAsync_DisposedClient_ThrowsObjectDisposedExceptionNamingTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var exception = await TestClient.CaptureAsync<ObjectDisposedException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Assert
        await Assert.That(exception.ObjectName).IsEqualTo(typeof(DynamoDbClient).FullName);
    }

    [Test]
    public async Task SendRawAsync_OperationNull_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        Func<Task> act = () => client.SendRawAsync(null!, "{}"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("operation");
    }

    [Test]
    public async Task SendRawAsync_OperationEmpty_ThrowsArgumentExceptionAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        Func<Task> act = () => client.SendRawAsync(string.Empty, "{}"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("operation");
    }

    [Test]
    [Arguments("Describe Limits")]
    [Arguments("Describe-Limits")]
    [Arguments("DescribeLimits.")]
    [Arguments("DynamoDB_20120810.DescribeLimits")]
    [Arguments("Describe\r\nLimits")]
    [Arguments("DescribeLimits\n")]
    [Arguments("Describe/Limits")]
    [Arguments("Büro")]
    public async Task SendRawAsync_OperationWithACharacterOtherThanLettersAndDigits_ThrowsArgumentExceptionAndSendsNothing(string operation)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        Func<Task> act = () => client.SendRawAsync(operation, "{}"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessage("The operation name must consist of ASCII letters and digits, for example 'DescribeLimits'. (Parameter 'operation')", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("A")]
    [Arguments("z")]
    [Arguments("Op9")]
    [Arguments("DescribeLimits")]
    public async Task SendRawAsync_OperationOfLettersAndDigits_IsSent(string operation)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.SendRawAsync(operation, "{}"u8.ToArray());

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo(operation);
    }

    [GeneratedRegex("^AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/[0-9]{8}/eu-central-2/dynamodb/aws4_request, SignedHeaders=content-type;host;x-amz-date;x-amz-target, Signature=[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SignatureShape();
}
