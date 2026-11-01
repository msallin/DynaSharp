using System.Net;
using System.Text;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Testing.HandlerRequest;

namespace DynaSharp.UnitTests.Testing;

public sealed class StubHandlerTests
{
    [Test]
    public async Task SendAsync_QueuedResponse_ReturnsItsStatusCode()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}", HttpStatusCode.Accepted));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task SendAsync_QueuedResponse_ReturnsItsBody()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"a"}}}"""));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task SendAsync_QueuedResponse_ReturnsTheDynamoDbContentType()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(response.Content.Headers.ContentType!.ToString()).IsEqualTo("application/x-amz-json-1.0");
    }

    [Test]
    public async Task SendAsync_ResponseWithHeader_ReturnsTheHeader()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["x-amzn-RequestId"] = "ABC123" };
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}", headers: headers));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(response.Headers.GetValues("x-amzn-RequestId").Single()).IsEqualTo("ABC123");
    }

    [Test]
    public async Task SendAsync_ResponseWithContentHeader_ReplacesTheDefaultContentHeader()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["Content-Type"] = "text/html" };
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("<html></html>", HttpStatusCode.ServiceUnavailable, headers));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/html");
    }

    [Test]
    public async Task SendAsync_TwoQueuedResponses_ReturnsThemInOrder()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"n":1}"""));
        handler.Enqueue(StubResponse.Json("""{"n":2}"""));

        // Act
        using var first = await PostAsync(handler);
        using var second = await PostAsync(handler);

        // Assert
        await Assert.That(await second.Content.ReadAsStringAsync()).IsEqualTo("""{"n":2}""");
    }

    [Test]
    public async Task Enqueue_Null_ThrowsArgumentNullException()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var act = () => handler.Enqueue(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("response");
    }

    [Test]
    public async Task SendAsync_NothingQueued_ThrowsInvalidOperationException()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var act = async () => await PostAsync(handler);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_NothingQueued_MessageNamesTheOperation()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.PutItem"));

        // Assert
        await Assert.That(exception.Message).Contains("operation PutItem", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_NothingQueued_MessageNamesMethodAndUri()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler));

        // Assert
        await Assert.That(exception.Message).Contains("POST " + Endpoint, StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_NothingQueued_MessageSaysHowToFixIt()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler));

        // Assert
        await Assert.That(exception.Message).Contains("Enqueue", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_NothingQueuedAndRequestWithoutTarget_MessageSaysUnknownOperation()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: null));

        // Assert
        await Assert.That(exception.Message).Contains("operation unknown", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_NothingQueued_StillRecordsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler));

        // Act
        var count = handler.Requests.Count;

        // Assert
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_QueueDrainedAndDefaultResponse_ReturnsTheDefaultResponse()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("""{"default":true}""") };

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"default":true}""");
    }

    [Test]
    public async Task SendAsync_DefaultResponseUsedTwice_AnswersBothRequests()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("""{"default":true}""") };
        using var first = await PostAsync(handler);

        // Act
        using var second = await PostAsync(handler);

        // Assert
        await Assert.That(await second.Content.ReadAsStringAsync()).IsEqualTo("""{"default":true}""");
    }

    [Test]
    public async Task SendAsync_QueuedResponseAndDefaultResponse_QueuedResponseWins()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.Json("""{"default":true}""") };
        handler.Enqueue(StubResponse.Json("""{"queued":true}"""));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"queued":true}""");
    }

    [Test]
    public async Task SendAsync_ResponderAndDefaultResponse_ResponderWins()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"responder":true}""")) { DefaultResponse = StubResponse.Json("""{"default":true}""") };

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"responder":true}""");
    }

    [Test]
    public async Task SendAsync_Responder_ReceivesTheRecordedRequest()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json($$"""{"operation":"{{request.Operation}}"}"""));

        // Act
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.DescribeTable");

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"operation":"DescribeTable"}""");
    }

    [Test]
    public async Task SendAsync_QueuedResponseAndResponder_QueuedResponseWins()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"responder":true}"""));
        handler.Enqueue(StubResponse.Json("""{"queued":true}"""));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"queued":true}""");
    }

    [Test]
    public async Task SendAsync_AsyncResponder_ReceivesTheCancellationTokenOfTheRequest()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var received = CancellationToken.None;
        using var handler = new StubHandler((_, cancellationToken) =>
        {
            received = cancellationToken;
            return Task.FromResult(StubResponse.Json("{}"));
        });

        // Act
        using var response = await PostAsync(handler, cancellationToken: cancellation.Token);

        // Assert
        await Assert.That(received.CanBeCanceled).IsTrue();
    }

    [Test]
    public async Task SendAsync_ResponderThrows_ThrowsTheSameException()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        // Act
        var act = async () => await PostAsync(handler);

        // Assert
        await Assert.That(act).ThrowsExactly<HttpRequestException>().WithMessage("connection refused", StringComparison.Ordinal);
    }

    [Test]
    public async Task Constructor_NullResponder_ThrowsArgumentNullException()
    {
        // Arrange
        Func<RecordedRequest, StubResponse> responder = null!;

        // Act
        var act = () => new StubHandler(responder);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("responder");
    }

    [Test]
    public async Task Constructor_NullAsyncResponder_ThrowsArgumentNullException()
    {
        // Arrange
        Func<RecordedRequest, CancellationToken, Task<StubResponse>> responder = null!;

        // Act
        var act = () => new StubHandler(responder);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("responder");
    }

    [Test]
    public async Task Requests_NewHandler_IsEmpty()
    {
        // Arrange
        using var handler = new StubHandler();

        // Act
        var requests = handler.Requests;

        // Assert
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    public async Task Requests_AfterTwoRequests_ListsThemOldestFirst()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json("{}"));
        using var first = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");
        using var second = await PostAsync(handler, target: "DynamoDB_20120810.PutItem");

        // Act
        var operations = string.Join(',', handler.Requests.Select(request => request.Operation));

        // Assert
        await Assert.That(operations).IsEqualTo("GetItem,PutItem");
    }

    [Test]
    public async Task Requests_ListTakenEarlier_DoesNotChangeWhenMoreRequestsArrive()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json("{}"));
        using var first = await PostAsync(handler);
        var snapshot = handler.Requests;

        // Act
        using var second = await PostAsync(handler);

        // Assert
        await Assert.That(snapshot.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_RequestWithBody_RecordsTheBodyBytes()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, body: """{"TableName":"Zürich"}""");

        // Act
        var body = handler.Requests.Single().Body.ToArray();

        // Assert
        await Assert.That(body).IsEquivalentTo(Encoding.UTF8.GetBytes("""{"TableName":"Zürich"}"""));
    }

    [Test]
    public async Task Requests_RequestWithBody_RecordsTheBodyText()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, body: """{"TableName":"Zürich"}""");

        // Act
        var text = handler.Requests.Single().BodyText;

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Zürich"}""");
    }

    [Test]
    public async Task Requests_RequestWithoutContent_RecordsAnEmptyBody()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        using var response = await client.SendAsync(request);

        // Act
        var body = handler.Requests.Single().Body;

        // Assert
        await Assert.That(body.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Requests_Request_RecordsMethodAndUri()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler);

        // Act
        var recorded = handler.Requests.Single();

        // Assert
        await Assert.That($"{recorded.Method} {recorded.RequestUri}").IsEqualTo("POST " + Endpoint);
    }

    [Test]
    public async Task Requests_Request_RecordsRequestHeaders()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Act
        var target = handler.Requests.Single().Headers["X-Amz-Target"];

        // Assert
        await Assert.That(target).IsEqualTo("DynamoDB_20120810.GetItem");
    }

    [Test]
    public async Task Requests_Request_RecordsContentHeaders()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler);

        // Act
        var contentType = handler.Requests.Single().Headers["Content-Type"];

        // Assert
        await Assert.That(contentType).StartsWith("application/x-amz-json-1.0");
    }

    [Test]
    public async Task Requests_RequestWithKnownLength_RecordsTheContentLength()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, body: """{"TableName":"Zürich"}""");

        // Act
        var contentLength = handler.Requests.Single().Headers["Content-Length"];

        // Assert
        await Assert.That(contentLength).IsEqualTo("23");
    }

    [Test]
    public async Task Requests_RequestWithStreamedContent_HasNoContentLength()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StreamContent(new ChunkedStream("{}"u8.ToArray(), chunkSize: 1, yieldOnRead: false)),
        };
        using var response = await client.SendAsync(request);

        // Act
        var found = handler.Requests.Single().Headers.ContainsKey("Content-Length");

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task Requests_HeaderNameInOtherCase_IsFound()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Act
        var found = handler.Requests.Single().Headers.ContainsKey("x-amz-target");

        // Assert
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Requests_HeaderWithTwoValues_JoinsThemWithCommaAndSpace()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.TryAddWithoutValidation("X-Test", ["a", "b"]);
        using var response = await client.SendAsync(request);

        // Act
        var value = handler.Requests.Single().Headers["X-Test"];

        // Assert
        await Assert.That(value).IsEqualTo("a, b");
    }

    [Test]
    public async Task Requests_RequestWithoutTargetHeader_OperationIsNull()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler, target: null);

        // Act
        var operation = handler.Requests.Single().Operation;

        // Assert
        await Assert.That(operation).IsNull();
    }

    [Test]
    public async Task Requests_RecordRequestsOff_StaysEmpty()
    {
        // Arrange
        using var handler = new StubHandler { RecordRequests = false };
        handler.Enqueue(StubResponse.Json("{}"));
        using var response = await PostAsync(handler);

        // Act
        var requests = handler.Requests;

        // Assert
        await Assert.That(requests).IsEmpty();
    }

    [Test]
    public async Task SendAsync_RecordRequestsOff_StillReturnsTheResponse()
    {
        // Arrange
        using var handler = new StubHandler { RecordRequests = false };
        handler.Enqueue(StubResponse.Json("{}", HttpStatusCode.Accepted));

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task SendAsync_RecordRequestsOffWithResponder_StillCallsTheResponder()
    {
        // Arrange
        using var handler = new StubHandler(request => StubResponse.Json($$"""{"operation":"{{request.Operation}}"}""")) { RecordRequests = false };

        // Act
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"operation":"GetItem"}""");
    }

    [Test]
    public async Task RecordRequests_Default_IsTrue()
    {
        // Arrange (nothing to arrange)

        // Act
        using var handler = new StubHandler();

        // Assert
        await Assert.That(handler.RecordRequests).IsTrue();
    }

    [Test]
    public async Task DefaultResponse_Default_IsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        using var handler = new StubHandler();

        // Assert
        await Assert.That(handler.DefaultResponse).IsNull();
    }
}
