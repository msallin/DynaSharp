using DynaSharp.Coordination.Testing;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;

namespace DynaSharp.UnitTests.Coordination;

// An acquire that waits at most a given time, on the clock of the client.
public sealed class LockTableWaitTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    [Arguments(0L)]
    [Arguments(-20_000_000L)]
    public async Task AcquireAsync_WaitNotPositive_ThrowsArgumentOutOfRangeException(long ticks)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        Func<Task> act = () => bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromTicks(ticks));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("wait");
    }

    [Test]
    public async Task AcquireAsync_WaitWithANameOutOfRange_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        Func<Task> act = () => bench.Locks.AcquireAsync(string.Empty, Lease, TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task AcquireAsync_InfiniteWaitOnAFreeLock_Acquires()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.AcquireAsync("job", Lease, Timeout.InfiniteTimeSpan);

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(1L);
    }

    [Test]
    public async Task AcquireAsync_HolderOutlastsTheWait_ThrowsTimeoutExceptionNamingTheLockAtTheEndOfTheWait()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        var start = bench.Clock.GetTimestamp();

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => bench.RunAsync(bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromSeconds(15)), TimeSpan.FromSeconds(1)));

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Acquire" && (string?)activity.GetTagItem("db.collection.name") == bench.TableName);
        await Assert.That((exception.Message.Contains($"lock job of table {bench.TableName}", StringComparison.Ordinal), bench.Clock.GetElapsedTime(start) >= TimeSpan.FromSeconds(15), span.GetTagItem("error.type")))
            .IsEqualTo((true, true, (object?)"System.TimeoutException"));
    }

    [Test]
    public async Task AcquireAsync_HolderOutlastsTheWait_SaysAnotherHolderHadIt()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => bench.RunAsync(bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromSeconds(15)), TimeSpan.FromSeconds(1)));

        // Assert
        await Assert.That(exception.Message).IsEqualTo($"The lock job of table {bench.TableName} was not acquired within a wait of 00:00:15: another holder had it at the last try.");
    }

    [Test]
    public async Task AcquireAsync_LastTryFindsTheLockFreeAfterItsWriteLost_SaysSo()
    {
        // Arrange (every write meets a transaction on the free lock item, and takes a second)
        using var bench = new LockHarness();
        bench.Intercept = (request, _) =>
        {
            if (request.Operation != "UpdateItem")
            {
                return Task.FromResult<StubResponse?>(null);
            }

            bench.Clock.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult<StubResponse?>(StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item"));
        };

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromSeconds(2)));

        // Assert
        await Assert.That(exception.Message)
            .IsEqualTo($"The lock job of table {bench.TableName} was not acquired within a wait of 00:00:02: the last try found it free, but another write to its item came first.");
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-20_000_000L)]
    public async Task TryAcquireAsync_WaitNotPositive_ThrowsArgumentOutOfRangeException(long ticks)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        Func<Task> act = () => bench.Locks.TryAcquireAsync("job", Lease, TimeSpan.FromTicks(ticks));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("wait");
    }

    [Test]
    public async Task TryAcquireAsync_WaitWithANameOutOfRange_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        Func<Task> act = () => bench.Locks.TryAcquireAsync(string.Empty, Lease, TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task TryAcquireAsync_HolderOutlastsTheWait_ReturnsNullAtTheEndOfTheWait()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        var start = bench.Clock.GetTimestamp();

        // Act
        var lease = await bench.RunAsync(bench.Locks.TryAcquireAsync("job", Lease, TimeSpan.FromSeconds(15)), TimeSpan.FromSeconds(1));

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire" && (string?)activity.GetTagItem("db.collection.name") == bench.TableName);
        await Assert.That((lease, bench.Clock.GetElapsedTime(start) >= TimeSpan.FromSeconds(15), span.GetTagItem("dynasharp.lock.acquired"), span.GetTagItem("error.type")))
            .IsEqualTo(((DynaSharp.Coordination.Lease?)null, true, (object?)false, (object?)null));
    }

    [Test]
    public async Task TryAcquireAsync_WaitOfALittleMoreThanALease_TakesOverACrashedHolderInANewLockTable()
    {
        // Arrange (a process that runs once: its LockTable is new, and the holder crashed before it started)
        using var bench = new LockHarness();
        var holder = await bench.Locks.SimulateHolderAsync("job", Lease);
        var run = bench.OtherProcess(bench.Clock);

        // Act
        await using var lease = await bench.RunAsync(run.TryAcquireAsync("job", Lease, Lease + TimeSpan.FromSeconds(10)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(holder + 1);
    }

    [Test]
    public async Task TryAcquireAsync_InfiniteWaitOnAFreeLock_Acquires()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease, Timeout.InfiniteTimeSpan);

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(1L);
    }

    [Test]
    public async Task AcquireAsync_WaitOfALittleMoreThanALease_TakesOverACrashedHolder()
    {
        // Arrange
        using var bench = new LockHarness();
        var holder = await bench.Locks.SimulateHolderAsync("job", Lease);

        // Act
        await using var lease = await bench.RunAsync(bench.Locks.AcquireAsync("job", Lease, Lease + TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(holder + 1);
    }

    [Test]
    public async Task AcquireAsync_InfiniteWaitOnAHeldLock_WaitsUntilItCanTakeItOver()
    {
        // Arrange
        using var bench = new LockHarness();
        var holder = await bench.Locks.SimulateHolderAsync("job", Lease);

        // Act
        await using var lease = await bench.RunAsync(bench.Locks.AcquireAsync("job", Lease, Timeout.InfiniteTimeSpan), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(holder + 1);
    }

    [Test]
    public async Task AcquireAsync_WaitShorterThanAPoll_ThrowsAtTheEndOfTheWait()
    {
        // Arrange (the holder is polled every 10 s; the wait of 2 s ends before the first poll)
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        var start = bench.Clock.GetTimestamp();

        // Act
        var exception = await TestClient.CaptureAsync<TimeoutException>(() => bench.RunAsync(bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromSeconds(2)), TimeSpan.FromSeconds(1)));

        // Assert
        await Assert.That((exception.Message.Contains("lock job of table", StringComparison.Ordinal), bench.Clock.GetElapsedTime(start) < TimeSpan.FromSeconds(10))).IsEqualTo((true, true));
    }

    [Test]
    public async Task AcquireAsync_LockReleasedDuringALongWait_TakesItAtTheNextPoll()
    {
        // Arrange (the contender found the lock held and polls every 10 s, well within its wait of a minute)
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        var start = bench.Clock.GetTimestamp();
        var waiting = bench.Locks.AcquireAsync("job", Lease, TimeSpan.FromMinutes(1));
        await bench.Locks.SimulateReleaseAsync("job");

        // Act
        await using var lease = await bench.RunAsync(waiting, TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(bench.Clock.GetElapsedTime(start) < TimeSpan.FromMinutes(1)).IsTrue();
    }
}
