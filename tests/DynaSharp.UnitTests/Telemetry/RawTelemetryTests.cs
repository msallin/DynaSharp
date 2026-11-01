using System.Diagnostics;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Telemetry;

// Spans and metrics of SendRawAsync. The library cannot read a table from JSON it does not parse, so such a call has the operation, the
// server and the region, and no table. The meter and the activity source are global and tests run in parallel, so every metric test
// filters by a host of its own and every span test by a trace of its own.
public sealed class RawTelemetryTests
{
    [Test]
    public async Task SendRawAsync_Listener_NamesTheSpanAfterTheServiceAndTheRealOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Assert
        await Assert.That(activity.OperationName).IsEqualTo("DynamoDB.DescribeLimits");
    }

    [Test]
    public async Task SendRawAsync_Listener_ActivityIsAClientSpan()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Assert
        await Assert.That(activity.Kind).IsEqualTo(ActivityKind.Client);
    }

    [Test]
    public async Task SendRawAsync_Listener_TagsTheOperationTheServiceAndTheMethod()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.SendRawAsync("DescribeEndpoints", "{}"u8.ToArray()));

        // Assert
        await Assert.That(ActivityCapture.Tags(activity)).Contains("db.operation.name=DescribeEndpoints").And.Contains("rpc.service=DynamoDB").And.Contains("rpc.method=DescribeEndpoints");
    }

    [Test]
    public async Task SendRawAsync_Listener_HasNoTableTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => client.SendRawAsync("DescribeTable", """{"TableName":"Secret"}"""u8.ToArray()));

        // Assert
        await Assert.That(ActivityCapture.Tags(activity)).DoesNotContain("db.collection.name").And.DoesNotContain("Secret");
    }

    [Test]
    public async Task SendRawAsync_ListenerAndError_MarksTheSpanAsFailedWithTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "No table."));

        // Act
        var activity = await ActivityCapture.RunAsync(handler, client => TestClient.CaptureAsync<ResourceNotFoundException>(() => client.SendRawAsync("DescribeTable", "{}"u8.ToArray())));

        // Assert
        await Assert.That((activity.Status, ActivityCapture.Tags(activity))).Satisfies(result => result.Status == ActivityStatusCode.Error && result.Item2.Contains("error.type=ResourceNotFoundException", StringComparison.Ordinal));
    }

    [Test]
    public async Task SendRawAsync_Streams_NamesTheSpanAfterTheStreamsServiceAndTheRealOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var capture = new ActivityCapture();
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator-secret"}"""u8.ToArray());

        // Assert
        await Assert.That(capture.Single().OperationName).IsEqualTo("DynamoDB Streams.GetRecords");
    }

    [Test]
    public async Task SendRawAsync_Streams_TagsTheStreamsService()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var capture = new ActivityCapture();
        using var client = TestClient.CreateStreams(handler);

        // Act
        await client.SendRawAsync("GetRecords", """{"ShardIterator":"iterator-secret"}"""u8.ToArray());

        // Assert
        await Assert.That(ActivityCapture.Tags(capture.Single())).Contains("rpc.service=DynamoDB Streams").And.DoesNotContain("iterator-secret");
    }

    [Test]
    public async Task SendRawAsync_Listener_RecordsTheDurationWithTheRealOperationAndNoTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri($"https://{host}"));

        // Act
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        var tags = capture.Single().Tags;
        await Assert.That((tags["db.operation.name"], tags.ContainsKey("db.collection.name"))).IsEqualTo(("DescribeLimits", false));
    }

    [Test]
    public async Task SendRawAsync_ErrorResponse_RecordsTheDurationWithTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("AccessDeniedException", "No."));
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri($"https://{host}"));
        await TestClient.CaptureAsync<AccessDeniedException>(() => client.SendRawAsync("DescribeLimits", "{}"u8.ToArray()));

        // Act
        var tags = capture.Single().Tags;

        // Assert
        await Assert.That((tags["db.operation.name"], tags["error.type"])).IsEqualTo(("DescribeLimits", "AccessDeniedException"));
    }

    [Test]
    public async Task SendRawAsync_ThrottledOnce_CountsTheRetryUnderTheRealOperation()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json("{}"));
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<long>.ForServer(host, TelemetryNames.RetriesMetric);
        using var client = CreateClient(handler, serviceUrl: new Uri($"https://{host}"));

        // Act
        await client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        await Assert.That(capture.Single().Tags["db.operation.name"]).IsEqualTo("DescribeLimits");
    }

    [Test]
    public async Task SendRawAsync_Streams_RecordsTheDurationWithTheRealOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var host = TestClient.UniqueHost();
        using var capture = MeasurementCapture<double>.ForServer(host, TelemetryNames.DurationMetric);
        using var client = TestClient.CreateStreams(handler, serviceUrl: new Uri($"https://{host}"));

        // Act
        await client.SendRawAsync("GetRecords", "{}"u8.ToArray());

        // Assert
        await Assert.That(capture.Single().Tags["db.operation.name"]).IsEqualTo("GetRecords");
    }
}
