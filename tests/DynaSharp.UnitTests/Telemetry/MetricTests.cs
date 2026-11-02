using System.Net;
using DynaSharp.Requests;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

public sealed class MetricTests
{
    [Test]
    public async Task GetItemAsync_Listener_RecordsTheElapsedSecondsOfTheClock()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ =>
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            return StubResponse.Json(TestClient.GetItemResponseJson);
        });
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(0.25);
    }

    [Test]
    public async Task GetItemAsync_Listener_UnitIsSeconds()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(capture.Single().Unit).IsEqualTo("s");
    }

    [Test]
    public async Task GetItemAsync_Listener_InstrumentHasADescription()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(capture.Single().Description).IsEqualTo("Duration of DynamoDB client operations.");
    }

    [Test]
    public async Task OperationDuration_Always_AdvisesTheBucketBoundariesOfTheDatabaseConventions()
    {
        // Arrange (the conventions advise these boundaries, in seconds, so exporters that honor the advice use them)

        // Act
        var boundaries = DynaSharpTelemetry.OperationDuration.Advice?.HistogramBucketBoundaries;

        // Assert
        await Assert.That(boundaries).IsEquivalentTo([0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10]);
    }

    [Test]
    public async Task GetItemAsync_Success_TagsAreLowCardinality()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(string.Join(',', capture.Single().Tags.OrderBy(tag => tag.Key).Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo(
            $"db.collection.name={table},db.operation.name=GetItem,db.system.name=aws.dynamodb,server.address=dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task PutItemAsync_Success_TagsTheOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.PutItemAsync(TestClient.PutItem(table));

        // Assert
        await Assert.That(capture.Single().Tags["db.operation.name"]).IsEqualTo("PutItem");
    }

    [Test]
    public async Task DescribeTableAsync_Success_TagsTheOperation()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableName":"Users"}}"""));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.DescribeTableAsync(TestClient.DescribeTable(table));

        // Assert
        await Assert.That(capture.Single().Tags["db.operation.name"]).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task GetItemAsync_EndpointWithPort_TagsThePort()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, serviceUrl: new Uri("http://localhost:8000"));

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(capture.Single().Tags["server.port"]).IsEqualTo(8000);
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_TagsTheErrorCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "slow down"));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(capture.Single().Tags["error.type"]).IsEqualTo("ProvisionedThroughputExceededException");
    }

    [Test]
    public async Task GetItemAsync_DynamoDbError_TagsTheResponseStatusCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "slow down"));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(capture.Single().Tags["db.response.status_code"]).IsEqualTo("ProvisionedThroughputExceededException");
    }

    [Test]
    public async Task GetItemAsync_UnrecognizedResponse_HasNoResponseStatusCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html></html>", HttpStatusCode.ServiceUnavailable));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(capture.Single().Tags.ContainsKey("db.response.status_code")).IsFalse();
    }

    [Test]
    public async Task GetItemAsync_UnrecognizedResponse_TagsTheDocumentedCode()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("<html></html>", HttpStatusCode.ServiceUnavailable));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(capture.Single().Tags["error.type"]).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task GetItemAsync_NetworkFailure_StillRecordsTheDuration()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ =>
        {
            time.Advance(TimeSpan.FromSeconds(2));
            throw new HttpRequestException("Connection refused");
        });
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, time);

        // Act
        await TestClient.CaptureAsync<HttpRequestException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(capture.Single().Value).IsEqualTo(2);
    }

    [Test]
    public async Task GetItemAsync_Timeout_TagsTimeout()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var attemptTimeout = TimeSpan.FromSeconds(30);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json("{}");
        });
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler, time, attemptTimeout: attemptTimeout);
        var call = client.GetItemAsync(TestClient.GetItem(table));
        await arrived.Task;
        time.Advance(attemptTimeout);

        // Act
        await TestClient.CaptureAsync<TimeoutException>(() => call);

        // Assert
        await Assert.That(capture.Single().Tags["error.type"]).IsEqualTo("timeout");
    }

    [Test]
    public async Task GetItemAsync_KeyValue_IsNotInAnyTag()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        var table = UniqueTable();
        using var capture = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var client = TestClient.Create(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table, key: "SECRET-KEY-VALUE"));

        // Assert
        await Assert.That(string.Join(',', capture.Single().Tags.Values)).DoesNotContain("SECRET");
    }
}
