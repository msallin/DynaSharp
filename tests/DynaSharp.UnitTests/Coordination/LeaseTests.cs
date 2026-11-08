using System.Diagnostics;
using System.Net;
using System.Text;
using DynaSharp.Coordination;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LeaseTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);
    private static readonly AsyncLocal<string?> Marker = new();

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
        await Assert.That((lease!.Ended.IsCancellationRequested, versions.Count)).IsEqualTo((false, 10));
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
        await Assert.That((lease!.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((true, "taken_over"));
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
            (LogLevel.Warning, 40, $"The lease on lock job in table {bench.TableName} is lost (taken_over).", $"db.collection.name={bench.TableName},dynasharp.lock.loss_reason=taken_over"));
    }

    [Test]
    public async Task Heartbeat_ItemDeleted_LosesTheLeaseAsDeletedAndTheNextTokenIsOne()
    {
        // Arrange
        using var bench = new LockHarness();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.losses");
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Fake.Delete(bench.TableName, bench.KeyOf("job"));

        // Act
        await BeatAsync(bench, lease!);
        await using var next = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease!.LossReason, metrics.Single().TagText, bench.LockLogs.Single(static entry => entry.EventId.Id == 40).Message, next!.FencingToken)).IsEqualTo(
            ("deleted", $"db.collection.name={bench.TableName},dynasharp.lock.loss_reason=deleted", $"The lease on lock job in table {bench.TableName} is lost (deleted).", 1L));
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
    public async Task Heartbeat_RenewFails_TriesAgainAfterAWaitAndKeepsTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));
        var beat = lease!.NextBeat;

        // Act
        bench.Clock.Advance(Period);
        await bench.RunAsync(Completion(beat), TimeSpan.FromMilliseconds(50));

        // Assert
        var entry = bench.LockLogs.Single();
        await Assert.That((lease.Ended.IsCancellationRequested, entry.Level, entry.EventId.Id, entry.Message)).IsEqualTo(
            (false, LogLevel.Warning, 42, $"Renewing the lease on lock job in table {bench.TableName} failed with InternalServerError. It is tried again in 00:00:00.1000000, until the lease runs out."));
    }

    [Test]
    public async Task Heartbeat_RenewMeetsTheLeasesOwnTransaction_RenewsWithoutALog()
    {
        // Arrange (the holder's IsHeld transaction holds the lock item when the heartbeat renews)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var before = bench.ItemOf("job")!.GetString("rvn");
        bench.Handler.Enqueue(TransactionConflict());
        var beat = lease!.NextBeat;

        // Act
        bench.Clock.Advance(Period);
        await bench.RunAsync(Completion(beat), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That((lease.Ended.IsCancellationRequested, bench.ItemOf("job")!.GetString("rvn") != before, bench.LockLogs.Count)).IsEqualTo((false, true, 0));
    }

    [Test]
    public async Task Heartbeat_RenewKeepsFailing_TriesWithGrowingWaitsUntilTheLeaseRunsOut()
    {
        // Arrange (every renewal fails; the release at the end goes through)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Intercept = static (request, _) => Task.FromResult(
            Encoding.UTF8.GetString(request.Body.Span).Contains("REMOVE", StringComparison.Ordinal)
                ? null
                : StubResponse.DynamoDbError("InternalServerError", "Down.", HttpStatusCode.InternalServerError));

        // Act
        await bench.RunAsync(Completion(Task.Delay(Timeout.Infinite, lease!.Ended)), TimeSpan.FromMilliseconds(100));

        // Assert (100 ms, doubling to a sixth of the lease, 5 s: the lease outlasts many more than two failures)
        var waits = bench.LockLogs.Where(static entry => entry.EventId.Id == 42).Select(static entry => entry.Message[(entry.Message.IndexOf(" in 00:", StringComparison.Ordinal) + 4)..entry.Message.IndexOf(", until", StringComparison.Ordinal)]).Distinct();
        await Assert.That((lease.LossReason, string.Join(",", waits))).IsEqualTo(
            ("expired", "00:00:00.1000000,00:00:00.2000000,00:00:00.4000000,00:00:00.8000000,00:00:01.6000000,00:00:03.2000000,00:00:05"));
    }

    [Test]
    public async Task Heartbeat_AnswersSlowerThanAThirdOfTheLease_KeepTheLease()
    {
        // Arrange (each renewal is answered 12 s after it was sent, with a lease of 30 s: the next one is sent at once, a third after the last send)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var renewals = 0;
        bench.Intercept = (request, _) =>
        {
            if (request.Operation == "UpdateItem" && Interlocked.Increment(ref renewals) <= 5)
            {
                bench.Clock.Advance(TimeSpan.FromSeconds(12));
            }

            return Task.FromResult<StubResponse?>(null);
        };

        // Act
        var beat = lease!.NextBeat;
        bench.Clock.Advance(Period);
        while (Volatile.Read(ref renewals) < 6)
        {
            await beat.WaitAsync(TimeSpan.FromSeconds(10));
            beat = lease.NextBeat;
        }

        // Assert
        await Assert.That((lease.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((false, (string?)null));
    }

    [Test]
    public async Task Heartbeat_Renewal_IsARootSpanLinkedToTheAcquire()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();
        var trace = capture.ParentTraceId;
        var renewals = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DynaSharp",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => LinksTo(options.Links, trace) ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "Lock.Renew" && LinksTo(activity.Links, trace))
                {
                    renewals.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var acquire = capture.All.Single(static activity => activity.OperationName == "Lock.TryAcquire");

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        var renewal = renewals.Single();
        await Assert.That((renewal.ParentSpanId == default, renewal.TraceId == trace, renewal.Links.Single().Context.SpanId == acquire.SpanId)).IsEqualTo((true, false, true));
    }

    [Test]
    public async Task Heartbeat_Renewal_DoesNotSeeTheAsyncLocalsOfTheAcquire()
    {
        // Arrange
        using var bench = new LockHarness();
        Marker.Value = "caller";
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        string? seen = null;
        bench.Intercept = (request, _) =>
        {
            if (request.Operation == "UpdateItem")
            {
                seen ??= Marker.Value ?? "none";
            }

            return Task.FromResult<StubResponse?>(null);
        };

        // Act
        await BeatAsync(bench, lease!);

        // Assert
        await Assert.That(seen).IsEqualTo("none");
    }

    [Test]
    public async Task Constructor_FlowSuppressedByTheCaller_StartsTheHeartbeat()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var acquired = await bench.Locks.TryAcquireAsync("job", Lease);
        Lease lease;

        // Act
        using (ExecutionContext.SuppressFlow())
        {
            lease = new Lease(bench.Locks, "job", acquired!.Owner, 1, Lease, bench.Clock.GetTimestamp());
        }

        // Assert
        await using (lease)
        {
            await BeatAsync(bench, lease);
            await Assert.That(lease.Ended.IsCancellationRequested).IsFalse();
        }
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
        var lostBefore = lease!.Ended.IsCancellationRequested;

        // Act
        bench.Clock.Advance(Lease - Period);

        // Assert
        await Assert.That((lostBefore, lease.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((false, true, "expired"));
    }

    [Test]
    public async Task Heartbeat_RenewalInFlightWhenTheLeaseRunsOut_StopsWithoutTryingAgain()
    {
        // Arrange (the renewal has reached DynamoDB and hangs; only then does the lease run out, which cancels it)
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bench.Intercept = async (request, cancellationToken) =>
        {
            if (Encoding.UTF8.GetString(request.Body.Span).Contains("REMOVE", StringComparison.Ordinal))
            {
                return null;
            }

            arrived.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        };
        bench.Clock.Advance(Period);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stopped = lease!.NextBeat;

        // Act
        bench.Clock.Advance(Lease - Period);
        await stopped.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert (a cancelled renewal is not a failure to try again)
        await Assert.That((lease.LossReason, bench.LockLogs.Count(static entry => entry.EventId.Id == 42))).IsEqualTo(("expired", 0));
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
        var heldBefore = !lease.Ended.IsCancellationRequested;
        bench.Clock.Advance(TimeSpan.FromTicks(1));

        // Assert
        await Assert.That((heldBefore, lease.Ended.IsCancellationRequested)).IsEqualTo((true, true));
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
        await Assert.That((renewed, lease.Ended.IsCancellationRequested)).IsEqualTo((false, true));
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
    public async Task RenewAsync_Fails_ThrowsAndKeepsTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(StubResponse.DynamoDbError("InternalServerError", "Try again.", HttpStatusCode.InternalServerError));

        // Act
        var act = () => lease!.RenewAsync();

        // Assert
        await Assert.That(act).Throws<DynamoDbException>().WithMessageContaining("Try again.", StringComparison.Ordinal);
        await Assert.That((lease!.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((false, (string?)null));
    }

    [Test]
    public async Task RenewAsync_MeetsTheLeasesOwnTransaction_RenewsAfterAShortWait()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(TransactionConflict());

        // Act
        var renewed = await bench.RunAsync(lease!.RenewAsync(), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That((renewed, string.Join(",", bench.Handler.Requests.Select(static request => request.Operation)))).IsEqualTo((true, "UpdateItem,UpdateItem,UpdateItem"));
    }

    [Test]
    public async Task RenewAsync_MeetsATransactionOnEveryRun_ThrowsAndKeepsTheLease()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        for (var run = 0; run < 5; run++)
        {
            bench.Handler.Enqueue(TransactionConflict());
        }

        // Act
        var act = () => bench.RunAsync(lease!.RenewAsync(), TimeSpan.FromMilliseconds(100));

        // Assert
        await Assert.That(act).Throws<TransactionConflictException>();
        await Assert.That((lease!.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((false, (string?)null));
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
        await Assert.That(lease!.Ended.IsCancellationRequested).IsFalse();
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
        await Assert.That((renewed, lease.Ended.IsCancellationRequested)).IsEqualTo((false, true));
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
        await Assert.That((renewed, lease.Ended.IsCancellationRequested)).IsEqualTo((true, false));
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
            ($"db.system.name=aws.dynamodb,db.collection.name={bench.TableName},dynasharp.lock.name=job", $"db.collection.name={bench.TableName}"));
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
        await Assert.That((item.ContainsKey("owner"), item.ContainsKey("leaseMs"), item.GetNumber<long>("fence"), lease.Ended.IsCancellationRequested, lease.LossReason))
            .IsEqualTo((false, false, 1L, true, (string?)null));
    }

    [Test]
    public async Task RenewAsync_TimeToLive_MovesTheExpiryOn()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { TimeToLiveAttribute = "expires" });
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(TimeSpan.FromSeconds(5));

        // Act
        await lease!.RenewAsync();

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetNumber<long>("expires")).IsEqualTo(bench.Clock.GetUtcNow().AddDays(30).ToUnixTimeSeconds());
    }

    [Test]
    public async Task DisposeAsync_TimeToLive_StampsTheExpiryOfTheReleasedItem()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { TimeToLiveAttribute = "expires", TimeToLiveRetention = TimeSpan.FromDays(7) });
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(TimeSpan.FromSeconds(5));

        // Act
        await lease!.DisposeAsync();

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetNumber<long>("expires")).IsEqualTo(bench.Clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds());
    }

    [Test]
    public async Task DisposeAsync_HolderLabel_RemovesTheLabel()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await lease!.DisposeAsync();

        // Assert
        await Assert.That(bench.ItemOf("job")!.ContainsKey("holder")).IsFalse();
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
    public async Task DisposeAsync_MeetsTheLeasesOwnTransaction_ReleasesAfterAShortWait()
    {
        // Arrange
        using var bench = new LockHarness();
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Handler.Enqueue(TransactionConflict());

        // Act
        await bench.RunAsync(Completion(lease!.DisposeAsync().AsTask()), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That((bench.ItemOf("job")!.ContainsKey("owner"), bench.LockLogs.Count)).IsEqualTo((false, 0));
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

    [Test]
    public async Task Current_OutsideATerm_IsNull()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        var current = DynaSharp.Coordination.Lease.Current;

        // Assert (a lease that was acquired by hand is never current)
        await Assert.That(current).IsNull();
    }

    [Test]
    public async Task Current_InTheWorkOfALeader_IsItsLease()
    {
        // Arrange
        using var bench = new LockHarness();
        Lease? led = null;
        Lease? seen = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, (lease, _) =>
        {
            led = lease;
            seen = DynaSharp.Coordination.Lease.Current;
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(seen).IsSameReferenceAs(led);
    }

    [Test]
    public async Task Current_TaskStartedInTheTerm_FlowsIntoIt()
    {
        // Arrange
        using var bench = new LockHarness();
        Lease? led = null;
        Lease? seen = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, async (lease, _) =>
        {
            led = lease;
            seen = await Task.Run(static () => DynaSharp.Coordination.Lease.Current);
        });

        // Assert
        await Assert.That(seen).IsSameReferenceAs(led);
    }

    [Test]
    public async Task Current_AfterTheTerm_IsNullForTheCaller()
    {
        // Arrange
        using var bench = new LockHarness();
        await bench.Locks.RunAsLeaderAsync("job", Lease, static (_, _) => Task.CompletedTask);

        // Act
        var current = DynaSharp.Coordination.Lease.Current;

        // Assert
        await Assert.That(current).IsNull();
    }

    [Test]
    public async Task Current_TaskThatOutlivesTheTerm_KeepsTheEndedLease()
    {
        // Arrange (the work starts a task and returns without waiting for it, so the term ends and the lease is released)
        using var bench = new LockHarness();
        var termEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Lease?>? straggler = null;
        await bench.Locks.RunAsLeaderAsync("job", Lease, (_, _) =>
        {
            straggler = Task.Run(async () =>
            {
                await termEnded.Task;
                return DynaSharp.Coordination.Lease.Current;
            });
            return Task.CompletedTask;
        });

        // Act
        termEnded.SetResult();
        var kept = await straggler!;

        // Assert
        await Assert.That(kept!.Ended.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Current_NestedLeadership_IsTheInnerLeaseAndThenTheOuterAgain()
    {
        // Arrange
        using var bench = new LockHarness();
        Lease? outer = null;
        Lease? inner = null;
        Lease? innerSeen = null;
        Lease? outerAfter = null;

        Task Inner(Lease lease, CancellationToken stop)
        {
            inner = lease;
            innerSeen = DynaSharp.Coordination.Lease.Current;
            return Task.CompletedTask;
        }

        // Act
        await bench.Locks.RunAsLeaderAsync("outer", Lease, async (lease, stop) =>
        {
            outer = lease;
            await bench.Locks.RunAsLeaderAsync("inner", Lease, Inner, stop);
            outerAfter = DynaSharp.Coordination.Lease.Current;
        });

        // Assert
        await Assert.That((ReferenceEquals(innerSeen, inner), ReferenceEquals(outerAfter, outer))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Current_UnderSuppressedFlow_IsNull()
    {
        // Arrange
        using var bench = new LockHarness();
        Lease? seen = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, async (lease, _) =>
        {
            Task<Lease?> suppressed;
            using (ExecutionContext.SuppressFlow())
            {
                suppressed = Task.Run(static () => DynaSharp.Coordination.Lease.Current);
            }

            seen = await suppressed;
        });

        // Assert
        await Assert.That(seen).IsNull();
    }

    [Test]
    public async Task Current_RenewalOfTheHeartbeat_IsNull()
    {
        // Arrange (the heartbeat started with the acquire, before the term, and under a suppressed flow)
        using var bench = new LockHarness();
        var renewals = new TaskCompletionSource<Lease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Lease? led = null;

        // Act
        await bench.Locks.RunAsLeaderAsync("job", Lease, async (lease, _) =>
        {
            led = lease;
            bench.Intercept = (request, _) =>
            {
                renewals.TrySetResult(DynaSharp.Coordination.Lease.Current);
                return Task.FromResult<StubResponse?>(null);
            };
            await BeatAsync(bench, lease);
            bench.Intercept = null;
        });

        // Assert
        await Assert.That((led is not null, await renewals.Task)).IsEqualTo((true, (Lease?)null));
    }

    // Moves the clock to the next renewal of the heartbeat and waits until it is done.
    private static bool LinksTo(IEnumerable<ActivityLink>? links, ActivityTraceId trace) => links?.Any(link => link.Context.TraceId == trace) == true;

    private static StubResponse TransactionConflict() =>
        StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item");

    private static async Task<bool> Completion(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return true;
    }

    private static async Task BeatAsync(LockHarness bench, Lease lease)
    {
        var beat = lease.NextBeat;
        bench.Clock.Advance(Period);
        await beat;
    }
}
