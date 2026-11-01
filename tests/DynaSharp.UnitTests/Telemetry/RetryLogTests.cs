using DynaSharp.Requests;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Telemetry;

// The three messages of the construction (event ids 1 to 3) are tested in ClientLogTests; these are the ones that come later.
public sealed class RetryLogTests
{
    [Test]
    public async Task GetItemAsync_AttemptsExhausted_WarnsOnceAboutTheExhaustedRetries()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Act
        var entry = RequestEntries(logs).Single();

        // Assert
        await Assert.That((entry.EventId.Id, entry.Level, entry.Category)).IsEqualTo((4, LogLevel.Warning, "DynaSharp.DynamoDbClient"));
    }

    [Test]
    public async Task GetItemAsync_AttemptsExhausted_MessageNamesOperationTableErrorAndAttempts()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Act
        var message = RequestEntries(logs).Single().Message;

        // Assert
        await Assert.That(message).IsEqualTo(
            "DynamoDB GetItem on table Orders failed with ProvisionedThroughputExceededException on attempt 3, the last one allowed. The error is passed on to the caller.");
    }

    [Test]
    public async Task PutItemAsync_AttemptsExhausted_NamesTheOperation()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = ProxyUnavailable() };
        using var client = CreateClient(handler, loggerFactory: logs, maxAttempts: 2);
        await TestClient.CaptureAsync<DynamoDbException>(() => client.PutItemAsync(TestClient.PutItem("Orders")));

        // Act
        var message = RequestEntries(logs).Single().Message;

        // Assert
        await Assert.That(message).IsEqualTo("DynamoDB PutItem on table Orders failed with UnrecognizedResponse on attempt 2, the last one allowed. The error is passed on to the caller.");
    }

    [Test]
    public async Task GetItemAsync_RetriesSwitchedOff_LogsNothing()
    {
        // Arrange (nothing was used up, so there is nothing to warn about)
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs, maxAttempts: 1);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var entries = RequestEntries(logs);

        // Assert
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetItemAsync_ErrorThatIsNotRetried_LogsNothing()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = StubResponse.DynamoDbError("ValidationException", "bad") };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ValidationException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var entries = RequestEntries(logs);

        // Assert
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetItemAsync_SucceedsAfterRetries_LogsNothing()
    {
        // Arrange (a retry that works is not an event to investigate; the counters show it)
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(ServerError());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, loggerFactory: logs);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        var entries = RequestEntries(logs);

        // Assert
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetItemAsync_QuotaRunsOut_WarnsOnceThatTheQuotaIsUsedUp()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs, maxAttempts: int.MaxValue);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem("Orders")));

        // Act
        var entry = RequestEntries(logs).Single();

        // Assert
        await Assert.That((entry.EventId.Id, entry.Level, entry.Message)).IsEqualTo((
            5,
            LogLevel.Warning,
            "DynamoDB GetItem on table Orders failed with ProvisionedThroughputExceededException on attempt 101. The retry quota of the client is used up, so the request is not repeated and the error is passed on to the caller."));
    }

    [Test]
    public async Task GetItemAsync_ClockCorrected_WarnsOnceWithTheOffset()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? SkewRejection(time.GetUtcNow().AddMinutes(15)) : Success());
        using var client = CreateClient(handler, time, loggerFactory: logs);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        var entry = RequestEntries(logs).Single();

        // Assert
        await Assert.That((entry.EventId.Id, entry.Level, entry.Message)).IsEqualTo((
            6,
            LogLevel.Warning,
            "The local clock differs from the DynamoDB clock by 00:15:00. Requests are signed with the corrected time from now on."));
    }

    [Test]
    public async Task GetItemAsync_LocalClockAhead_WarnsWithANegativeOffset()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ => Interlocked.Increment(ref calls) == 1 ? SkewRejection(time.GetUtcNow().AddMinutes(-20)) : Success());
        using var client = CreateClient(handler, time, loggerFactory: logs);
        await client.GetItemAsync(TestClient.GetItem());

        // Act
        var entry = RequestEntries(logs).Single();

        // Assert
        await Assert.That(entry.Message).Contains("-00:20:00");
    }

    [Test]
    public async Task GetItemAsync_SkewResponseWithoutAnOffsetToLearn_LogsNothing()
    {
        // Arrange (the Date header agrees with the local clock)
        var logs = new CapturingLoggerFactory();
        var time = new ManualTimeProvider();
        using var handler = new StubHandler { DefaultResponse = SkewRejection(time.GetUtcNow()) };
        using var client = CreateClient(handler, time, loggerFactory: logs);
        await TestClient.CaptureAsync<DynamoDbException>(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        var entries = RequestEntries(logs);

        // Assert
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetItemAsync_KeyValue_IsNotInAnyRetryLog()
    {
        // Arrange
        var logs = new CapturingLoggerFactory();
        using var handler = new StubHandler { DefaultResponse = Throttled() };
        using var client = CreateClient(handler, loggerFactory: logs);
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(() => client.GetItemAsync(TestClient.GetItem(key: "SECRET-KEY-VALUE")));

        // Act
        var messages = string.Join('\n', RequestEntries(logs).Select(entry => entry.Message));

        // Assert
        await Assert.That(messages).DoesNotContain("SECRET");
    }

    private static List<CapturingLoggerFactory.LogEntry> RequestEntries(CapturingLoggerFactory logs) =>
        [.. logs.Entries.Where(entry => entry.EventId.Id > 3)];
}
