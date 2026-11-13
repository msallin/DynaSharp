using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

// What FailOnDrift() does with the drift that IfNotExists() would report: it throws before anything waits or changes, or after time to live when only that differs.
public sealed partial class CreateTableCommandTests
{
    private const string GsiMissing = "The table Shop lacks the global index GSI1 over the partition key gsi1pk (String) and sort key gsi1sk (String). Queries of it fail until UpdateTable adds it.";

    private const string LsiMissing = "The table Shop lacks the local index LSI1 over the partition key pk (String) and sort key lsi1sk (String). A local index exists only if the table was created with it, so queries of it fail until the items move to a new table that has it.";

    [Test]
    public async Task SendAsync_FailOnDriftOverATableThatLacksIndexes_ThrowsWithEachBeforeAnyWaitOrTimeToLive()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{ByTracking}}]""")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var command = client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().FailOnDrift().WaitUntilActive().TimeToLive("expires");

        // Act
        var failure = await TestClient.CaptureAsync<TableDriftException>(async () => await command.SendAsync());

        // Assert
        await Assert.That((failure.TableName, Kinds(failure.Drift), Operations(handler))).IsEqualTo(("Shop", "IndexMissing:GSI1,IndexMissing:LSI1", "DescribeTable"));
    }

    [Test]
    public async Task SendAsync_FailOnDriftOverTwoDrifts_SaysBothInTheMessage()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{ByTracking}}]""")));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<TableDriftException>(async () => await client.Table<SchemaOrder>("Shop").CreateTable().FailOnDrift().SendAsync());

        // Assert
        await Assert.That(failure.Message).IsEqualTo($"The table Shop differs from what CreateTable() declares in 2 ways: {GsiMissing} {LsiMissing}");
    }

    [Test]
    public async Task SendAsync_FailOnDriftOverTimeToLiveOnAnotherAttribute_ThrowsAfterLookingAndChangesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(TimeToLive("ENABLED", "other")));
        using var client = TestClient.Create(handler, new ManualTimeProvider());

        // Act
        var failure = await TestClient.CaptureAsync<TableDriftException>(async () => await client.Table("Shop", "pk").CreateTable().IfNotExists().FailOnDrift().TimeToLive("expires").SendAsync());

        // Assert
        await Assert.That((Kinds(failure.Drift), failure.Message.StartsWith("The table Shop differs from what CreateTable() declares in 1 way: The table Shop has time to live on the attribute other", StringComparison.Ordinal), Operations(handler)))
            .IsEqualTo(("TimeToLiveDiffers:", true, "DescribeTable,DescribeTimeToLive"));
    }

    [Test]
    public async Task SendAsync_FailOnDriftWithoutIfNotExists_KeepsTheTableItFinds()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var result = await client.Table("Shop", "pk").CreateTable().FailOnDrift().SendAsync();

        // Assert
        await Assert.That((result.Created, Operations(handler))).IsEqualTo((false, "DescribeTable"));
    }

    [Test]
    public async Task SendAsync_FailOnDriftOverATableAsDeclared_ReturnsAndLogsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"GlobalSecondaryIndexes":[{{Gsi1}},{{ByTracking}}],"LocalSecondaryIndexes":[{{Lsi1}}]""")));
        using var logs = new CapturingLoggerFactory();
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Act
        var result = await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().FailOnDrift().SendAsync();

        // Assert
        await Assert.That((result.Drift.Count, logs.Entries.Count(static entry => entry.EventId.Id == 14))).IsEqualTo((0, 0));
    }

    [Test]
    public async Task SendAsync_FailOnDriftOverDrift_LogsNoWarning()
    {
        // Arrange (the exception says it all, so the warning of a drift that returns would say it twice)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Described($$"""{{OrderKey}},{{OrderAttributes}},"LocalSecondaryIndexes":[{{Lsi1}}]""")));
        using var logs = new CapturingLoggerFactory();
        using var client = TestClient.Create(handler, loggerFactory: logs);

        // Act
        await TestClient.CaptureAsync<TableDriftException>(async () => await client.Table<SchemaOrder>("Shop").CreateTable().IfNotExists().FailOnDrift().SendAsync());

        // Assert
        await Assert.That(logs.Entries.Count(static entry => entry.EventId.Id == 14)).IsEqualTo(0);
    }

    [Test]
    public async Task TableDriftException_OneDrift_KeepsWhatItWasGiven()
    {
        // Arrange
        TableDrift[] drift = [new(TableDriftKind.StreamDiffers, null, "The table Shop has no stream.")];

        // Act
        var exception = new TableDriftException("Shop", drift);

        // Assert
        await Assert.That((exception.TableName, ReferenceEquals(exception.Drift, drift), exception.Message, exception is InvalidOperationException))
            .IsEqualTo(("Shop", true, "The table Shop differs from what CreateTable() declares in 1 way: The table Shop has no stream.", true));
    }

    [Test]
    public async Task TableDriftException_NullTableName_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new TableDriftException(null!, [new(TableDriftKind.StreamDiffers, null, "d")]));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("tableName");
    }

    [Test]
    public async Task TableDriftException_NullDrift_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new TableDriftException("Shop", null!));

        // Assert
        await Assert.That(((ArgumentNullException)failure).ParamName).IsEqualTo("drift");
    }

    [Test]
    public async Task TableDriftException_NoDrift_Throws()
    {
        // Act
        var failure = TestClient.Catch(() => _ = new TableDriftException("Shop", []));

        // Assert
        await Assert.That((failure.GetType(), ((ArgumentException)failure).ParamName)).IsEqualTo((typeof(ArgumentException), "drift"));
    }

    private static string Kinds(IReadOnlyList<TableDrift> drift) => string.Join(",", drift.Select(static drift => $"{drift.Kind}:{drift.IndexName}"));
}
