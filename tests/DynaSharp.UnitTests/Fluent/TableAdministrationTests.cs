using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class TableAdministrationTests
{
    private const string Active = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE"}}""";
    private const string Creating = """{"Table":{"TableName":"Shop","TableStatus":"CREATING"}}""";
    private const string Backfilling = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"Ready","IndexStatus":"ACTIVE"},{"IndexName":"ByCity","IndexStatus":"CREATING","Backfilling":true}]}}""";
    private const string IndexesActive = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"Ready","IndexStatus":"ACTIVE"},{"IndexName":"ByCity","IndexStatus":"ACTIVE"}]}}""";
    private const string OneOfTheOrderIndexes = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"}]}}""";
    private const string AllOfTheOrderIndexes = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByTracking","IndexStatus":"ACTIVE"}]}}""";

    private static readonly TimeSpan Forever = TimeSpan.FromDays(30);

    [Test]
    public async Task DescribeTableAsync_Table_AsksForItsName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DescribeTableAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Single().Operation, handler.Requests.Single().BodyText)).IsEqualTo((TableStatus.Active, "DescribeTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task DescribeTableAsync_ResponseWithoutATable_ReturnsTheNameAndNoStatus()
    {
        // Arrange (the service model requires nothing of a response)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DescribeTableAsync();

        // Assert
        await Assert.That((description.TableName, description.TableStatus)).IsEqualTo(("Shop", TableStatus.Unknown));
    }

    [Test]
    public async Task DeleteTableAsync_ResponseWithoutADescription_ReturnsTheNameAndNoStatus()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DeleteTableAsync();

        // Assert
        await Assert.That((description.TableName, description.TableStatus)).IsEqualTo(("Shop", TableStatus.Unknown));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ResponseWithoutATable_KeepsWaiting()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("{}"));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That((await waiting).TableStatus).IsEqualTo(TableStatus.Active);
    }

    [Test]
    public async Task DescribeTableAsync_Missing_ThrowsThatException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await shop.DescribeTableAsync());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task DeleteTableAsync_Table_AsksForItsNameAndReturnsTheDescriptionWithoutWaiting()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Shop","TableStatus":"DELETING"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DeleteTableAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Single().Operation, handler.Requests.Single().BodyText)).IsEqualTo((TableStatus.Deleting, "DeleteTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task DeleteTableAsync_ViewOfAnEntity_DeletesTheTable()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Shop"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table<SchemaOrder>("Shop").DeleteTableAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo("""{"TableName":"Shop"}""");
    }

    [Test]
    public async Task DeleteTableAsync_Missing_ThrowsThatException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await shop.DeleteTableAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("DeleteTable");
    }

    [Test]
    public async Task DeleteTableAsync_DisposedClient_ThrowsNamingTheClient()
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        client.Dispose();

        // Act
        var failure = await TestClient.CaptureAsync<ObjectDisposedException>(async () => await shop.DeleteTableAsync());

        // Assert
        await Assert.That((failure.ObjectName, handler.Requests.Count)).IsEqualTo((typeof(DynamoDbClient).FullName!, 0));
    }

    [Test]
    public async Task DeleteTableAsync_AnyTable_HasTheSpanOfTheOperation()
    {
        // Arrange
        using var capture = new ActivityCapture();
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Shop"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Shop", "pk").DeleteTableAsync();

        // Assert
        await Assert.That(capture.All.Single().OperationName).IsEqualTo("DynamoDB.DeleteTable");
    }

    [Test]
    public async Task WaitUntilActiveAsync_Active_LooksOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").WaitUntilActiveAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 1));
    }

    [Test]
    public async Task WaitUntilActiveAsync_Creating_PollsOnTheClockOfTheClientWithGrowingDelays()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        for (var i = 0; i < 9; i++)
        {
            handler.Enqueue(StubResponse.Json(Creating));
        }

        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        var delays = await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That(string.Join(",", delays.Select(static delay => delay.TotalMilliseconds))).IsEqualTo("100,200,400,800,1600,3200,5000,5000,5000");
    }

    [Test]
    public async Task WaitUntilActiveAsync_NeverActive_ThrowsATimeoutAtTheBound()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, time);
        var bound = TimeSpan.FromSeconds(1);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync(bound).AsTask();
        var delays = await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var failure = await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert (the last delay is cut to the bound, and the table gets one last look at it)
        await Assert.That((string.Join(",", delays.Select(static delay => delay.TotalMilliseconds)), handler.Requests.Count, failure.InnerException)).IsEqualTo(("100,200,400,300", 5, null));
    }

    [Test]
    public async Task WaitUntilActiveAsync_NeverActive_NamesTheTableAndItsLastStatus()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.TableName, failure.LastStatus, failure.PendingIndex, failure.InnerException, failure.Message)).IsEqualTo(("Shop", (TableStatus?)TableStatus.Creating, null, null, "The table Shop and its indexes were not ACTIVE after 00:00:00: the table was Creating."));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ActiveTableWithABackfillingIndex_KeepsWaitingUntilItIsActive()
    {
        // Arrange (a table reads ACTIVE while an index it was given backfills)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Backfilling));
        handler.Enqueue(StubResponse.Json(Backfilling));
        handler.Enqueue(StubResponse.Json(IndexesActive));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var description = await waiting;

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes[1].IndexStatus, handler.Requests.Count)).IsEqualTo((IndexStatus.Active, 3));
    }

    [Test]
    public async Task WaitUntilActiveAsync_DeclaredIndexNotListedYet_KeepsWaitingUntilItIs()
    {
        // Arrange (an index UpdateTable just added may not be listed yet, and what is listed is all active)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Active));
        handler.Enqueue(StubResponse.Json(OneOfTheOrderIndexes));
        handler.Enqueue(StubResponse.Json(AllOfTheOrderIndexes));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table<SchemaOrder>("Shop").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var description = await waiting;

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes.Count, handler.Requests.Count)).IsEqualTo((2, 3));
    }

    [Test]
    public async Task WaitUntilActiveAsync_DeclaredIndexNotListedForMoreThanFiveLooks_KeepsWaiting()
    {
        // Arrange (unlike CreateTable, a wait of a table cannot know whether an UpdateTable that adds the index is still to come, so it does not end early)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        for (var look = 0; look < 8; look++)
        {
            handler.Enqueue(StubResponse.Json(OneOfTheOrderIndexes));
        }

        handler.Enqueue(StubResponse.Json(AllOfTheOrderIndexes));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table<SchemaOrder>("Shop").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var description = await waiting;

        // Assert
        await Assert.That((description.GlobalSecondaryIndexes.Count, handler.Requests.Count)).IsEqualTo((2, 9));
    }

    [Test]
    public async Task WaitUntilActiveAsync_DeclaredIndexNotListedAtTheEnd_NamesItAndSaysSo()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(OneOfTheOrderIndexes));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var orders = client.Table<SchemaOrder>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await orders.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex, failure.Message))
            .IsEqualTo(((TableStatus?)TableStatus.Active, (string?)"ByTracking", "The table Shop and its indexes were not ACTIVE after 00:00:00: the table was Active, and the index ByTracking was not listed."));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ListedIndexBehindAndDeclaredOneNotListed_NamesTheListedOne()
    {
        // Arrange (a status says more than a name that is missing)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"CREATING"}]}}"""));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var orders = client.Table<SchemaOrder>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await orders.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That(failure.Message).IsEqualTo("The table Shop and its indexes were not ACTIVE after 00:00:00: the table was Active, and the index GSI1 was Creating.");
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableCreatingAndIndexNotListed_SaysBoth()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var orders = client.Table<SchemaOrder>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await orders.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex)).IsEqualTo(((TableStatus?)TableStatus.Creating, (string?)"GSI1"));
    }

    [Test]
    public async Task WaitUntilActiveAsync_EntityWithALocalIndexOnly_WaitsForNoIndex()
    {
        // Arrange (a local index exists from the creation of the table, and DescribeTable lists it elsewhere)
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table<SchemaReading>("Shop").WaitUntilActiveAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Count)).IsEqualTo((TableStatus.Active, 1));
    }

    [Test]
    public async Task WaitUntilActiveAsync_AnotherEntityOfTheTable_WaitsForWhatItDeclares()
    {
        // Arrange (the view knows its own indexes, and those of the entity it came from are not listed here)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableStatus":"ACTIVE","GlobalSecondaryIndexes":[{"IndexName":"GSI1","IndexStatus":"ACTIVE"},{"IndexName":"ByEmail","IndexStatus":"ACTIVE"}]}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table<SchemaOrder>("Shop").Of<SchemaCustomer>().WaitUntilActiveAsync();

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WaitUntilActiveAsync_IndexStillBackfillingAtTheEnd_NamesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Backfilling));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex, failure.Message)).IsEqualTo(((TableStatus?)TableStatus.Active, (string?)"ByCity", "The table Shop and its indexes were not ACTIVE after 00:00:00: the table was Active, and the index ByCity was Creating."));
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableAndIndexBothCreating_SaysBoth()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Table":{"TableStatus":"CREATING","GlobalSecondaryIndexes":[{"IndexName":"ByCity","IndexStatus":"CREATING"}]}}"""));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That((failure.LastStatus, failure.PendingIndex)).IsEqualTo(((TableStatus?)TableStatus.Creating, (string?)"ByCity"));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ResponseWithoutATableToTheEnd_HasNoKnownStatus()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That(failure.LastStatus).IsEqualTo(TableStatus.Unknown);
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableGoneAtTheLastLook_HasNoStatus()
    {
        // Arrange (only the last look counts)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json(Creating));
        handler.DefaultResponse = StubResponse.DynamoDbError("ResourceNotFoundException", "Gone.");
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync(TimeSpan.FromSeconds(1)).AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var failure = await TestClient.CaptureAsync<TableNotActiveException>(() => waiting);

        // Assert
        await Assert.That((failure.LastStatus, failure.InnerException is ResourceNotFoundException, failure.Message)).IsEqualTo(((TableStatus?)null, true, "The table Shop and its indexes were not ACTIVE after 00:00:01: the table was not found."));
    }

    [Test]
    public async Task WaitUntilActiveAsync_CatchOfTheBaseType_StillCatchesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TimeoutException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That(failure).IsTypeOf<TableNotActiveException>();
    }

    [Test]
    public async Task WaitUntilActiveAsync_PollRunsOutOfTheAttemptTimeout_ThrowsThePlainType()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            arrived.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return StubResponse.Json(Active);
        });
        using var client = TestClient.Create(handler, time, attemptTimeout: TimeSpan.FromSeconds(30));
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        await arrived.Task;

        // Act
        time.Advance(TimeSpan.FromSeconds(30));

        // Assert
        await Assert.That(async () => await waiting).ThrowsExactly<TimeoutException>();
    }

    [Test]
    public async Task Constructor_NoTableName_Throws()
    {
        // Arrange
        string tableName = null!;

        // Act
        var failure = TestClient.Catch(() => _ = new TableNotActiveException("m", tableName, null, null));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task WaitUntilActiveAsync_LessThanAMillisecondLeft_WaitsAMillisecondOnTheClock()
    {
        // Arrange (Task.Delay truncates to milliseconds, so without a floor the clock stands still; the stub ends the spin, which would hang the test)
        var time = new ManualTimeProvider();
        var calls = 0;
        using var handler = new StubHandler(_ => ++calls > 100 ? throw new InvalidOperationException("The wait spins.") : StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, time);
        var bound = TimeSpan.FromMilliseconds(100.5);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync(bound).AsTask();
        var delays = await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var failure = await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert
        await Assert.That((string.Join(",", delays.Select(static delay => delay.TotalMilliseconds)), calls, failure.InnerException)).IsEqualTo(("100,1", 3, null));
    }

    [Test]
    public async Task WaitUntilActiveAsync_DefaultBound_IsFiveMinutes()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var started = time.GetUtcNow();
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert
        await Assert.That(time.GetUtcNow() - started).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task WaitUntilActiveAsync_ZeroTimeout_LooksOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<TimeoutException>(async () => await shop.WaitUntilActiveAsync(TimeSpan.Zero));

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WaitUntilActiveAsync_NegativeTimeout_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(async () => await shop.WaitUntilActiveAsync(Timeout.InfiniteTimeSpan));

        // Assert
        await Assert.That((failure.ParamName, handler.Requests.Count)).IsEqualTo(("timeout", 0));
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableNotThereYet_KeepsWaiting()
    {
        // Arrange: DescribeTable right after CreateTable can miss the new table
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        handler.Enqueue(StubResponse.Json(Creating));
        handler.Enqueue(StubResponse.Json(Active));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync().AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);

        // Assert
        await Assert.That((await waiting).TableStatus).IsEqualTo(TableStatus.Active);
    }

    [Test]
    public async Task WaitUntilActiveAsync_TableNeverThere_TimesOutWithTheCause()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync(TimeSpan.FromSeconds(1)).AsTask();
        await AdvanceThroughDelaysAsync(waiting, time, Forever);
        var failure = await TestClient.CaptureAsync<TimeoutException>(() => waiting);

        // Assert
        await Assert.That((failure.InnerException is ResourceNotFoundException, failure.Message.Contains("Shop", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task WaitUntilActiveAsync_OtherError_Surfaces()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("AccessDeniedException", "No."));
        using var client = TestClient.Create(handler, new ManualTimeProvider());
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<AccessDeniedException>(async () => await shop.WaitUntilActiveAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WaitUntilActiveAsync_CancelledWhileItWaits_Throws()
    {
        // Arrange
        var time = new ManualTimeProvider();
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler(_ => StubResponse.Json(Creating));
        using var client = TestClient.Create(handler, time);

        // Act
        var waiting = client.Table("Shop", "pk").WaitUntilActiveAsync(cancellation.Token).AsTask();
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => waiting);

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, handler.Requests.Count)).IsEqualTo((true, 1));
    }
}
