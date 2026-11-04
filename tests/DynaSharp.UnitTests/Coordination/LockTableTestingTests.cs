using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LockTableTestingTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task SimulateHolderAsync_FreeLock_WritesAHolderWithTheNextToken()
    {
        // Arrange
        using var bench = new LockHarness();
        await (await bench.Locks.TryAcquireAsync("job", Lease))!.DisposeAsync();

        // Act
        var token = await bench.Locks.SimulateHolderAsync("job", Lease, "pod-9");

        // Assert
        var item = bench.ItemOf("job")!;
        await Assert.That((token, item.GetNumber<long>("fence"), item.GetString("holder"), item.GetNumber<long>("leaseMs"), item.GetString("_type"), item.ContainsKey("owner")))
            .IsEqualTo((2L, 2L, (string?)"pod-9", 30_000L, (string?)"LOCK", true));
    }

    [Test]
    public async Task SimulateHolderAsync_LockHeldHere_TheLeaseIsLostAsTakenOver()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await bench.Locks.SimulateHolderAsync("job", Lease);

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((renewed, lease.LossReason)).IsEqualTo((false, (string?)"taken_over"));
    }

    [Test]
    public async Task SimulateHolderAsync_NoLabel_RemovesTheLabelOfTheHolderBefore()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await bench.Locks.SimulateHolderAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.ContainsKey("holder")).IsFalse();
    }

    [Test]
    public async Task SimulateHolderAsync_HolderNeverRenews_IsTakenOverAfterALease()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task SimulateHolderAsync_TimeToLive_StampsTheExpiry()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { TimeToLiveAttribute = "expires" });

        // Act
        await bench.Locks.SimulateHolderAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetNumber<long>("expires")).IsEqualTo(bench.Clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds());
    }

    [Test]
    public async Task SimulateHolderAsync_LeaseOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.SimulateHolderAsync("job", TimeSpan.Zero);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("leaseDuration");
    }

    [Test]
    public async Task SimulateHolderAsync_NullLocks_ThrowsArgumentNullException()
    {
        // Arrange
        LockTable? locks = null;

        // Act
        var act = () => locks!.SimulateHolderAsync("job", Lease);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("locks");
    }

    [Test]
    public async Task SimulateReleaseAsync_LockHeldHere_FreesItAndTheNextAcquireRaisesTheToken()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await bench.Locks.SimulateReleaseAsync("job");

        // Act
        await using var next = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((next?.FencingToken, await lease!.RenewAsync(), lease.LossReason)).IsEqualTo(((long?)2L, false, (string?)"taken_over"));
    }

    [Test]
    public async Task SimulateReleaseAsync_NoItem_WritesAFreeLock()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await bench.Locks.SimulateReleaseAsync("job");

        // Assert
        var item = bench.ItemOf("job")!;
        await Assert.That((item.GetString("_type"), item.ContainsKey("owner"), item.ContainsKey("fence"))).IsEqualTo(((string?)"LOCK", false, false));
    }

    [Test]
    [Arguments("release")]
    [Arguments("delete")]
    public async Task Simulate_NullLocks_ThrowsArgumentNullException(string helper)
    {
        // Arrange
        LockTable? locks = null;

        // Act
        var act = () => helper == "release" ? locks!.SimulateReleaseAsync("job") : locks!.SimulateDeleteAsync("job");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("locks");
    }

    [Test]
    [Arguments("release")]
    [Arguments("delete")]
    public async Task Simulate_NoName_ThrowsArgumentException(string helper)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => helper == "release" ? bench.Locks.SimulateReleaseAsync(string.Empty) : bench.Locks.SimulateDeleteAsync(string.Empty);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task SimulateDeleteAsync_LockHeldHere_TheLeaseIsLostAsDeletedAndTheTokenStartsAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await bench.Locks.SimulateDeleteAsync("job");
        await lease!.RenewAsync();

        // Act
        await using var next = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease.LossReason, next?.FencingToken)).IsEqualTo(((string?)"deleted", (long?)1L));
    }
}
