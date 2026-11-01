using System.Text;
using DynaSharp.Streams;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests;

/// <summary><see cref="DynamoDbStreamsClient.SendRawAsync"/>: the same escape hatch for the operations of DynamoDB Streams.</summary>
public sealed class DynamoDbStreamsClientRawTests
{
    private const string RecordsResponse = """{"Records":[],"NextShardIterator":"next"}""";

    [Test]
    public async Task SendRawAsync_GetRecords_ReturnsTheResponseAsTheServiceSentIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(RecordsResponse));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var response = await client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator"}"""u8.ToArray());

        // Assert
        await Assert.That(Encoding.UTF8.GetString(response)).IsEqualTo(RecordsResponse);
    }

    [Test]
    public async Task SendRawAsync_Operation_SendsItInTheStreamsTargetHeader()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(RecordsResponse));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator"}"""u8.ToArray());

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo("DynamoDBStreams_20120810.GetRecords");
    }

    [Test]
    public async Task SendRawAsync_Request_SendsTheJsonUnchangedAsTheBody()
    {
        // Arrange
        const string Json = """{"StreamArn":"arn","SomeParameterOfTheFuture":1}""";
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.SendRawAsync("DescribeStream", Encoding.UTF8.GetBytes(Json));

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(Json);
    }

    [Test]
    public async Task SendRawAsync_ErrorResponse_ExceptionNamesTheOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ExpiredIteratorException", "The iterator expired."));
        using var client = TestClient.CreateStreams(handler);

        // Act
        var exception = await TestClient.CaptureAsync<ExpiredIteratorException>(() => client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator"}"""u8.ToArray()));

        // Assert
        await Assert.That(exception.OperationName).IsEqualTo("GetRecords");
    }

    [Test]
    public async Task SendRawAsync_ThrottledOnce_SendsAgainAndReturnsTheResponse()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json(RecordsResponse));
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        await client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator"}"""u8.ToArray());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SendRawAsync_DisposedClient_ThrowsObjectDisposedExceptionNamingTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.CreateStreams(handler);
        client.Dispose();

        // Act
        var exception = await TestClient.CaptureAsync<ObjectDisposedException>(() => client.SendRawAsync("GetRecords", "{}"u8.ToArray()));

        // Assert
        await Assert.That(exception.ObjectName).IsEqualTo(typeof(DynamoDbStreamsClient).FullName);
    }

    [Test]
    public async Task SendRawAsync_OperationWithASpace_ThrowsArgumentException()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.CreateStreams(handler);

        // Act
        Func<Task> act = () => client.SendRawAsync("Get Records", "{}"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("operation");
    }
}
