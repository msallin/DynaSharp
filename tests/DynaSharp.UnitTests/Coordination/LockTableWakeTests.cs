using System.Text;
using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;

namespace DynaSharp.UnitTests.Coordination;

// A lease released in this process wakes the acquires of the same LockTable that wait for that lock, at once; the clock of the bench does not move unless a test says so,
// so an acquire that completes without a step of it was woken and not polled.
public sealed class LockTableWakeTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan Real = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AcquireAsync_HolderOfThisTableReleases_AcquiresWithoutWaitingForThePoll()
    {
        // Arrange
        using var bench = new LockHarness();
        var holder = await bench.Locks.AcquireAsync("tour", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var waiting = bench.Locks.AcquireAsync("tour", Lease);
        await refused;
        var start = bench.Clock.GetTimestamp();

        // Act
        await holder.DisposeAsync();
        await using var lease = await waiting.WaitAsync(Real, TimeProvider.System);

        // Assert
        await Assert.That((lease.FencingToken, bench.Clock.GetElapsedTime(start))).IsEqualTo((2L, TimeSpan.Zero));
    }

    [Test]
    public async Task AcquireAsync_ReleaseBetweenARefusedTryAndItsWait_IsNotLost()
    {
        // Arrange (the answer of the try is taken while the lock is held, and handed over only after the release)
        using var bench = new LockHarness();
        var holder = await bench.Locks.AcquireAsync("tour", Lease);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var once = 0;
        bench.Intercept = async (request, _) =>
        {
            if (!IsAcquireWrite(request) || Interlocked.Exchange(ref once, 1) == 1)
            {
                return null;
            }

            var refusal = bench.Fake.Answer(request);
            asked.TrySetResult();
            await answer.Task;
            return refusal;
        };
        var waiting = bench.Locks.AcquireAsync("tour", Lease);
        await asked.Task.WaitAsync(Real, TimeProvider.System);
        var start = bench.Clock.GetTimestamp();

        // Act
        await holder.DisposeAsync();
        answer.SetResult();
        await using var lease = await waiting.WaitAsync(Real, TimeProvider.System);

        // Assert
        await Assert.That((lease.FencingToken, bench.Clock.GetElapsedTime(start))).IsEqualTo((2L, TimeSpan.Zero));
    }

    [Test]
    public async Task AcquireAsync_SeveralWaitersAndReleases_HandsTheLockOnWithoutAPoll()
    {
        // Arrange (three consumers of one process contend for one lock, as three topics for one tour)
        using var bench = new LockHarness();
        var holder = await bench.Locks.AcquireAsync("tour", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var first = bench.Locks.AcquireAsync("tour", Lease);
        await refused;
        refused = bench.Locks.WaitForRefusalAsync("tour");
        var second = bench.Locks.AcquireAsync("tour", Lease);
        await refused;
        var start = bench.Clock.GetTimestamp();

        // Act
        await holder.DisposeAsync();
        var next = await Task.WhenAny(first, second).WaitAsync(Real, TimeProvider.System);
        await (await next).DisposeAsync();
        var last = await (next == first ? second : first).WaitAsync(Real, TimeProvider.System);
        await last.DisposeAsync();

        // Assert
        await Assert.That((last.FencingToken, bench.Clock.GetElapsedTime(start), bench.Locks.WaitingNames)).IsEqualTo((3L, TimeSpan.Zero, 0));
    }

    [Test]
    public async Task TryAcquireAsync_WaitWoken_RecordsTheWakeupOnTheSpanAndInTheMetric()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();
        using var counters = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.wakeups");
        var holder = await bench.Locks.AcquireAsync("tour", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var waiting = bench.Locks.TryAcquireAsync("tour", Lease, TimeSpan.FromMinutes(1));
        await refused;

        // Act
        await holder.DisposeAsync();
        await using var lease = await waiting.WaitAsync(Real, TimeProvider.System);

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire" && (string?)activity.GetTagItem("db.collection.name") == bench.TableName && activity.GetTagItem("dynasharp.lock.acquired") is true);
        await Assert.That((span.GetTagItem("dynasharp.lock.wakeup_count"), counters.Single().Value, counters.Single().Unit)).IsEqualTo(((object?)1, 1L, "{wakeup}"));
    }

    [Test]
    public async Task AcquireAsync_ReleaseOfAnotherLock_DoesNotWake()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("tour", Lease);
        var other = await bench.Locks.AcquireAsync("yard", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var waiting = bench.Locks.AcquireAsync("tour", Lease);
        await refused;
        await other.DisposeAsync();
        await bench.Locks.SimulateReleaseAsync("tour");
        var start = bench.Clock.GetTimestamp();

        // Act
        await using var lease = await bench.RunAsync(waiting, TimeSpan.FromSeconds(1));

        // Assert (a third of the lease is the poll)
        await Assert.That(bench.Clock.GetElapsedTime(start)).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task AcquireAsync_ReleaseInAnotherProcess_IsSeenByThePoll()
    {
        // Arrange
        using var bench = new LockHarness();
        var elsewhere = bench.OtherProcess(new ManualTimeProvider());
        var holder = await elsewhere.AcquireAsync("tour", Lease);
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var waiting = bench.Locks.AcquireAsync("tour", Lease);
        await refused;
        await holder.DisposeAsync();
        var start = bench.Clock.GetTimestamp();

        // Act
        await using var lease = await bench.RunAsync(waiting, TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That(bench.Clock.GetElapsedTime(start)).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task AcquireAsync_CancelledWhileItWaits_ThrowsAndLeavesNoWaitBehind()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.AcquireAsync("tour", Lease);
        using var cancellation = new CancellationTokenSource();
        var refused = bench.Locks.WaitForRefusalAsync("tour");
        var waiting = bench.Locks.AcquireAsync("tour", Lease, cancellation.Token);
        await refused;

        // Act
        await cancellation.CancelAsync();
        var failure = await TestClient.CaptureAsync<OperationCanceledException>(() => waiting.WaitAsync(Real, TimeProvider.System));

        // Assert
        await Assert.That((failure.CancellationToken == cancellation.Token, bench.Locks.WaitingNames)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task TryAcquireAsync_WaitOver_LeavesNoWaitBehind()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.SimulateHolderAsync("tour", Lease);

        // Act
        var lease = await bench.RunAsync(bench.Locks.TryAcquireAsync("tour", Lease, TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That((lease, bench.Locks.WaitingNames)).IsEqualTo(((Lease?)null, 0));
    }

    [Test]
    public async Task DisposeAsync_NobodyWaits_KeepsNoWait()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.AcquireAsync("tour", Lease);

        // Act
        await lease.DisposeAsync();

        // Assert
        await Assert.That(bench.Locks.WaitingNames).IsEqualTo(0);
    }

    [Test]
    public async Task Wake_NoSpan_RecordsOnlyTheMetric()
    {
        // Act
        var act = () => CoordinationTelemetry.Wake(null, "Locks-" + Guid.NewGuid().ToString("N"), 1);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    // The write of an acquire raises the fencing token with ADD; a release removes the owner, and a renewal does neither.
    private static bool IsAcquireWrite(RecordedRequest request) =>
        request.Operation == "UpdateItem" && Encoding.UTF8.GetString(request.Body.Span).Contains("ADD", StringComparison.Ordinal);
}
