using System.Net;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests;

/// <summary>
/// The four DynamoDB Streams operations end to end over a <see cref="StubHandler"/>: what goes over the wire, what comes back, the
/// errors, and that the pooled buffers are returned.
/// </summary>
public sealed class DynamoDbStreamsClientOperationsTests
{
    private const string TwoRecords = """
        {"NextShardIterator":"next-iterator","Records":[
         {"awsRegion":"us-west-2","dynamodb":{"ApproximateCreationDateTime":1.46480431E9,"Keys":{"ForumName":{"S":"DynamoDB"}},"SequenceNumber":"300000000000000499659","SizeBytes":41,"StreamViewType":"KEYS_ONLY"},"eventID":"e1","eventName":"INSERT","eventSource":"aws:dynamodb","eventVersion":"1.0"},
         {"awsRegion":"us-west-2","dynamodb":{"Keys":{"ForumName":{"S":"DynamoDB"}},"SequenceNumber":"400000000000000499660","StreamViewType":"KEYS_ONLY"},"eventID":"e2","eventName":"REMOVE","eventSource":"aws:dynamodb","eventVersion":"1.0"}]}
        """;

    [Test]
    public async Task ListStreamsAsync_Request_SendsTheListStreamsOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.ListStreamsAsync(TestClient.ListStreams("Users"));

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.Headers["X-Amz-Target"], sent.BodyText)).IsEqualTo(("ListStreams", "DynamoDBStreams_20120810.ListStreams", """{"TableName":"Users"}"""));
    }

    [Test]
    public async Task ListStreamsAsync_Page_ReturnsTheStreamsAndTheContinuation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$"""{"Streams":[{"StreamArn":"{{TestClient.StreamArn}}","TableName":"Users","StreamLabel":"2026-10-31T12:00:00.000"}],"LastEvaluatedStreamArn":"{{TestClient.StreamArn}}"}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.ListStreamsAsync(TestClient.ListStreams());

        // Assert
        await Assert.That((response.Streams.Single().TableName, response.LastEvaluatedStreamArn)).IsEqualTo(("Users", TestClient.StreamArn));
    }

    [Test]
    public async Task ListStreamsAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));
        using var client = TestClient.CreateStreams(handler, pool: pool);

        // Act
        await client.ListStreamsAsync(TestClient.ListStreams());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task ListStreamsAsync_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var act = async () => await client.ListStreamsAsync(TestClient.ListStreams("Missing"));

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task ListStreamsAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = async () => await client.ListStreamsAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task DescribeStreamAsync_Request_SendsTheDescribeStreamOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"StreamDescription":{"StreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.DescribeStreamAsync(TestClient.DescribeStream());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("DescribeStream", $$"""{"StreamArn":"{{TestClient.StreamArn}}"}"""));
    }

    [Test]
    public async Task DescribeStreamAsync_StreamWithShards_ReturnsTheShardTree()
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeStream", "RetrieveInformationAboutAStreamFromApiReference");
        using var handler = new StubHandler(_ => StubResponse.Json(vector.Wire));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var description = (await client.DescribeStreamAsync(TestClient.DescribeStream())).StreamDescription!;

        // Assert
        await Assert.That((description.Shards.Count, description.StreamStatus, description.StreamViewType)).IsEqualTo((4, StreamStatus.Enabled, StreamViewType.NewAndOldImages));
    }

    [Test]
    public async Task DescribeStreamAsync_ReferenceExampleWithADateString_ThrowsUnrecognizedResponse()
    {
        // Arrange (the AWS reference prints the creation time as a date string; DynamoDB sends epoch seconds)
        const string json = """{"StreamDescription":{"StreamArn":"arn","CreationRequestDateTime":"Wed May 20 13:51:10 PDT 2015"}}""";
        using var handler = new StubHandler(_ => StubResponse.Json(json));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.DescribeStreamAsync(TestClient.DescribeStream()));

        // Assert
        await Assert.That(exception.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task DescribeStreamAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"StreamDescription":{"StreamArn":"{{{TestClient.StreamArn}}}"}}"""));
        using var client = TestClient.CreateStreams(handler, pool: pool);

        // Act
        await client.DescribeStreamAsync(TestClient.DescribeStream());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task DescribeStreamAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = async () => await client.DescribeStreamAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task GetShardIteratorAsync_Request_SendsTheGetShardIteratorOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ShardIterator":"iterator"}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.GetShardIteratorAsync(TestClient.GetShardIterator());

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.BodyText)).IsEqualTo(("GetShardIterator", $$"""{"StreamArn":"{{TestClient.StreamArn}}","ShardId":"shardId-1","ShardIteratorType":"TRIM_HORIZON"}"""));
    }

    [Test]
    public async Task GetShardIteratorAsync_Created_ReturnsTheIterator()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ShardIterator":"iterator"}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.GetShardIteratorAsync(TestClient.GetShardIterator());

        // Assert
        await Assert.That(response.ShardIterator).IsEqualTo("iterator");
    }

    [Test]
    public async Task GetShardIteratorAsync_PositionOlderThanTheRetention_ThrowsTrimmedDataAccessExceptionWithoutRetrying()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("TrimmedDataAccessException", "The requested data is trimmed.") };
        using var client = TestClient.CreateStreams(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<TrimmedDataAccessException>(() => client.GetShardIteratorAsync(TestClient.GetShardIterator()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("TrimmedDataAccessException", 1));
    }

    [Test]
    public async Task GetShardIteratorAsync_IteratorTypeNotSet_ThrowsArgumentOutOfRangeExceptionAndSendsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.CreateStreams(handler);
        var request = TestClient.GetShardIterator() with { ShardIteratorType = ShardIteratorType.Unknown };

        // Act
        var act = async () => await client.GetShardIteratorAsync(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>();
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task GetShardIteratorAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"ShardIterator":"iterator"}"""));
        using var client = TestClient.CreateStreams(handler, pool: pool);

        // Act
        await client.GetShardIteratorAsync(TestClient.GetShardIterator());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task GetShardIteratorAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = async () => await client.GetShardIteratorAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task GetRecordsAsync_Request_SendsTheGetRecordsOperationWithTheRequestBody()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TwoRecords));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords() with { Limit = 100 });

        // Assert
        var sent = handler.Requests.Single();
        await Assert.That((sent.Operation, sent.Headers["X-Amz-Target"], sent.BodyText)).IsEqualTo(("GetRecords", "DynamoDBStreams_20120810.GetRecords", $$"""{"ShardIterator":"{{TestClient.ShardIterator}}","Limit":100}"""));
    }

    [Test]
    public async Task GetRecordsAsync_Records_ReturnsThemWithTheNextIterator()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TwoRecords));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That((string.Join(",", response.Records.Select(record => record.EventName)), response.NextShardIterator)).IsEqualTo(("Insert,Remove", "next-iterator"));
    }

    [Test]
    public async Task GetRecordsAsync_NoNewRecords_ReturnsAnEmptyListAndTheNextIterator()
    {
        // Arrange (an open shard without new changes answers like this, and the reader goes on)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[],"NextShardIterator":"next-iterator"}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That((response.Records.Count, response.NextShardIterator)).IsEqualTo((0, "next-iterator"));
    }

    [Test]
    public async Task GetRecordsAsync_ClosedShard_HasNoNextIterator()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(response.NextShardIterator).IsNull();
    }

    [Test]
    public async Task GetRecordsAsync_ContinuationWithTheReturnedIterator_SendsItAsTheShardIterator()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(TwoRecords));
        handler.Enqueue(StubResponse.Json("""{"Records":[]}"""));
        using var client = TestClient.CreateStreams(handler);
        var first = await client.GetRecordsAsync(TestClient.GetRecords());

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords(first.NextShardIterator!));

        // Assert
        await Assert.That(handler.Requests[1].BodyText).IsEqualTo("""{"ShardIterator":"next-iterator"}""");
    }

    [Test]
    public async Task GetRecordsAsync_Success_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json(TwoRecords));
        using var client = TestClient.CreateStreams(handler, pool: pool);

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((0, 2));
    }

    [Test]
    public async Task GetRecordsAsync_ResponseWithMalformedRecord_ThrowsUnrecognizedResponseAndReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[{"dynamodb":{"Keys":{"pk":{"X":"b"}}}}]}"""));
        using var client = TestClient.CreateStreams(handler, pool: pool);

        // Act
        var exception = await TestClient.CaptureAsync<DynamoDbException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((exception.ErrorCode, pool.Outstanding)).IsEqualTo(("UnrecognizedResponse", 0));
    }

    [Test]
    public async Task GetRecordsAsync_ExpiredIterator_ThrowsExpiredIteratorExceptionWithoutRetrying()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ExpiredIteratorException", "The provided iterator exceeds the maximum age allowed.") };
        using var client = TestClient.CreateStreams(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<ExpiredIteratorException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((exception.ErrorCode, exception.StatusCode, handler.Requests.Count)).IsEqualTo(("ExpiredIteratorException", HttpStatusCode.BadRequest, 1));
    }

    [Test]
    public async Task GetRecordsAsync_ExpiredIteratorWithAnotherStatus_KeepsTheStatus()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ExpiredIteratorException", "Expired.", HttpStatusCode.Forbidden));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ExpiredIteratorException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetRecordsAsync_TrimmedData_ThrowsTrimmedDataAccessExceptionWithoutRetrying()
    {
        // Arrange
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("TrimmedDataAccessException", "The requested data is trimmed.") };
        using var client = TestClient.CreateStreams(handler, maxAttempts: 3);

        // Act
        var exception = await TestClient.CaptureAsync<TrimmedDataAccessException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((exception.ErrorCode, handler.Requests.Count)).IsEqualTo(("TrimmedDataAccessException", 1));
    }

    [Test]
    public async Task GetRecordsAsync_RequestRateTooHigh_IsRetriedLikeAnyThrottlingError()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("LimitExceededException", "Rate exceeded."));
        handler.Enqueue(StubResponse.Json(TwoRecords));
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That((response.Records.Count, handler.Requests.Count)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task GetRecordsAsync_ServerError_IsRetried()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json(TwoRecords));
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That((response.Records.Count, handler.Requests.Count)).IsEqualTo((2, 2));
    }

    [Test]
    public async Task GetRecordsAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        using var client = TestClient.CreateStreams(new StubHandler());

        // Act
        var act = async () => await client.GetRecordsAsync(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }
}
