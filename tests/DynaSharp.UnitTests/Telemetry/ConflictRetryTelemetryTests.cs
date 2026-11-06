using DynaSharp.Telemetry;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging;
using static DynaSharp.UnitTests.TestClient;

namespace DynaSharp.UnitTests.Telemetry;

// What RetryOnConflictAsync reports. The meter is global and tests run in parallel, so every test filters by a table name or a host of its own.
public sealed class ConflictRetryTelemetryTests
{
    private const string SecretKey = "USER#secret-5d1a3e9b";

    [Test]
    public async Task RetryOnConflictAsync_RunThatLost_CountsARetryPerRepeatedRun()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await PutAsync(client, table);

        // Assert
        await Assert.That((counters.Of(TelemetryNames.ConflictRetriesMetric).Sum(static measurement => measurement.Value), counters.Of(TelemetryNames.ConflictsExhaustedMetric).Count)).IsEqualTo((2L, 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_RunThatLost_TagsTheRetryWithOperationTableAndReason()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await PutAsync(client, table);

        // Assert
        await Assert.That(counters.Of(TelemetryNames.ConflictRetriesMetric).Single().TagText).IsEqualTo(
            $"db.collection.name={table},db.operation.name=PutItem,error.type=ConditionalCheckFailedException,server.address=dynamodb.eu-central-2.amazonaws.com");
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRunLoses_CountsTheRetriesAndOneExhausted()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed());
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await CaptureAsync<ConditionalCheckFailedException>(() => PutAsync(client, table, maxAttempts: 3));

        // Assert
        await Assert.That((counters.Of(TelemetryNames.ConflictRetriesMetric).Count, counters.Of(TelemetryNames.ConflictsExhaustedMetric).Single().TagText)).IsEqualTo((
            2,
            $"db.collection.name={table},db.operation.name=PutItem,error.type=ConditionalCheckFailedException,server.address=dynamodb.eu-central-2.amazonaws.com"));
    }

    [Test]
    public async Task RetryOnConflictAsync_Instruments_HaveTheirUnits()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed());
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await CaptureAsync<ConditionalCheckFailedException>(() => PutAsync(client, table, maxAttempts: 2));

        // Assert
        await Assert.That((counters.Of(TelemetryNames.ConflictRetriesMetric).Single().Unit, counters.Of(TelemetryNames.ConflictsExhaustedMetric).Single().Unit)).IsEqualTo(("{retry}", "{conflict}"));
    }

    [Test]
    public async Task RetryOnConflictAsync_TransactionOnTwoTables_NamesTheOperationAndNoTable()
    {
        // Arrange (a transaction has no single table, so the host tells the measurements of this test apart)
        var host = UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.ConflictRetriesMetric);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.TransactionConflict));
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = Create(handler, serviceUrl: new Uri($"https://{host}"), random: FixedRandom.Zero);
        var (first, second) = (client.Table(UniqueTable(), "pk"), client.Table(UniqueTable(), "pk"));

        // Act
        await client.RetryOnConflictAsync(async ct => await client.TransactWrite().Put(first, new Item { ["pk"] = "a" }).Put(second, new Item { ["pk"] = "b" }).SendAsync(ct));

        // Assert
        await Assert.That(counters.Single().TagText).IsEqualTo($"db.operation.name=TransactWriteItems,error.type=TransactionCanceledException,server.address={host}");
    }

    [Test]
    public async Task RetryOnConflictAsync_ExceptionNotFromAResponse_SaysThatTheOperationIsUnknown()
    {
        // Arrange (a delegate can throw one itself, and that has no operation)
        var host = UniqueHost();
        using var counters = MeasurementCapture<long>.ForServer(host, TelemetryNames.ConflictRetriesMetric, TelemetryNames.ConflictsExhaustedMetric);
        using var loggers = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler();
        using var client = Create(handler, serviceUrl: new Uri($"https://{host}"), loggerFactory: loggers, random: FixedRandom.Zero);

        // Act
        await CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync<int>(static _ => throw new ConditionalCheckFailedException("Made here."), maxAttempts: 2));

        // Assert
        await Assert.That((counters.All.Count(static measurement => measurement.Tags["db.operation.name"] is "(unknown operation)"), loggers.Entries.Single().Message)).IsEqualTo((
            2,
            "DynamoDB (unknown operation) on table (no single table) lost to another writer with ConditionalCheckFailedException on run 2 of RetryOnConflictAsync, the last one allowed. The error is passed on to the caller."));
    }

    [Test]
    public async Task RetryOnConflictAsync_NoConflict_CountsNothing()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await PutAsync(client, table);

        // Assert
        await Assert.That(counters.All.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetryOnConflictAsync_OtherFailure_CountsNothing()
    {
        // Arrange
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ValidationException", "The expression does not parse."));
        using var client = Create(handler, random: FixedRandom.Zero);

        // Act
        await CaptureAsync<ValidationException>(() => PutAsync(client, table));

        // Assert
        await Assert.That(counters.All.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetryOnConflictAsync_ConflictThatRetryOnDoesNotName_CountsNothing()
    {
        // Arrange (it surfaces like any other failure, so nothing gave up)
        var table = UniqueTable();
        using var counters = Capture(table);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed());
        using var client = Create(handler, random: FixedRandom.Zero);
        var shop = client.Table(table, "pk");

        // Act
        await CaptureAsync<ConditionalCheckFailedException>(() => client.RetryOnConflictAsync(async ct => await shop.Put(new Item { ["pk"] = SecretKey }).IfNotExists().SendAsync(ct), ConflictKind.TransactionConflict));

        // Assert
        await Assert.That(counters.All.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetryOnConflictAsync_OneRunThatLoses_ReportsNothing()
    {
        // Arrange (one run asks for no retry, so nothing gave up, as with one attempt of a call)
        var table = UniqueTable();
        using var counters = Capture(table);
        using var loggers = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed());
        using var client = Create(handler, loggerFactory: loggers, random: FixedRandom.Zero);

        // Act
        await CaptureAsync<ConditionalCheckFailedException>(() => PutAsync(client, table, maxAttempts: 1));

        // Assert
        await Assert.That((counters.All.Count, loggers.Entries.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task RetryOnConflictAsync_EveryRunLoses_LogsOneWarningWithoutKeysOrValues()
    {
        // Arrange
        var table = UniqueTable();
        using var loggers = new CapturingLoggerFactory(LogLevel.Debug);
        using var handler = new StubHandler(_ => StubResponse.ConditionalCheckFailed());
        using var client = Create(handler, loggerFactory: loggers, random: FixedRandom.Zero);

        // Act
        var failure = await CaptureAsync<ConditionalCheckFailedException>(() => PutAsync(client, table, maxAttempts: 3));

        // Assert (the exception holds the key by now, and that must stop at the exception)
        var warning = loggers.Entries.Single(static entry => entry.Level >= LogLevel.Warning);
        await Assert.That((warning.Level, warning.EventId.Id, warning.Message, failure.Key!.GetString("pk"), warning.Message.Contains(SecretKey, StringComparison.Ordinal))).IsEqualTo((
            LogLevel.Warning,
            8,
            $"DynamoDB PutItem on table {table} lost to another writer with ConditionalCheckFailedException on run 3 of RetryOnConflictAsync, the last one allowed. The error is passed on to the caller.",
            SecretKey,
            false));
    }

    [Test]
    public async Task RetryOnConflictAsync_LaterRunGetsThrough_LogsNoWarning()
    {
        // Arrange
        var table = UniqueTable();
        using var loggers = new CapturingLoggerFactory(LogLevel.Warning);
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.ConditionalCheckFailed());
        handler.Enqueue(StubResponse.Json("{}"));
        using var client = Create(handler, loggerFactory: loggers, random: FixedRandom.Zero);

        // Act
        await PutAsync(client, table);

        // Assert
        await Assert.That(loggers.Entries.Count).IsEqualTo(0);
    }

    private static MeasurementCapture<long> Capture(string table) =>
        MeasurementCapture<long>.ForTable(table, TelemetryNames.ConflictRetriesMetric, TelemetryNames.ConflictsExhaustedMetric);

    private static Task<WriteResult<Item>> PutAsync(DynamoDbClient client, string table, int maxAttempts = 5)
    {
        var shop = client.Table(table, "pk");
        return client.RetryOnConflictAsync(async ct => await shop.Put(new Item { ["pk"] = SecretKey, ["secret"] = "value-5d1a3e9b" }).IfNotExists().SendAsync(ct), maxAttempts: maxAttempts);
    }
}
