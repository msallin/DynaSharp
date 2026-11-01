using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class TableAdministrationTests
{
    private const string Active = """{"Table":{"TableName":"Shop","TableStatus":"ACTIVE"}}""";
    private const string Creating = """{"Table":{"TableName":"Shop","TableStatus":"CREATING"}}""";

    private static readonly TimeSpan Forever = TimeSpan.FromDays(30);

    [Test]
    public async Task DescribeAsync_Table_AsksForItsName()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(Active));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DescribeAsync();

        // Assert
        await Assert.That((description.TableStatus, handler.Requests.Single().Operation, handler.Requests.Single().BodyText)).IsEqualTo((TableStatus.Active, "DescribeTable", """{"TableName":"Shop"}"""));
    }

    [Test]
    public async Task DescribeAsync_ResponseWithoutATable_ReturnsTheNameAndNoStatus()
    {
        // Arrange (the service model requires nothing of a response)
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        var description = await client.Table("Shop", "pk").DescribeAsync();

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
    public async Task DescribeAsync_Missing_ThrowsThatException()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ResourceNotFoundException", "Gone."));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ResourceNotFoundException>(async () => await shop.DescribeAsync());

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
