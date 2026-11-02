using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Telemetry;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

// Counters, spans and the duration metric of calls that are retried. The meter and the activity source are global and tests run in
// parallel, so every test filters by a table name or a trace of its own.
public sealed class RetryTelemetryTests
{
    [Test]
    public async Task GetItemAsync_RetriedOnce_CountsOneRetry()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Select(measurement => measurement.Value).ToArray()).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task GetItemAsync_RetriedOnce_TagsTheRetryWithOperationTableAndReason()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().TagText).IsEqualTo(
            $"db.collection.name={table},db.operation.name=GetItem,error.type=ProvisionedThroughputExceededException,server.address=dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task PutItemAsync_RetriedOnce_TagsTheOperation()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = CreateClient(handler);

        // Act
        await client.PutItemAsync(TestClient.PutItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().Tags["db.operation.name"]).IsEqualTo("PutItem");
    }

    [Test]
    public async Task GetItemAsync_ThreeAttempts_CountsTwoRetries()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetItemAsync_ProxyAnswers503_TagsTheRetryWithTheCodeOfUnrecognizedResponses()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(ProxyUnavailable());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().Tags["error.type"]).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task GetItemAsync_NetworkFailure_TagsTheRetryWithTheExceptionType()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? throw new HttpRequestException("Connection reset") : Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().Tags["error.type"]).IsEqualTo("System.Net.Http.HttpRequestException");
    }

    [Test]
    public async Task GetItemAsync_Timeout_TagsTheRetryWithTimeout()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? throw new TimeoutException("The attempt timed out.") : Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().Tags["error.type"]).IsEqualTo("timeout");
    }

    [Test]
    public async Task GetItemAsync_ClockCorrection_CountsARetryWithTheErrorCode()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? SkewRejection(time.GetUtcNow().AddMinutes(15)) : Success());
        using var client = CreateClient(handler, time);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.RetriesMetric).Single().Tags["error.type"]).IsEqualTo("InvalidSignatureException");
    }

    [Test]
    public async Task GetItemAsync_Success_CountsNeitherRetryNorThrottle()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = Success() };
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That((counters.Of(TelemetryNames.RetriesMetric).Count, counters.Of(TelemetryNames.ThrottlesMetric).Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task GetItemAsync_ValidationError_CountsNeitherRetryNorThrottle()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ValidationException", "bad") };
        using var client = CreateClient(handler);
        await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Act
        var (retries, throttles) = (counters.Of(TelemetryNames.RetriesMetric).Count, counters.Of(TelemetryNames.ThrottlesMetric).Count);

        // Assert
        await Assert.That((retries, throttles)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task GetItemAsync_ThrottledOnce_CountsOneThrottle()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.ThrottlesMetric).Select(measurement => measurement.Value).ToArray()).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task GetItemAsync_ThrottledOnce_TagsTheThrottleWithOperationTableAndReason()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Rate of requests exceeds the allowed throughput."));
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.ThrottlesMetric).Single().TagText).IsEqualTo(
            $"db.collection.name={table},db.operation.name=GetItem,error.type=ThrottlingException,server.address=dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task GetItemAsync_ThrottledOnANonDefaultPort_TagsTheThrottleAndTheRetryWithServerAddressAndPort()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, serviceUrl: new Uri("http://emulator.test:8000"));

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That((counters.Of(TelemetryNames.ThrottlesMetric).Single().TagText, counters.Of(TelemetryNames.RetriesMetric).Single().TagText)).IsEqualTo((
            $"db.collection.name={table},db.operation.name=GetItem,error.type=ProvisionedThroughputExceededException,server.address=emulator.test,server.port=8000",
            $"db.collection.name={table},db.operation.name=GetItem,error.type=ProvisionedThroughputExceededException,server.address=emulator.test,server.port=8000"));
    }

    [Test]
    public async Task GetItemAsync_ThrottledOnEveryAttempt_CountsEveryThrottle()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler);

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Assert
        await Assert.That((counters.Of(TelemetryNames.ThrottlesMetric).Count, counters.Of(TelemetryNames.RetriesMetric).Count)).IsEqualTo((3, 2));
    }

    [Test]
    public async Task GetItemAsync_ThrottledWithRetriesSwitchedOff_CountsTheThrottleButNoRetry()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, maxAttempts: 1);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(table)));

        // Act
        var (throttles, retries) = (counters.Of(TelemetryNames.ThrottlesMetric).Count, counters.Of(TelemetryNames.RetriesMetric).Count);

        // Assert
        await Assert.That((throttles, retries)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task GetItemAsync_ServerError_CountsNoThrottle()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        await Assert.That(counters.Of(TelemetryNames.ThrottlesMetric).Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetItemAsync_Listener_RetryCounterIsMeasuredInRetries()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        var measurement = counters.Of(TelemetryNames.RetriesMetric).Single();
        await Assert.That((measurement.Unit, measurement.Description)).IsEqualTo(("{retry}", "Number of times DynamoDB client operations sent a request again after a failed attempt."));
    }

    [Test]
    public async Task GetItemAsync_Listener_ThrottleCounterIsMeasuredInThrottles()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table));

        // Assert
        var measurement = counters.Of(TelemetryNames.ThrottlesMetric).Single();
        await Assert.That((measurement.Unit, measurement.Description)).IsEqualTo(("{throttle}", "Number of throttling errors DynamoDB returned to client operations, whether or not the attempt was retried."));
    }

    [Test]
    public async Task GetItemAsync_KeyValue_IsNotInAnyCounterTag()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = MeasurementCapture<long>.ForTable(table, TelemetryNames.RetriesMetric, TelemetryNames.ThrottlesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem(table, key: "SECRET-KEY-VALUE"));

        // Assert
        var tags = counters.Of(TelemetryNames.RetriesMetric).Concat(counters.Of(TelemetryNames.ThrottlesMetric)).SelectMany(measurement => measurement.Tags.Values);
        await Assert.That(string.Join(',', tags)).DoesNotContain("SECRET");
    }

    [Test]
    public async Task GetItemAsync_RetriedThenSucceeded_RecordsOneSpanWithoutAnError()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert (Single fails if the retries had started more spans)
        await Assert.That(capture.Single().Status).IsEqualTo(ActivityStatusCode.Unset);
    }

    [Test]
    public async Task GetItemAsync_AttemptsExhausted_RecordsOneSpanThatFailsWithTheLastError()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Slow down."));
        using var client = CreateClient(handler);
        await TestClient.CaptureAsync<ThrottlingException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var activity = capture.Single();

        // Assert
        await Assert.That((activity.Status, activity.GetTagItem("error.type"))).IsEqualTo((ActivityStatusCode.Error, "ThrottlingException"));
    }

    [Test]
    public async Task GetItemAsync_RetryAfterAWait_RecordsTheDurationOfTheWholeOperation()
    {
        // Arrange (a wait of 100 ms between two attempts that take no time on the clock of the test)
        var time = new ManualTimeProvider();
        var table = UniqueTable();
        using var metric = MeasurementCapture<double>.ForTable(table, TelemetryNames.DurationMetric);
        using var handler = new StubHandler();
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        var call = client.GetItemAsync(TestClient.GetItem(table));

        // Act
        time.Advance(TimeSpan.FromMilliseconds(100));
        await call;

        // Assert (one measurement for the operation, not one per attempt)
        await Assert.That(metric.Single().Value).IsEqualTo(0.1);
    }
}
