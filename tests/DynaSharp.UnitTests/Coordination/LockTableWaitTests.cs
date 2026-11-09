using DynaSharp.Coordination.Testing;
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
