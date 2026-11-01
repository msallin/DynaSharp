using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Signing;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using DynaSharp.UnitTests.Signing;

namespace DynaSharp.UnitTests.Http;

// The pipeline is internal and only used by DynamoDbClient, so the tests reach it through the client.
public sealed class DynamoDbPipelineTests
{
    private const string Endpoint = "https://dynamodb.eu-central-2.amazonaws.com/";
    private const string RequestId = "7QTD4FR8SJ3T2JKB5LCUTH4MJVVV4KQNSO5AEMVJF66Q9ASUAAJG";

    private const string MusicTable = """
        {"Table":{"AttributeDefinitions":[{"AttributeName":"Artist","AttributeType":"S"}],"TableName":"Music","KeySchema":[{"AttributeName":"Artist","KeyType":"HASH"}],"TableStatus":"ACTIVE","CreationDateTime":1.421866952062E9,"ItemCount":0,"TableSizeBytes":0}}
        """;

    [Test]
    public async Task ExecuteAsync_GetItem_PostsToTheEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var request = handler.Requests.Single();
        await Assert.That($"{request.Method} {request.RequestUri}").IsEqualTo("POST " + Endpoint);
    }

    [Test]
    public async Task ExecuteAsync_GetItem_SendsTheOperationInTheTargetHeader()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDB_20120810.GetItem");
    }

    [Test]
    public async Task ExecuteAsync_PutItem_SendsTheOperationInTheTargetHeader()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.PutItemAsync(TestClient.PutItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDB_20120810.PutItem");
    }

    [Test]
    public async Task ExecuteAsync_DescribeTable_SendsTheOperationInTheTargetHeader()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(MusicTable));
        using var client = TestClient.Create(handler);

        // Act
        await client.DescribeTableAsync(TestClient.DescribeTable("Music"));

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDB_20120810.DescribeTable");
    }

    [Test]
    public async Task ExecuteAsync_Request_SendsTheDynamoDbJsonContentType()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Content-Type"]).IsEqualTo("application/x-amz-json-1.0");
    }

    [Test]
    public async Task ExecuteAsync_GetItem_SendsTheRequestJsonAsBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo("""{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}""");
    }

    [Test]
    public async Task ExecuteAsync_Request_SendsTheContentLengthOfTheBodyOnly()
    {
        // Arrange (the pooled array is larger than the body)
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var request = handler.Requests.Single();
        await Assert.That(request.Headers["Content-Length"]).IsEqualTo(request.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task ExecuteAsync_Request_SignsForTheRegionAndServiceOnTheClockDate()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["Authorization"]).StartsWith(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20261031/eu-central-2/dynamodb/aws4_request, SignedHeaders=content-type;host;x-amz-date;x-amz-target, Signature=");
    }

    [Test]
    public async Task ExecuteAsync_Request_SendsTheAmzDateOfTheClock()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Date"]).IsEqualTo("20261031T123456Z");
    }

    [Test]
    public async Task ExecuteAsync_Request_IdentifiesTheClient()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["User-Agent"]).IsEqualTo("DynaSharp");
    }

    [Test]
    public async Task ExecuteAsync_Request_SignatureCoversTheSha256OfTheBodySent()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var time = new ManualTimeProvider();
        using var client = TestClient.Create(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That(sent.Headers["Authorization"]).IsEqualTo(Sign(sent, new Uri(Endpoint), TestClient.Credentials, time));
    }

    [Test]
    public async Task ExecuteAsync_EndpointWithPort_SignatureCoversTheHostWithPort()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var time = new ManualTimeProvider();
        var endpoint = new Uri("http://localhost:8000");
        using var client = TestClient.Create(handler, time, serviceUrl: endpoint);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That(sent.Headers["Authorization"]).IsEqualTo(Sign(sent, endpoint, TestClient.Credentials, time));
    }

    [Test]
    public async Task ExecuteAsync_EndpointWithPort_SendsToThatEndpoint()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://localhost:8000"));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().RequestUri).IsEqualTo(new Uri("http://localhost:8000/"));
    }

    [Test]
    public async Task ExecuteAsync_SessionTokenCredentials_SendsTheSecurityToken()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret", "FwoGZXIvYXdzEJr-token"));
        using var client = TestClient.Create(handler, credentials: credentials);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Security-Token"]).IsEqualTo("FwoGZXIvYXdzEJr-token");
    }

    [Test]
    public async Task ExecuteAsync_LongTermCredentials_SendsNoSecurityToken()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().Headers.ContainsKey("X-Amz-Security-Token")).IsFalse();
    }

    [Test]
    public async Task ExecuteAsync_GetItemResponse_ReturnsTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["name"].AsString()).IsEqualTo("Alice");
    }

    [Test]
    public async Task ExecuteAsync_PutItemResponse_ReturnsTheReplacedAttributes()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Attributes":{"pk":{"S":"user-1"},"name":{"S":"Alicia"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.PutItemAsync(TestClient.PutItem());

        // Assert
        await Assert.That(response.Attributes!["name"].AsString()).IsEqualTo("Alicia");
    }

    [Test]
    public async Task ExecuteAsync_DescribeTableResponse_ReturnsTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(MusicTable));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.DescribeTableAsync(TestClient.DescribeTable("Music"));

        // Assert
        await Assert.That((response.Table.TableName, response.Table.TableStatus)).IsEqualTo(("Music", TableStatus.Active));
    }

    [Test]
    public async Task ExecuteAsync_ResourceNotFound_ThrowsTheTypedException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","message":"Requested resource not found"}""",
            HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>().WithMessage("Requested resource not found", StringComparison.Ordinal);
    }

    [Test]
    public async Task ExecuteAsync_ErrorWithRequestIdHeader_ExceptionCarriesTheRequestId()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad", requestId: RequestId));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo(RequestId);
    }

    [Test]
    public async Task ExecuteAsync_ErrorWithoutRequestIdHeader_RequestIdIsNull()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.RequestId).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_ConditionalCheckFailedWithItem_ExceptionCarriesTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{"pk":{"S":"user-1"}},"message":"The conditional request failed"}""",
            HttpStatusCode.BadRequest));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => client.PutItemAsync(TestClient.PutItem()));

        // Assert
        await Assert.That(exception.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    public async Task ExecuteAsync_UnknownErrorCode_ThrowsDynamoDbExceptionWithTheCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("SomethingNewException", "A code from a later version of the service"));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.GetType(), exception.ErrorCode)).IsEqualTo((typeof(DynamoDbException), "SomethingNewException"));
    }

    [Test]
    public async Task ExecuteAsync_ProxyAnswers503WithHtml_ThrowsUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html><body>503 Service Unavailable</body></html>", HttpStatusCode.ServiceUnavailable));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode)).IsEqualTo(("UnrecognizedResponse", HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task ExecuteAsync_ErrorWithEmptyBody_ThrowsUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(string.Empty, HttpStatusCode.BadGateway));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode)).IsEqualTo(("UnrecognizedResponse", HttpStatusCode.BadGateway));
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithBodyThatIsNotAGetItemResponse_ThrowsUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":5}"""));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode)).IsEqualTo(("UnrecognizedResponse", HttpStatusCode.OK));
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithBodyThatIsNotAGetItemResponse_InnerExceptionIsTheJsonException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Item":5}"""));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.InnerException).IsTypeOf<JsonException>();
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithEmptyBody_ThrowsUnrecognizedResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(string.Empty));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithWrongValueKind_ThrowsUnrecognizedResponse()
    {
        // Arrange (CapacityUnits is a string, not a number)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ConsumedCapacity":{"CapacityUnits":"many"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.InnerException).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteAsync_SuccessWithNumberInWrongFormat_ThrowsUnrecognizedResponse()
    {
        // Arrange (ItemCount is not an integer)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Music","ItemCount":1.5}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.DescribeTableAsync(TestClient.DescribeTable("Music")));

        // Assert
        await Assert.That(exception.InnerException).IsTypeOf<FormatException>();
    }

    [Test]
    public async Task ExecuteAsync_CredentialsUnavailable_ThrowsCredentialsUnavailableException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, credentials: new MissingCredentialsProvider());

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>();
    }

    [Test]
    public async Task ExecuteAsync_CredentialsUnavailable_SendsNothing()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, credentials: new MissingCredentialsProvider());
        await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var count = handler.Requests.Count;

        // Assert
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_NetworkFailure_ThrowsHttpRequestException()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused"));
        using var client = TestClient.Create(handler);

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<HttpRequestException>();
    }

    [Test]
    public async Task ExecuteAsync_ResponseWithoutContentLength_ReadsTheWholeBody()
    {
        // Arrange (a chunked response has no length)
        var bytes = Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson);
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ChunkedStream(bytes, chunkSize: 7, yieldOnRead: true)),
        }));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["name"].AsString()).IsEqualTo("Alice");
    }

    [Test]
    public async Task ExecuteAsync_ResponseLargerThanTheInitialBuffer_ReadsItWithOneRentalOfItsLength()
    {
        // Arrange
        var name = new string('x', 10_000);
        var json = $$$$"""{"Item":{"pk":{"S":"user-1"},"name":{"S":"{{{{name}}}}"}}}""";
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(json));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(pool.RentedLengths).Contains(json.Length);
    }

    [Test]
    public async Task ExecuteAsync_ResponseLargerThanTheInitialBuffer_ReturnsTheWholeItem()
    {
        // Arrange
        var name = new string('x', 10_000);
        using var handler = new StubHandler(_ => StubResponse.Json($$$$"""{"Item":{"pk":{"S":"user-1"},"name":{"S":"{{{{name}}}}"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["name"].AsString()).IsEqualTo(name);
    }

    [Test]
    public async Task ExecuteAsync_ContentLengthAboveTheLimit_ThrowsHttpRequestExceptionConfigurationLimitExceeded()
    {
        // Arrange (the header claims 100 MB, the limit of the client is 1000 bytes)
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            content.Headers.ContentLength = 100_000_000;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = TestClient.Create(handler, maxResponseLength: 1000);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.HttpRequestError).IsEqualTo(HttpRequestError.ConfigurationLimitExceeded);
    }

    [Test]
    public async Task ExecuteAsync_ContentLengthAboveTheLimit_MessageNamesTheLimit()
    {
        // Arrange (the header claims 100 MB, the limit of the client is 1000 bytes)
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            content.Headers.ContentLength = 100_000_000;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = TestClient.Create(handler, maxResponseLength: 1000);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The DynamoDB response is larger than the limit of 1000 bytes.");
    }

    [Test]
    public async Task ExecuteAsync_ContentLengthBeyondTheLargestArray_ThrowsHttpRequestExceptionConfigurationLimitExceeded()
    {
        // Arrange (a header that claims 3 GB, more than any buffer can hold, is a body above the limit like any other)
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            content.Headers.ContentLength = 3_000_000_000;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = TestClient.Create(handler, maxResponseLength: 1000);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.HttpRequestError).IsEqualTo(HttpRequestError.ConfigurationLimitExceeded);
    }

    [Test]
    public async Task ExecuteAsync_ContentLengthAboveTheLimit_RentsNoBufferOfThatSizeAndReturnsEveryArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            content.Headers.ContentLength = 100_000_000;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = TestClient.Create(handler, pool: pool, maxResponseLength: 1000);
        await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var (largest, outstanding) = (pool.RentedLengths.Max(), pool.Outstanding);

        // Assert
        await Assert.That((largest < 1_000_000, outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ExecuteAsync_BodyOfUnknownLengthThatNeverEnds_StopsAtTheLimitAndReturnsEveryArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var body = new EndlessStream();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var client = TestClient.Create(handler, pool: pool, maxResponseLength: 10_000);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That((exception.HttpRequestError, body.BytesServed, pool.Outstanding)).IsEqualTo((HttpRequestError.ConfigurationLimitExceeded, 10_001L, 0));
    }

    [Test]
    public async Task ExecuteAsync_BodyAboveTheLimit_IsNotRetried()
    {
        // Arrange (the same request would get the same response)
        var requests = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) });
        });
        using var client = TestClient.Create(handler, maxAttempts: 4, maxResponseLength: 10_000);
        await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var sent = requests;

        // Assert
        await Assert.That(sent).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ExecuteAsync_BodyExactlyTheLimit_IsRead(bool withContentLength)
    {
        // Arrange (a response with a length announced, and one without)
        var bytes = Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson);
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = withContentLength ? new ByteArrayContent(bytes) : new StreamContent(new ChunkedStream(bytes, chunkSize: 7, yieldOnRead: true)),
        }));
        using var client = TestClient.Create(handler, maxResponseLength: bytes.Length);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["pk"].AsString()).IsEqualTo("user-1");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ExecuteAsync_BodyOneByteAboveTheLimit_ThrowsHttpRequestExceptionConfigurationLimitExceeded(bool withContentLength)
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson);
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = withContentLength ? new ByteArrayContent(bytes) : new StreamContent(new ChunkedStream(bytes, chunkSize: 7, yieldOnRead: true)),
        }));
        using var client = TestClient.Create(handler, maxResponseLength: bytes.Length - 1);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.HttpRequestError).IsEqualTo(HttpRequestError.ConfigurationLimitExceeded);
    }

    [Test]
    public async Task ExecuteAsync_ResponseBodyFailsWhileReading_ThrowsHttpRequestExceptionWithTheFailure()
    {
        // Arrange
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream()),
        }));
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.InnerException).IsTypeOf<IOException>();
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsWhileTheBodyFailsWithAnIoException_ThrowsOperationCanceledExceptionWithTheirToken()
    {
        // Arrange (a read that a cancellation interrupts can end in an IOException instead of an OperationCanceledException)
        using var cancellation = new CancellationTokenSource();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(onRead: cancellation.Cancel)),
        }));
        using var client = TestClient.Create(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => client.GetItemAsync(TestClient.GetItem(), cancellation.Token));

        // Assert
        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsWhileTheBodyFailsWithAnIoException_IsNotRetried()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream(onRead: cancellation.Cancel)) });
        });
        using var client = TestClient.Create(handler, new ManualTimeProvider(), maxAttempts: 3);
        await TestClient.CaptureAsync<OperationCanceledException>(() => client.GetItemAsync(TestClient.GetItem(), cancellation.Token));

        // Act
        var sent = requests;

        // Assert
        await Assert.That(sent).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_AttemptTimesOutWhileTheBodyFailsWithAnIoException_ThrowsTimeoutException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(onRead: () => time.Advance(TimeSpan.FromSeconds(30)))),
        }));
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));

        // Act
        var act = async () => await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(act).ThrowsExactly<TimeoutException>();
    }

    [Test]
    public async Task ExecuteAsync_BodyShorterThanContentLength_ThrowsHttpRequestExceptionResponseEnded()
    {
        // Arrange
        using var handler = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson));
            content.Headers.ContentLength = TestClient.GetItemResponseJson.Length + 10;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = TestClient.Create(handler);

        // Act
        var exception = await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.HttpRequestError).IsEqualTo(HttpRequestError.ResponseEnded);
    }

    [Test]
    public async Task ExecuteAsync_AttemptTakesLongerThanTheTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await arrived;

        // Act
        time.Advance(TimeSpan.FromSeconds(30));

        // Assert
        await Assert.That(async () => await call).ThrowsExactly<TimeoutException>();
    }

    [Test]
    public async Task ExecuteAsync_AttemptTimesOut_MessageNamesOperationAndTimeout()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await arrived;
        time.Advance(TimeSpan.FromSeconds(30));

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => call);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("DynamoDB GetItem did not complete within the attempt timeout of 00:00:30.");
    }

    [Test]
    public async Task ExecuteAsync_AttemptTimesOut_InnerExceptionIsTheCancellation()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var call = client.GetItemAsync(TestClient.GetItem());
        await arrived;
        time.Advance(TimeSpan.FromSeconds(30));

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => call);

        // Assert
        await Assert.That(exception.InnerException).IsAssignableTo<OperationCanceledException>();
    }

    [Test]
    public async Task ExecuteAsync_ClockAdvancesLessThanTheTimeout_DoesNotTimeOut()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived;
        time.Advance(TimeSpan.FromSeconds(29));

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.That(async () => await call).ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    public async Task ExecuteAsync_CallerCancels_ThrowsOperationCanceledExceptionWithTheCallersToken()
    {
        // Arrange
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived;
        await cancellation.CancelAsync();

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_CallerCancels_IsNotATimeoutException()
    {
        // Arrange
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived;
        await cancellation.CancelAsync();

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(exception).IsNotTypeOf<TimeoutException>();
    }

    [Test]
    public async Task ExecuteAsync_CallerCancelsWithoutAttemptTimeout_ThrowsOperationCanceledExceptionWithTheCallersToken()
    {
        // Arrange
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, attemptTimeout: Timeout.InfiniteTimeSpan);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived;
        await cancellation.CancelAsync();

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Assert
        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => client.GetItemAsync(TestClient.GetItem(), cancellation.Token));

        // Assert
        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_HandlerCancelsOnItsOwnWithoutAttemptTimeout_ThrowsTheCancellationUnchanged()
    {
        // Arrange (a cancellation that neither the caller nor the timeout caused is none of the pipeline's business)
        using var handler = new StubHandler(_ => throw new OperationCanceledException("The handler gave up."));
        using var client = TestClient.Create(handler, attemptTimeout: Timeout.InfiniteTimeSpan);

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The handler gave up.");
    }

    [Test]
    public async Task ExecuteAsync_HandlerCancelsOnItsOwnBeforeTheTimeout_ThrowsTheCancellationUnchanged()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new OperationCanceledException("The handler gave up."));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var exception = await TestClient.CaptureAsync<OperationCanceledException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The handler gave up.");
    }

    [Test]
    public async Task ExecuteAsync_FiniteTimeout_CreatesOneTimerAndDisposesIt()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((time.TimersCreated, time.ActiveTimers)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task ExecuteAsync_InfiniteTimeout_CreatesNoTimer()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, time, attemptTimeout: Timeout.InfiniteTimeSpan);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(time.TimersCreated).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task ExecuteAsync_Request_RentsOneBufferWithTheDefaultCapacity()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, pool: pool);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(pool.RentedLengths[0]).IsEqualTo(4096);
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "bad"));
        using var client = TestClient.Create(handler, pool: pool);
        await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_UnparsableResponse_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("not json"));
        using var client = TestClient.Create(handler, pool: pool);
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_Cancelled_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var (handler, arrived) = HangingHandler();
        using var _ = handler;
        using var client = TestClient.Create(handler, pool: pool);
        using var cancellation = new CancellationTokenSource();
        var call = client.GetItemAsync(TestClient.GetItem(), cancellation.Token);
        await arrived;
        await cancellation.CancelAsync();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_CredentialsUnavailable_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, pool: pool, credentials: new MissingCredentialsProvider());
        await TestClient.CaptureAsync<CredentialsUnavailableException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_ResponseBodyFailsWhileReading_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream()),
        }));
        using var client = TestClient.Create(handler, pool: pool);
        await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_ResponseBodyFailsWithOtherThanIO_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(new InvalidOperationException("The stream broke."))),
        }));
        using var client = TestClient.Create(handler, pool: pool);
        await TestClient.CaptureAsync<InvalidOperationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_RequestCannotBeSerialized_ThrowsTheSerializationFailure()
    {
        // Arrange (a default AttributeValue has no type)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var request = new PutItemRequest { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = default } };

        // Act
        var act = async () => await client.PutItemAsync(request);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ExecuteAsync_RequestCannotBeSerialized_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, pool: pool);
        var request = new PutItemRequest { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = default } };
        await TestClient.CaptureAsync<ArgumentException>(() => client.PutItemAsync(request));

        // Act
        var outstanding = pool.Outstanding;

        // Assert
        await Assert.That(outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_PreviousCallFailedWhileSerializing_NextCallSendsTheBodyOfAFreshClient()
    {
        // Arrange (the failed call leaves a half-written body in the buffers that the next call reuses)
        using var freshHandler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var fresh = TestClient.Create(freshHandler);
        await fresh.GetItemAsync(TestClient.GetItem());
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler);
        var broken = new PutItemRequest { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = default } };
        await TestClient.CaptureAsync<ArgumentException>(() => client.PutItemAsync(broken));

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(freshHandler.Requests.Single().BodyText);
    }

    // Checks the wiring only: that the pipeline signs what it sends. The signer itself is checked against botocore in SigV4SignerTests.
    private static string Sign(RecordedRequest sent, Uri endpoint, AwsCredentials credentials, TimeProvider time)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-Amz-Target", sent.Headers["X-Amz-Target"]);
        request.Content = new ByteArrayContent(sent.Body.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.0");
        new SigV4Signer(TestClient.Region.Name).Sign(request, SHA256.HashData(sent.Body.Span), credentials, time.GetUtcNow());
        return request.Headers.GetValues("Authorization").Single();
    }

    private static (StubHandler Handler, Task Arrived) HangingHandler()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json("{}");
        });
        return (handler, arrived.Task);
    }

    private sealed class MissingCredentialsProvider : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) =>
            throw new CredentialsUnavailableException("No credentials.");
    }

    private sealed class FailingStream(Exception? failure = null, Action? onRead = null) : System.IO.Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw Failure();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw Failure();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private Exception Failure()
        {
            onRead?.Invoke();
            return failure ?? new IOException("The connection was closed.");
        }
    }
}
