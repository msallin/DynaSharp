using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Coordination;

/// <summary>
/// The lease of <c>DynaSharp.Coordination</c> against each backend: what the conditional writes of acquire, renew and release leave in the item, and
/// that <c>IsHeld</c> lets a transaction commit only while the lease holds the lock. Two <see cref="LockTable"/> instances stand for two processes.
/// The leases last long enough that no heartbeat or takeover depends on the speed of the backend.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class LockTableTests(Backend backend)
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task TryAcquireAsync_HeldByAnotherProcess_ReturnsNull()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await using var held = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);

        // Act
        var second = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);

        // Assert
        await Assert.That((held is not null, second)).IsEqualTo((true, (Lease?)null));
    }

    [Test]
    public async Task TryAcquireAsync_AfterTheHolderReleased_AcquiresWithTheNextFencingToken()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var first = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);
        await first!.DisposeAsync();

        // Act
        await using var second = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);

        // Assert
        await Assert.That((first.FencingToken, second!.FencingToken)).IsEqualTo((1L, 2L));
    }

    [Test]
    public async Task DisposeAsync_Held_KeepsTheItemAndItsFencingTokenWithoutAnOwner()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var lease = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);

        // Act
        await lease!.DisposeAsync();

        // Assert
        var item = await sandbox.ReadAsync("LOCK#nightly-report", "LOCK");
        await Assert.That((item!.ContainsKey("owner"), item.ContainsKey("leaseMs"), item.GetNumber<long>("fence"), item.GetString("_type"))).IsEqualTo((false, false, 1L, "LOCK"));
    }

    [Test]
    public async Task RenewAsync_Held_WritesANewRecordVersion()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await using var lease = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);
        var before = (await sandbox.ReadAsync("LOCK#nightly-report", "LOCK"))!.GetString("rvn");

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        var after = (await sandbox.ReadAsync("LOCK#nightly-report", "LOCK"))!.GetString("rvn");
        await Assert.That((renewed, after != before)).IsEqualTo((true, true));
    }

    [Test]
    public async Task AcquireAsync_HolderReleasesWhileAnotherWaits_HandsTheLockOn()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var first = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", TimeSpan.FromSeconds(1));
        var waiting = new LockTable(sandbox.Shop).AcquireAsync("nightly-report", TimeSpan.FromSeconds(1));

        // Act
        await first!.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        await Assert.That(second.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task IsHeld_LeaseHeld_CommitsTheWrite()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await using var lease = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);

        // Act
        await sandbox.Client.TransactWrite().IsHeld(lease!).Put(sandbox.Shop, Report(lease!)).SendAsync();

        // Assert
        await Assert.That((await sandbox.ReadAsync("REPORT", "2026-11-04"))!.GetNumber<long>("fence")).IsEqualTo(1L);
    }

    [Test]
    public async Task IsHeld_TakenOverByAnotherProcess_CancelsTheTransactionAtTheCheck()
    {
        // Arrange (another process took the lock over, which writes its own owner and a higher token)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await using var lease = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);
        await sandbox.Shop.Update("LOCK#nightly-report", "LOCK").Set("owner", "another-process").Add("fence", 1L).SendAsync();

        TransactMarker check = default;

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(async () =>
            await sandbox.Client.TransactWrite().IsHeld(lease!, out check).Put(sandbox.Shop, Report(lease!)).SendAsync());

        // Assert
        await Assert.That((failure.ReasonFor(check), await sandbox.ReadAsync("REPORT", "2026-11-04"))).IsEqualTo((TransactionFailure.ConditionalCheckFailed, (Item?)null));
    }

    [Test]
    public async Task TryAcquireAsync_HolderSeenAndReleased_ReadsAndAcquires()
    {
        // Arrange (the second try of the contender reads first, because it has seen the holder)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var contender = new LockTable(sandbox.Shop);
        var first = await new LockTable(sandbox.Shop).TryAcquireAsync("nightly-report", Lease);
        var seen = await contender.TryAcquireAsync("nightly-report", Lease);
        await first!.DisposeAsync();

        // Act
        await using var second = await contender.TryAcquireAsync("nightly-report", Lease);

        // Assert
        await Assert.That((seen, second?.FencingToken)).IsEqualTo(((Lease?)null, (long?)2L));
    }

    [Test]
    public async Task TryAcquireAsync_Options_WriteTheHolderLabelAndTheExpiry()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var locks = LockTable.For(sandbox.Shop, new() { HolderLabel = "pod-1", TimeToLiveAttribute = "expires" });

        // Act
        await using var lease = await locks.TryAcquireAsync("nightly-report", Lease);

        // Assert
        var item = (await sandbox.ReadAsync("LOCK#nightly-report", "LOCK"))!;
        await Assert.That((item.GetString("holder"), item.GetNumber<long>("expires") > DateTimeOffset.UtcNow.AddDays(29).ToUnixTimeSeconds())).IsEqualTo(((string?)"pod-1", true));
    }

    [Test]
    public async Task SimulateHolderAsync_LockHeld_IsHeldFailsAndTheLeaseIsLost()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var locks = new LockTable(sandbox.Shop);
        await using var lease = await locks.TryAcquireAsync("nightly-report", Lease);
        var token = await locks.SimulateHolderAsync("nightly-report", Lease, "elsewhere");

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((token, renewed, lease.Ended.IsCancellationRequested)).IsEqualTo((2L, false, true));
    }

    [Test]
    public async Task SimulateDeleteAsync_LockReleased_TheNextTokenIsOne()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var locks = new LockTable(sandbox.Shop);
        await (await locks.TryAcquireAsync("nightly-report", Lease))!.DisposeAsync();
        await locks.SimulateReleaseAsync("nightly-report");
        await locks.SimulateDeleteAsync("nightly-report");

        // Act
        await using var lease = await locks.TryAcquireAsync("nightly-report", Lease);

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(1L);
    }

    private static Item Report(Lease lease) => new() { ["pk"] = "REPORT", ["sk"] = "2026-11-04", ["fence"] = lease.FencingToken };
}
