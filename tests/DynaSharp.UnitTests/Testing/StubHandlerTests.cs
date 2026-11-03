using System.Net;
using System.Text;
using DynaSharp.Requests;
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

    [Test]
    public async Task SendAsync_ResponseOfTheOperation_ReturnsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Item(new Item { ["pk"] = "a" }));

        // Act
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"Item":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task SendAsync_ResponseOfTheSecondOfItsOperations_ReturnsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Count(2));

        // Act
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.Scan");

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"Count":2,"ScannedCount":2}""");
    }

    [Test]
    public async Task SendAsync_ErrorThatFitsAnyOperation_ReturnsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ResourceNotFound());

        // Act
        using var response = await PostAsync(handler, target: "DynamoDB_20120810.UpdateTimeToLive");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    // The trap of a service: an answer of UpdateTimeToLive reads as no item, so a test of a miss passed on it.
    [Test]
    public async Task SendAsync_QueuedResponseOfAnotherOperation_ThrowsInvalidOperationException()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TimeToLiveUpdated(true, "expires"));

        // Act
        var act = async () => await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_QueuedResponseOfAnotherOperation_MessageNamesBothOperations()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Items(Array.Empty<Item>()));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.GetItem"));

        // Assert
        await Assert.That(exception.Message).Contains("for a request to GetItem, but that response is the answer to Query or Scan.", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_SecondQueuedResponseOfAnotherOperation_MessageGivesItsPositionInTheQueue()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Item<Item>(null));
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        using var first = await PostAsync(handler, target: "DynamoDB_20120810.GetItem");

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.Query"));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took queued response 2 for a request to Query", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_QueuedResponseOfAnotherOperation_MessageSaysHowToFixIt()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Item<Item>(null));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.PutItem"));

        // Assert
        await Assert.That(exception.Message).EndsWith(
            "A test that went on with it would pass for the wrong reason. Give the request the answer of its own operation, or StubResponse.Json for a body that fits any.",
            StringComparison.Ordinal);
    }

    // The answer of DescribeTable is what a test of an UpdateTable reaches for first, and a ListTables had nothing but Json.
    [Test]
    [Arguments("UpdateTable", "StubResponse.TableUpdated for an UpdateTable")]
    [Arguments("ListTables", "StubResponse.TablesListed for a ListTables")]
    public async Task SendAsync_QueuedResponseOfAnotherOperation_MessageNamesTheFactoryOfTheOperation(string operation, string factory)
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Table("Orders"));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810." + operation));

        // Assert
        await Assert.That(exception.Message).EndsWith(
            $"Give the request the answer of its own operation, {factory}, or StubResponse.Json for a body that fits any.",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_QueuedResponseOfAnotherOperation_StillRecordsTheRequest()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ShardIterator("iterator"));

        // Act
        await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDBStreams_20120810.GetRecords"));

        // Assert
        await Assert.That(handler.Requests.Single().Operation).IsEqualTo("GetRecords");
    }

    [Test]
    public async Task SendAsync_ResponseOfAnOperationForARequestWithoutTarget_MessageSaysTheRequestHasNone()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TableDeleted("Orders"));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: null));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took queued response 1 for a request without X-Amz-Target, but that response is the answer to DeleteTable.", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_ResponderAnswersWithAnotherOperation_MessageNamesTheResponder()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.TableCreated("Orders"));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.DescribeTable"));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took the response of its responder for a request to DescribeTable, but that response is the answer to CreateTable.", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_DefaultResponseOfAnotherOperation_MessageNamesTheDefaultResponse()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.TimeToLive(TimeToLiveStatus.Disabled) };

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.UpdateTimeToLive"));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took DefaultResponse for a request to UpdateTimeToLive, but that response is the answer to DescribeTimeToLive.", StringComparison.Ordinal);
    }

    // A responder that has no answer leaves the request to DefaultResponse, and the message names where the response came from.
    [Test]
    public async Task SendAsync_ResponderAnswersNullAndDefaultResponseOfAnotherOperation_MessageNamesTheDefaultResponse()
    {
        // Arrange
        using var handler = new StubHandler(_ => null!) { DefaultResponse = StubResponse.Records(null) };

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDBStreams_20120810.DescribeStream"));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took DefaultResponse for a request to DescribeStream, but that response is the answer to GetRecords.", StringComparison.Ordinal);
    }

    [Test]
    public async Task SendAsync_RecordRequestsOffAndResponseOfAnotherOperation_MessageNamesTheOperationOfTheRequest()
    {
        // Arrange
        using var handler = new StubHandler { RecordRequests = false };
        handler.Enqueue(StubResponse.Unprocessed("Orders"));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() => PostAsync(handler, target: "DynamoDB_20120810.BatchGetItem"));

        // Assert
        await Assert.That(exception.Message).StartsWith("StubHandler took queued response 1 for a request to BatchGetItem, but that response is the answer to BatchWriteItem.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfResponsesLeft_EveryResponseTaken_DoesNotThrow()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(new StubResponse());
        using var response = await PostAsync(handler);

        // Act
        var act = handler.ThrowIfResponsesLeft;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task ThrowIfResponsesLeft_ResponseLeft_ThrowsInvalidOperationException()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(new StubResponse());

        // Act
        var act = handler.ThrowIfResponsesLeft;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ThrowIfResponsesLeft_OneResponseLeft_MessageGivesItsPositionAndOperation()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Item<Item>(null));
        using var response = await PostAsync(handler);

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            handler.ThrowIfResponsesLeft();
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(exception.Message).IsEqualTo(
            "StubHandler has 1 queued response that no request took: response 2, the answer to GetItem. The code under test sent fewer requests than the test queued answers for.");
    }

    [Test]
    public async Task ThrowIfResponsesLeft_TwoResponsesLeft_MessageListsBoth()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded"));
        handler.Enqueue(StubResponse.Attributes<Item>(null));

        // Act
        var exception = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            handler.ThrowIfResponsesLeft();
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(exception.Message).StartsWith(
            "StubHandler has 2 queued responses that no request took: response 1, the answer to any operation; response 2, the answer to PutItem, UpdateItem or DeleteItem.",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfResponsesLeft_ResponseLeft_LeavesItInTheQueue()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"n":1}"""));
        await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            handler.ThrowIfResponsesLeft();
            return Task.CompletedTask;
        });

        // Act
        using var response = await PostAsync(handler);

        // Assert
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"n":1}""");
    }
}
