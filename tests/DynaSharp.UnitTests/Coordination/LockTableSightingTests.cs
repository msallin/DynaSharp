using DynaSharp.Coordination.Testing;

namespace DynaSharp.UnitTests.Coordination;

// How long a lock table remembers a holder it saw: long enough for a retry to take a crashed holder over, not for ever.
public sealed class LockTableSightingTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task TryAcquireAsync_RetryWithinTenLeases_TakesOverTheHolderItSawBefore()
    {
        // Arrange: a try that gave up, and its retry five leases later, as a message handler retries.
        using var bench = new LockHarness();
        var holder = await bench.Locks.SimulateHolderAsync("job", Lease);
        _ = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease * 5);

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(holder + 1);
    }

    [Test]
    public async Task TryAcquireAsync_RetryAfterTenLeases_WatchesTheHolderForAWholeLeaseAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        _ = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease * 10);

        // Act
        await using var late = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        await using var after = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((late is null, after is null)).IsEqualTo((true, false));
    }

    [Test]
    public async Task TryAcquireAsync_ManyLocksGivenUp_DropsTheirSightingsOnceTheyAreTenLeasesOld()
    {
        // Arrange: a lock per job, each held, each tried once and given up.
        using var bench = new LockHarness();
        for (var job = 0; job < 20; job++)
        {
            await bench.Locks.SimulateHolderAsync($"tour-{job}", Lease);
            _ = await bench.Locks.TryAcquireAsync($"tour-{job}", Lease);
        }

        var before = bench.Locks.SightingCount;
        bench.Clock.Advance(Lease * 10);
        await bench.Locks.SimulateHolderAsync("tour-next", Lease);

        // Act
        _ = await bench.Locks.TryAcquireAsync("tour-next", Lease);

        // Assert
        await Assert.That((before, bench.Locks.SightingCount)).IsEqualTo((20, 1));
    }

    [Test]
    public async Task TryAcquireAsync_SightingsYoungerThanTenLeases_AreKeptBySweeps()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("first", Lease);
        _ = await bench.Locks.TryAcquireAsync("first", Lease);
        bench.Clock.Advance(Lease * 9);
        await bench.Locks.SimulateHolderAsync("second", Lease);

        // Act
        _ = await bench.Locks.TryAcquireAsync("second", Lease);

        // Assert
        await Assert.That(bench.Locks.SightingCount).IsEqualTo(2);
    }
}
