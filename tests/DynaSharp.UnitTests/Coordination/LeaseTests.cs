using System.Diagnostics;
using System.Net;
using DynaSharp.Coordination;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LeaseTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Heartbeat_EveryThirdOfTheLease_KeepsTheLeaseForLongerThanItLasts()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var versions = new HashSet<string> { bench.ItemOf("job")!.GetString("rvn")! };

        // Act
        for (var beat = 0; beat < 9; beat++)
        {
            await BeatAsync(bench, lease!);
            versions.Add(bench.ItemOf("job")!.GetString("rvn")!);
        }

        // Assert
        await Assert.That((lease!.Lost.IsCancellationRequested, versions.Count)).IsEqualTo((false, 10));
    }

    [Test]
    public async Task Heartbeat_BeforeAThirdOfTheLease_DoesNotRenew()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var version = bench.ItemOf("job")!.GetString("rvn");

        // Act
        bench.Clock.Advance(Period - TimeSpan.FromTicks(1));

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetString("rvn")).IsEqualTo(version);
    }

    [Test]
    public async Task Heartbeat_RenewFindsAnotherOwner_LosesTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        await Assert.That((lease!.Lost.IsCancellationRequested, lease.LossReason)).IsEqualTo((true, "taken_over"));
    }

    [Test]
    public async Task Heartbeat_LeaseLost_LogsAWarningAndRecordsTheLoss()
    {
        // Arrange
        using var bench = new LockHarness();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.losses");
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        var entry = bench.LockLogs.Single();
        await Assert.That((entry.Level, entry.EventId.Id, entry.Message, metrics.Single().TagText)).IsEqualTo(
            (LogLevel.Warning, 40, $"The lease on lock job in table {bench.TableName} is lost (taken_over).", $"db.collection.name={bench.TableName},dynasharp.lock.loss_reason=taken_over,dynasharp.lock.name=job"));
    }

    [Test]
    public async Task Heartbeat_AfterTheLoss_Stops()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");
        await BeatAsync(bench, lease!);
        var requests = bench.Handler.Requests.Count;

        // Act
        bench.Clock.Advance(Lease);

        // Assert
        await Assert.That((bench.Handler.Requests.Count, bench.Clock.ActiveTimers)).IsEqualTo((requests, 0));
    }

    [Test]
    public async Task Heartbeat_RenewFailsOnce_TriesAgainAtOnceAndKeepsTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        var entry = bench.LockLogs.Single();
        await Assert.That((lease!.Lost.IsCancellationRequested, entry.Level, entry.EventId.Id, entry.Message)).IsEqualTo(
            (false, LogLevel.Warning, 42, $"Renewing the lease on lock job in table {bench.TableName} failed with InternalServerError. It is tried once more at once."));
    }

    [Test]
    public async Task Heartbeat_RenewFailsTwice_LosesTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        await Assert.That((lease!.Lost.IsCancellationRequested, lease.LossReason)).IsEqualTo((true, "renew_failed"));
    }

    [Test]
    public async Task Heartbeat_TableDeleted_LosesTheLeaseWithoutTryingAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Fake.DeleteTable(bench.TableName);
        var requests = bench.Handler.Requests.Count;

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        await Assert.That((lease!.LossReason, bench.Handler.Requests.Count - requests)).IsEqualTo(("table_not_found", 1));
    }

    [Test]
    public async Task Lost_NoRenewalAnswersWithinTheLease_IsCancelledWhenTheLeaseRunsOut()
    {
        // Arrange (renewals hang until they are cancelled)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.HoldBack();
        bench.Clock.Advance(Period);
        var lostBefore = lease!.Lost.IsCancellationRequested;

        // Act
        bench.Clock.Advance(Lease - Period);

        // Assert
        await Assert.That((lostBefore, lease.Lost.IsCancellationRequested, lease.LossReason)).IsEqualTo((false, true, "expired"));
    }

    [Test]
    public async Task Lost_RenewedLate_LastsAWholeLeaseFromTheRenewal()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Period - TimeSpan.FromTicks(1));
        await lease!.RenewAsync();

        // Act (the heartbeat is held back, so the renewal above is the last one)
        bench.HoldBack();
        bench.Clock.Advance(Lease - TimeSpan.FromTicks(1));
        var heldBefore = !lease.Lost.IsCancellationRequested;
        bench.Clock.Advance(TimeSpan.FromTicks(1));

        // Assert
        await Assert.That((heldBefore, lease.Lost.IsCancellationRequested)).IsEqualTo((true, true));
    }

    [Test]
    public async Task RenewAsync_Held_RenewsTheRecordVersion()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var version = bench.ItemOf("job")!.GetString("rvn");

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((renewed, bench.ItemOf("job")!.GetString("rvn") == version)).IsEqualTo((true, false));
    }

    [Test]
    public async Task RenewAsync_TakenOver_ReturnsFalse()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((renewed, lease.Lost.IsCancellationRequested)).IsEqualTo((false, true));
    }

    [Test]
    public async Task RenewAsync_AlreadyLost_ReturnsFalseWithoutARequest()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");
        await lease!.RenewAsync();
        var requests = bench.Handler.Requests.Count;

        // Act
        var renewed = await lease.RenewAsync();

        // Assert
        await Assert.That((renewed, bench.Handler.Requests.Count)).IsEqualTo((false, requests));
    }

    [Test]
    public async Task RenewAsync_Released_ReturnsFalse()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await lease!.DisposeAsync();

        // Act
        var renewed = await lease.RenewAsync();

        // Assert
        await Assert.That(renewed).IsFalse();
    }

    [Test]
    public async Task RenewAsync_TableDeleted_ThrowsResourceNotFoundExceptionAndLosesTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Fake.DeleteTable(bench.TableName);

        // Act
        var act = () => lease!.RenewAsync();

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
        await Assert.That(lease!.LossReason).IsEqualTo("table_not_found");
    }

    [Test]
    public async Task RenewAsync_FailsTwice_ThrowsAndLosesTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Still not.", HttpStatusCode.InternalServerError));

        // Act
        var act = () => lease!.RenewAsync();

        // Assert
        await Assert.That(act).Throws<DynamoDbException>().WithMessageContaining("Still not.", StringComparison.Ordinal);
        await Assert.That(lease!.LossReason).IsEqualTo("renew_failed");
    }

    [Test]
    public async Task RenewAsync_CallerCancels_ThrowsAndKeepsTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var act = () => lease!.RenewAsync(stop.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
        await Assert.That(lease!.Lost.IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task RenewAsync_LeaseRunsOutDuringTheCall_ReturnsFalse()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.HoldBack(() => bench.Clock.Advance(Lease));

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((renewed, lease.LossReason)).IsEqualTo((false, "expired"));
    }

    [Test]
    public async Task RenewAsync_AnswerArrivesAfterTheLease_ReturnsFalse()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Intercept = (_, _) =>
        {
            bench.Clock.Advance(Lease);
            return Task.FromResult<StubResponse?>(StubResponse.Json("{}"));
        };

        // Act
        var renewed = await lease!.RenewAsync();

        // Assert
        await Assert.That((renewed, lease.Lost.IsCancellationRequested)).IsEqualTo((false, true));
    }

    [Test]
    public async Task RenewAsync_AnswerArrivesAfterALaterRenewal_KeepsTheLease()
    {
        // Arrange (a renewal is held back while the heartbeat renews three times, then answers after its own lease would have run out)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        bench.Intercept = async (_, _) =>
        {
            if (first)
            {
                first = false;
                await held.Task;
            }

            return null;
        };
        var late = lease!.RenewAsync();
        for (var beat = 0; beat < 3; beat++)
        {
            await BeatAsync(bench, lease);
        }

        bench.Clock.Advance(TimeSpan.FromSeconds(5));

        // Act
        held.SetResult();
        var renewed = await late;

        // Assert
        await Assert.That((renewed, lease.Lost.IsCancellationRequested)).IsEqualTo((true, false));
    }

    [Test]
    public async Task RenewAsync_CallerCancelsAsTheLeaseRunsOut_Throws()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        using var stop = new CancellationTokenSource();
        bench.Intercept = async (_, cancellationToken) =>
        {
            bench.Clock.Advance(Lease);
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        };

        // Act
        var act = () => lease!.RenewAsync(stop.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RenewAsync_Renewed_RecordsASpanAndARenewal()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        using var capture = new ActivityCapture();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.renewals");

        // Act
        await lease!.RenewAsync();

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Renew");
        await Assert.That((ActivityCapture.Tags(span), metrics.Single().TagText)).IsEqualTo(
            ($"db.system.name=aws.dynamodb,db.collection.name={bench.TableName},dynasharp.lock.name=job", $"db.collection.name={bench.TableName},dynasharp.lock.name=job"));
    }

    [Test]
    public async Task RenewAsync_Fails_RecordsTheErrorOnTheSpanAndTheRenewal()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");
        using var capture = new ActivityCapture();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.renewals");

        // Act
        await lease!.RenewAsync();

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Renew");
        await Assert.That((span.Status, span.GetTagItem("error.type"), metrics.Single().Tags["error.type"])).IsEqualTo(
            (ActivityStatusCode.Error, (object?)"ConditionalCheckFailedException", (object?)"ConditionalCheckFailedException"));
    }

    [Test]
    public async Task DisposeAsync_Held_ReleasesTheLockAndKeepsTheFencingToken()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await lease!.DisposeAsync();

        // Assert
        var item = bench.ItemOf("job")!;
        await Assert.That((item.ContainsKey("owner"), item.ContainsKey("leaseMs"), item.GetNumber<long>("fence"), lease.Lost.IsCancellationRequested, lease.LossReason))
            .IsEqualTo((false, false, 1L, true, (string?)null));
    }

    [Test]
    public async Task DisposeAsync_Held_StopsTheHeartbeat()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await lease!.DisposeAsync();

        // Assert
        await Assert.That(bench.Clock.ActiveTimers).IsEqualTo(0);
    }

    [Test]
    public async Task DisposeAsync_TakenOver_LeavesTheNewHolder()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Steal("job", "thief");

        // Act
        await lease!.DisposeAsync();

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetString("owner")).IsEqualTo("thief");
    }

    [Test]
    public async Task DisposeAsync_Twice_ReleasesOnce()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await lease!.DisposeAsync();
        var requests = bench.Handler.Requests.Count;

        // Act
        await lease.DisposeAsync();

        // Assert
        await Assert.That(bench.Handler.Requests.Count).IsEqualTo(requests);
    }

    [Test]
    public async Task DisposeAsync_ReleaseFails_LogsAWarningInsteadOfThrowing()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Fake.DeleteTable(bench.TableName);
        using var capture = new ActivityCapture();

        // Act
        await lease!.DisposeAsync();

        // Assert
        var entry = bench.LockLogs.Single();
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Release");
        await Assert.That((entry.Level, entry.EventId.Id, entry.Message, span.Status)).IsEqualTo(
            (LogLevel.Warning, 43, $"Releasing the lease on lock job in table {bench.TableName} failed with ResourceNotFoundException. The lock is free once its lease runs out.", ActivityStatusCode.Error));
    }

    // Moves the clock to the next renewal of the heartbeat and waits until it is done.
    private static async Task BeatAsync(LockHarness bench, Lease lease)
    {
        var beat = lease.NextBeat;
        bench.Clock.Advance(Period);
        await beat;
    }
}
