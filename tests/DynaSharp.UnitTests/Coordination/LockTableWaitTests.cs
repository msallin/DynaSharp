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
}
