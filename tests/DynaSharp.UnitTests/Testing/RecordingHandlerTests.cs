using System.Net;
using System.Text;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Testing.HandlerRequest;

namespace DynaSharp.UnitTests.Testing;

public sealed class RecordingHandlerTests
{
    [Test]
    public async Task SendAsync_InnerHandlerAnswers_ReturnsTheStatusCode()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}", HttpStatusCode.Accepted));
        using var recorder = new RecordingHandler(stub);

        // Act
        using var response = await PostAsync(recorder);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task SendAsync_InnerHandlerAnswers_CallerStillReadsTheBody()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"Zürich"}}}"""));
        using var recorder = new RecordingHandler(stub);

        // Act
        using var response = await PostAsync(recorder);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"Item":{"pk":{"S":"Zürich"}}}""");
    }

    [Test]
    public async Task SendAsync_InnerHandlerAnswers_CallerStillReadsTheBodyAsStream()
    {
        // Arrange (the pipeline reads responses as streams)
        using var stub = new StubHandler(_ => StubResponse.Json("""{"a":1}"""));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Assert
        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("""{"a":1}""");
    }

    [Test]
    public async Task SendAsync_InnerHandlerAnswers_KeepsTheContentType()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);

        // Act
        using var response = await PostAsync(recorder);

        // Assert
        await Assert.That(response.Content.Headers.ContentType!.ToString()).IsEqualTo("application/x-amz-json-1.0");
    }

    [Test]
    public async Task SendAsync_InnerHandlerAnswers_KeepsTheResponseHeaders()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["x-amzn-RequestId"] = "ABC123" };
        using var stub = new StubHandler(_ => StubResponse.Json("{}", headers: headers));
        using var recorder = new RecordingHandler(stub);

        // Act
        using var response = await PostAsync(recorder);

        // Assert
        await Assert.That(response.Headers.GetValues("x-amzn-RequestId").Single()).IsEqualTo("ABC123");
    }

    [Test]
    public async Task Exchanges_NewHandler_IsEmpty()
    {
        // Arrange
        using var stub = new StubHandler();
        using var recorder = new RecordingHandler(stub);

        // Act
        var exchanges = recorder.Exchanges;

        // Assert
        await Assert.That(exchanges).IsEmpty();
    }

    [Test]
    public async Task Exchanges_Exchange_RecordsTheRequest()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder, body: """{"TableName":"Zürich"}""", target: "DynamoDB_20120810.GetItem");

        // Act
        var request = recorder.Exchanges.Single().Request;

        // Assert
        await Assert.That($"{request.Operation} {request.BodyText}").IsEqualTo("""GetItem {"TableName":"Zürich"}""");
    }

    [Test]
    public async Task Exchanges_Exchange_RecordsTheStatusCode()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "gone", HttpStatusCode.BadRequest));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        var recorded = recorder.Exchanges.Single().Response!;

        // Assert
        await Assert.That(recorded.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Exchanges_Exchange_RecordsTheBodyText()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("""{"Item":{"pk":{"S":"Zürich"}}}"""));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        var text = recorder.Exchanges.Single().Response!.BodyText;

        // Assert
        await Assert.That(text).IsEqualTo("""{"Item":{"pk":{"S":"Zürich"}}}""");
    }

    [Test]
    public async Task Exchanges_Exchange_RecordsTheBodyBytes()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("""{"n":"é"}"""));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        var body = recorder.Exchanges.Single().Response!.Body.ToArray();

        // Assert
        await Assert.That(body).IsEquivalentTo(Encoding.UTF8.GetBytes("""{"n":"é"}"""));
    }

    [Test]
    public async Task Exchanges_BodyRecordedBeforeTheCallerReadsIt_StaysAvailableAfterwards()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("""{"a":1}"""));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);
        await response.Content.ReadAsStringAsync();

        // Act
        var text = recorder.Exchanges.Single().Response!.BodyText;

        // Assert
        await Assert.That(text).IsEqualTo("""{"a":1}""");
    }

    [Test]
    public async Task Exchanges_Exchange_RecordsResponseAndContentHeaders()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["x-amzn-RequestId"] = "ABC123" };
        using var stub = new StubHandler(_ => StubResponse.Json("""{"a":1}""", headers: headers));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        var recorded = recorder.Exchanges.Single().Response!.Headers;

        // Assert
        await Assert.That($"{recorded["x-amzn-requestid"]} {recorded["Content-Type"]} {recorded["Content-Length"]}").IsEqualTo("ABC123 application/x-amz-json-1.0 7");
    }

    [Test]
    public async Task Exchanges_Exchange_KeepsTheSignatureHeadersOfTheRequest()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);
        using var client = TestClient.Create(recorder);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        var found = recorder.Exchanges.Single().Request.Headers.ContainsKey("Authorization");

        // Assert
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task GetItemAsync_ThroughRecordingHandler_ClientStillParsesTheResponse()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var recorder = new RecordingHandler(stub);
        using var client = TestClient.Create(recorder);

        // Act
        var response = await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(response.Item!["name"].AsString).IsEqualTo("Alice");
    }

    [Test]
    public async Task GetItemAsync_ThroughRecordingHandler_RecordsTheResponseTheClientParsed()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var recorder = new RecordingHandler(stub);
        using var client = TestClient.Create(recorder);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        var text = recorder.Exchanges.Single().Response!.BodyText;

        // Assert
        await Assert.That(text).IsEqualTo(TestClient.GetItemResponseJson);
    }

    [Test]
    public async Task GetItemAsync_ErrorResponseThroughRecordingHandler_ClientStillMapsTheError()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));
        using var recorder = new RecordingHandler(stub);
        using var client = TestClient.Create(recorder);

        // Act
        var exception = await TestClient.CaptureAsync<ResourceNotFoundException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task Exchanges_TwoCalls_ListsThemOldestFirst()
    {
        // Arrange
        using var stub = new StubHandler(request => StubResponse.Json($$"""{"operation":"{{request.Operation}}"}"""));
        using var recorder = new RecordingHandler(stub);
        using var first = await PostAsync(recorder, target: "DynamoDB_20120810.GetItem");
        using var second = await PostAsync(recorder, target: "DynamoDB_20120810.PutItem");

        // Act
        var operations = string.Join(',', recorder.Exchanges.Select(exchange => exchange.Request.Operation));

        // Assert
        await Assert.That(operations).IsEqualTo("GetItem,PutItem");
    }

    [Test]
    public async Task Exchanges_ListTakenEarlier_DoesNotChangeWhenMoreRequestsArrive()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);
        using var first = await PostAsync(recorder);
        var snapshot = recorder.Exchanges;

        // Act
        using var second = await PostAsync(recorder);

        // Assert
        await Assert.That(snapshot.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Exchanges_SuccessfulExchange_HasNoError()
    {
        // Arrange
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);
        using var response = await PostAsync(recorder);

        // Act
        var error = recorder.Exchanges.Single().Error;

        // Assert
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task SendAsync_InnerHandlerThrows_ThrowsTheSameException()
    {
        // Arrange
        using var stub = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        using var recorder = new RecordingHandler(stub);

        // Act
        var act = async () => await PostAsync(recorder);

        // Assert
        await Assert.That(act).ThrowsExactly<HttpRequestException>().WithMessage("connection refused", StringComparison.Ordinal);
    }

    [Test]
    public async Task Exchanges_InnerHandlerThrows_RecordsRequestAndExceptionWithoutResponse()
    {
        // Arrange
        using var stub = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        using var recorder = new RecordingHandler(stub);
        await TestClient.CaptureAsync<HttpRequestException>(() => PostAsync(recorder, target: "DynamoDB_20120810.GetItem"));

        // Act
        var exchange = recorder.Exchanges.Single();

        // Assert
        await Assert.That($"{exchange.Request.Operation} {exchange.Error!.Message} {exchange.Response is null}").IsEqualTo("GetItem connection refused True");
    }

    [Test]
    public async Task SendAsync_BodyCannotBeRead_DisposesTheResponseThatIsNotPassedOn()
    {
        // Arrange
        using var response = new TrackingResponse { Content = new FailingContent() };
        using var inner = new TrackingHandler(_ => response);
        using var recorder = new RecordingHandler(inner);

        // Act
        await TestClient.CaptureAsync<HttpRequestException>(() => PostAsync(recorder));

        // Assert
        await Assert.That(response.Disposed).IsTrue();
    }

    [Test]
    public async Task Exchanges_BodyCannotBeRead_RecordsTheExceptionAndPassesItOn()
    {
        // Arrange
        using var inner = new TrackingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingContent() });
        using var recorder = new RecordingHandler(inner);
        var thrown = await TestClient.CaptureAsync<HttpRequestException>(() => PostAsync(recorder));

        // Act
        var error = recorder.Exchanges.Single().Error;

        // Assert
        await Assert.That(error).IsSameReferenceAs(thrown);
    }

    [Test]
    public async Task Constructor_NullInnerHandler_ThrowsArgumentNullException()
    {
        // Arrange
        HttpMessageHandler inner = null!;

        // Act
        var act = () => new RecordingHandler(inner);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("innerHandler");
    }

    [Test]
    public async Task Constructor_NoInnerHandler_SendsWithASocketsHttpHandler()
    {
        // Arrange (nothing to arrange)

        // Act
        using var recorder = new RecordingHandler();

        // Assert
        await Assert.That(recorder.InnerHandler).IsTypeOf<SocketsHttpHandler>();
    }

    [Test]
    public async Task Dispose_InnerHandlerGiven_DisposesTheInnerHandler()
    {
        // Arrange
        using var inner = new TrackingHandler(_ => new HttpResponseMessage());
        using var recorder = new RecordingHandler(inner);

        // Act
        recorder.Dispose();

        // Assert
        await Assert.That(inner.Disposed).IsTrue();
    }

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("connection reset");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TrackingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
