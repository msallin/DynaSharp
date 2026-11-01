using System.Diagnostics;
using DynaSharp.Streams;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Telemetry;

// Spans, the duration metric, the counters and the logs of DynamoDB Streams calls. The meter and the activity source are global and
// tests run in parallel, so every test filters by a trace of its own, by a host name of its own or by an operation that no other test retries.
public sealed class StreamsTelemetryTests
{
    private const string NextIterator = "next-iterator-secret-77c0";

    [Test]
    public async Task GetRecordsAsync_Listener_StartsAnActivityNamedAfterTheStreamsServiceAndOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB Streams.GetRecords");
    }

    [Test]
    [Arguments("DescribeStream", "DynamoDB Streams.DescribeStream")]
    [Arguments("GetShardIterator", "DynamoDB Streams.GetShardIterator")]
    [Arguments("ListStreams", "DynamoDB Streams.ListStreams")]
    public async Task Call_Listener_StartsAnActivityNamedAfterTheStreamsServiceAndOperation(string operation, string expected)
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$"""{"StreamDescription":{"StreamArn":"arn"},"ShardIterator":"iterator"}"""));

        // Act
        var activity = await RunAsync(handler, client => operation switch
        {
            "DescribeStream" => client.DescribeStreamAsync(TestClient.DescribeStream()),
            "GetShardIterator" => client.GetShardIteratorAsync(TestClient.GetShardIterator()),
            _ => client.ListStreamsAsync(TestClient.ListStreams()),
        });

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo(expected);
    }

    [Test]
    public async Task GetRecordsAsync_Listener_ActivityIsAClientSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
    }

    [Test]
    public async Task GetRecordsAsync_Listener_ReportsTheServiceAndTheMethod()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((activity.GetTagItem("rpc.system"), activity.GetTagItem("rpc.service"), activity.GetTagItem("rpc.method"), activity.GetTagItem("db.operation.name")))
            .IsEqualTo(((object?)"aws-api", "DynamoDB Streams", "GetRecords", "GetRecords"));
    }

    [Test]
    public async Task GetRecordsAsync_Listener_ReportsRegionAndStreamsHost()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((activity.GetTagItem("cloud.region"), activity.GetTagItem("server.address"))).IsEqualTo(((object?)"eu-central-2", "streams.dynamodb.eu-central-2.amazonaws.com"));
    }

    [Test]
    public async Task GetRecordsAsync_Listener_HasNoTableTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), activity.GetTagItem("aws.dynamodb.table_names"))).IsEqualTo(((object?)null, (object?)null));
    }

    [Test]
    public async Task GetRecordsAsync_Listener_NoTagCarriesTheShardIterator()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$"""{"Records":[],"NextShardIterator":"{{NextIterator}}"}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords() with { Limit = 10 }));

        // Assert
        await Assert.That(AllTagText(activity)).DoesNotContain(TestClient.ShardIterator).And.DoesNotContain(NextIterator);
    }

    [Test]
    public async Task GetShardIteratorAsync_Listener_NoTagCarriesTheIteratorOrTheStream()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json($$"""{"ShardIterator":"{{NextIterator}}"}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetShardIteratorAsync(TestClient.GetShardIterator()));

        // Assert
        await Assert.That(AllTagText(activity)).DoesNotContain(NextIterator).And.DoesNotContain(TestClient.StreamArn);
    }

    [Test]
    public async Task GetRecordsAsync_Listener_SetsTheLimitTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.GetRecordsAsync(TestClient.GetRecords() with { Limit = 250 }));

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.limit")).IsEqualTo(250);
    }

    [Test]
    public async Task ListStreamsAsync_ListenerWithTable_ReportsTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ListStreamsAsync(TestClient.ListStreams("Orders")));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), ((string[])activity.GetTagItem("aws.dynamodb.table_names")!).Single())).IsEqualTo(((object?)"Orders", "Orders"));
    }

    [Test]
    public async Task ListStreamsAsync_ListenerWithoutTable_HasNoTableTags()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Streams":[]}"""));

        // Act
        var activity = await RunAsync(handler, client => client.ListStreamsAsync(TestClient.ListStreams()));

        // Assert
        await Assert.That((activity.GetTagItem("db.collection.name"), activity.GetTagItem("aws.dynamodb.table_names"))).IsEqualTo(((object?)null, (object?)null));
    }

    [Test]
    public async Task GetRecordsAsync_ExpiredIterator_RecordsTheErrorOnTheSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ExpiredIteratorException", "The provided iterator exceeds the maximum age allowed."));
        using var capture = new ActivityCapture();
        using var client = TestClient.CreateStreams(handler);

        // Act
        await TestClient.CaptureAsync<ExpiredIteratorException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Assert
        var activity = capture.Single();
        await Assert.That((activity.Status, activity.GetTagItem("error.type"), activity.GetTagItem("db.response.status_code"))).IsEqualTo((ActivityStatusCode.Error, (object?)"ExpiredIteratorException", "ExpiredIteratorException"));
    }

    [Test]
    public async Task GetRecordsAsync_Listener_RecordsTheDurationWithoutATable()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Records":[]}"""));
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.CreateStreams(handler, serviceUrl: new Uri($"http://{host}:8000"));

        // Act
        await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        var tags = capture.Single().Tags;
        await Assert.That((tags.ContainsKey("db.collection.name"), tags["db.operation.name"], tags["db.system.name"], tags["server.port"])).IsEqualTo((false, (object?)"GetRecords", "aws.dynamodb", 8000));
    }

    [Test]
    public async Task ListStreamsAsync_ThrottledOnce_CountsTheRetryAndTheThrottleWithoutATable()
    {
        // Arrange
        var host = TestClient.UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json("""{"Streams":[]}"""));
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), serviceUrl: new Uri($"http://{host}:8000"), maxAttempts: 3, random: FixedRandom.Zero);

        // Act
        await client.ListStreamsAsync(TestClient.ListStreams());

        // Assert
        await Assert.That((counters.Of(TelemetryNames.RetriesMetric).Single().TagText, counters.Of(TelemetryNames.ThrottlesMetric).Single().TagText)).IsEqualTo((
            $"db.operation.name=ListStreams,error.type=ProvisionedThroughputExceededException,server.address={host},server.port=8000",
            $"db.operation.name=ListStreams,error.type=ProvisionedThroughputExceededException,server.address={host},server.port=8000"));
    }

    [Test]
    public async Task ListStreamsAsync_AttemptsExhausted_WarnsAndPassesTheErrorOn()
    {
        // Arrange
        // A table of its own keeps this retry out of the counter capture of the throttle test, which watches ListStreams calls without a table.
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = ServerError() };
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), loggerFactory: logs, maxAttempts: 2, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<InternalServerErrorException>(() => client.ListStreamsAsync(TestClient.ListStreams($"streams-{Guid.NewGuid():N}")));

        // Assert
        var warning = logs.Entries.Single(entry => entry.EventId.Id == 4);
        await Assert.That((warning.Category, warning.Message.StartsWith("DynamoDB ListStreams on table", StringComparison.Ordinal))).IsEqualTo(("DynaSharp.Streams.DynamoDbStreamsClient", true));
    }

    [Test]
    public async Task GetRecordsAsync_AttemptsExhausted_WarnsThatTheCallHasNoSingleTable()
    {
        // Arrange (GetRecords names no table at all, so "several tables" would be wrong)
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = ServerError() };
        using var client = TestClient.CreateStreams(handler, new ManualTimeProvider(), serviceUrl: new Uri($"http://{TestClient.UniqueHost()}:8000"), loggerFactory: logs, maxAttempts: 2, random: FixedRandom.Zero);
        await TestClient.CaptureAsync<InternalServerErrorException>(() => client.GetRecordsAsync(TestClient.GetRecords()));

        // Act
        var message = logs.Entries.Single(entry => entry.EventId.Id == 4).Message;

        // Assert
        await Assert.That(message).StartsWith("DynamoDB GetRecords on table (no single table) failed", StringComparison.Ordinal);
    }

    private static async Task<Activity> RunAsync(StubHandler handler, Func<DynamoDbStreamsClient, Task> call)
    {
        using var capture = new ActivityCapture();
        using var client = TestClient.CreateStreams(handler);
        await call(client);
        return capture.Single();
    }

    private static string AllTagText(Activity activity) => string.Join('|', activity.TagObjects.Select(tag => tag.Value is string[] values ? string.Join(',', values) : tag.Value?.ToString()));
}
