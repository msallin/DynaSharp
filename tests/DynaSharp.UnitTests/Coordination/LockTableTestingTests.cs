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

    [Test]
    public async Task GetStateAsync_LockHeld_SaysWhoHoldsItAndTheToken()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        var state = await bench.Locks.GetStateAsync("job");

        // Assert
        await Assert.That(state).IsEqualTo(new LockState(IsHeld: true, HolderLabel: "pod-1", LeaseDuration: Lease, FencingToken: 1));
    }

    [Test]
    public async Task GetStateAsync_LockReleased_IsFreeAndKeepsTheToken()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });
        await (await bench.Locks.TryAcquireAsync("job", Lease))!.DisposeAsync();

        // Act
        var state = await bench.Locks.GetStateAsync("job");

        // Assert
        await Assert.That(state).IsEqualTo(new LockState(IsHeld: false, HolderLabel: null, LeaseDuration: null, FencingToken: 1));
    }

    [Test]
    public async Task GetStateAsync_HeldByAnotherProcess_SaysItsLabelAndLease()
    {
        // Arrange
        using var bench = new LockHarness(sortKey: false);
        await bench.Locks.SimulateHolderAsync("job", TimeSpan.FromSeconds(10), "another-pod");

        // Act
        var state = await bench.Locks.GetStateAsync("job");

        // Assert
        await Assert.That(state).IsEqualTo(new LockState(IsHeld: true, HolderLabel: "another-pod", LeaseDuration: TimeSpan.FromSeconds(10), FencingToken: 1));
    }

    [Test]
    public async Task GetStateAsync_NoItem_IsNull()
    {
        // Arrange
        using var bench = new LockHarness();
        await (await bench.Locks.TryAcquireAsync("job", Lease))!.DisposeAsync();
        await bench.Locks.SimulateDeleteAsync("job");

        // Act
        var state = await bench.Locks.GetStateAsync("job");

        // Assert
        await Assert.That(state).IsNull();
    }

    [Test]
    public async Task GetStateAsync_ItemAtTheKeyIsNoLock_ThrowsInvalidOperationException()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.Put(bench.TableName, new Item(bench.KeyOf("job")) { ["owner"] = "someone" });

        // Act
        var act = () => bench.Locks.GetStateAsync("job");

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("is not a lock item written by LockTable", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetStateAsync_NoName_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.GetStateAsync(string.Empty);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task GetStateAsync_NoLockTable_ThrowsArgumentNullException()
    {
        // Arrange
        LockTable locks = null!;

        // Act
        var act = () => locks.GetStateAsync("job");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("locks");
    }

    [Test]
    public async Task WaitForRefusalAsync_TryAcquireOfAHeldLock_Completes()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("job", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("job");

        // Act
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await refused.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        await Assert.That((lease, refused.IsCompletedSuccessfully)).IsEqualTo(((Lease?)null, true));
    }

    [Test]
    public async Task WaitForRefusalAsync_ContenderPollsALiveHolder_CompletesAtEachPoll()
    {
        // Arrange (the first try writes and finds the lock held, the next ones read it: each is a refusal)
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("job", Lease);
        var contender = bench.OtherProcess(bench.Clock);
        using var stop = new CancellationTokenSource();
        var first = contender.WaitForRefusalAsync("job");
        var acquire = contender.AcquireAsync("job", Lease, stop.Token);
        await first.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        var second = contender.WaitForRefusalAsync("job");

        // Act
        await bench.RunAsync(Completion(second), TimeSpan.FromSeconds(1));

        // Assert
        await stop.CancelAsync();
        await Assert.That(() => (Task)acquire).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task WaitForRefusalAsync_FreeLock_StaysOpen()
    {
        // Arrange
        using var bench = new LockHarness();
        var refused = bench.Locks.WaitForRefusalAsync("job");

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease is not null, refused.IsCompleted)).IsEqualTo((true, false));
    }

    [Test]
    public async Task WaitForRefusalAsync_RefusalOnAnotherLock_StaysOpen()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("other", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("job");

        // Act
        var lease = await bench.Locks.TryAcquireAsync("other", Lease);

        // Assert
        await Assert.That((lease is null, refused.IsCompleted)).IsEqualTo((true, false));
    }

    [Test]
    public async Task WaitForRefusalAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var bench = new LockHarness();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = () => bench.Locks.WaitForRefusalAsync("job", cancelled.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task WaitForRefusalAsync_NoName_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.WaitForRefusalAsync(string.Empty);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task WaitForRefusalAsync_NoLockTable_ThrowsArgumentNullException()
    {
        // Arrange
        LockTable locks = null!;

        // Act
        var act = () => locks.WaitForRefusalAsync("job");

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("locks");
    }

    [Test]
    [Arguments("SimulateHolderAsync")]
    [Arguments("SimulateReleaseAsync")]
    [Arguments("SimulateDeleteAsync")]
    [Arguments("GetStateAsync")]
    public async Task Helper_AnswerThatDoesNotComeAtOnce_ResumesWithoutTheContextOfTheCaller(string helper)
    {
        // Arrange
        using var bench = new LockHarness();
        var context = new CountingSynchronizationContext();
        var answer = bench.Hold(static _ => true);

        // Act
        var call = context.Start(() => helper switch
        {
            "SimulateHolderAsync" => bench.Locks.SimulateHolderAsync("job", Lease),
            "SimulateReleaseAsync" => bench.Locks.SimulateReleaseAsync("job"),
            "SimulateDeleteAsync" => bench.Locks.SimulateDeleteAsync("job"),
            _ => bench.Locks.GetStateAsync("job"),
        });
        answer.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    private static async Task<bool> Completion(Task task)
    {
        await task;
        return true;
    }
}
