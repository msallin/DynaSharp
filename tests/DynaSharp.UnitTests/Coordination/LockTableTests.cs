using System.Diagnostics;
using System.Net;
using DynaSharp.Coordination;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests.Coordination;

public sealed class LockTableTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Test]
    public async Task Constructor_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        Table<Item>? table = null;

        // Act
        var act = () => new LockTable(table!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("table");
    }

    [Test]
    public async Task Table_AfterConstruction_IsTheTableGiven()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var table = bench.Locks.Table;

        // Assert
        await Assert.That(table).IsSameReferenceAs(bench.Table);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task TryAcquireAsync_NoName_ThrowsArgumentException(string? name)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.TryAcquireAsync(name!, Lease);

        // Assert
        await Assert.That(act).Throws<ArgumentException>().WithParameterName("name");
    }

    [Test]
    [Arguments(0L)]
    [Arguments(9_999_999L)]
    [Arguments(864_000_000_001L)]
    public async Task TryAcquireAsync_LeaseOutOfRange_ThrowsArgumentOutOfRangeException(long ticks)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => bench.Locks.TryAcquireAsync("job", TimeSpan.FromTicks(ticks));

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("leaseDuration");
    }

    [Test]
    [Arguments(10_000_000L)]
    [Arguments(864_000_000_000L)]
    public async Task TryAcquireAsync_LeaseAtTheBounds_Acquires(long ticks)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", TimeSpan.FromTicks(ticks));

        // Assert
        await Assert.That(lease).IsNotNull();
    }

    [Test]
    public async Task AcquireAsync_NoName_ThrowsArgumentException()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        Func<Task> act = () => bench.Locks.AcquireAsync(string.Empty, Lease);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("name");
    }

    [Test]
    public async Task TryAcquireAsync_FreeLock_ReturnsLeaseWithFencingTokenOne()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease!.Name, lease.FencingToken, lease.Lost.IsCancellationRequested)).IsEqualTo(("job", 1L, false));
    }

    [Test]
    public async Task TryAcquireAsync_FreeLock_WritesTheDocumentedItem()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", TimeSpan.FromTicks(300_000_001));
        var item = bench.ItemOf("job")!;

        // Assert
        await Assert.That(
            (item.Count, item.GetString("_type"), item.GetString("owner")!.Length, item.GetString("rvn")!.Length, item.GetNumber<long>("fence"), item.GetNumber<long>("leaseMs")))
            .IsEqualTo((7, "LOCK", 32, 32, 1L, 30_001L));
    }

    [Test]
    public async Task TryAcquireAsync_TableWithoutSortKey_KeysTheItemByPartitionKeyAlone()
    {
        // Arrange
        using var bench = new LockHarness(sortKey: false);

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")?.GetNumber<long>("fence")).IsEqualTo(1L);
    }

    [Test]
    public async Task TryAcquireAsync_LockHeld_ReturnsNull()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        var second = await new LockTable(bench.Table).TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(second).IsNull();
    }

    [Test]
    public async Task TryAcquireAsync_OtherLockHeld_Acquires()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await using var other = await bench.Locks.TryAcquireAsync("other-job", Lease);

        // Assert
        await Assert.That(other?.FencingToken).IsEqualTo(1L);
    }

    [Test]
    public async Task TryAcquireAsync_AfterRelease_RaisesTheFencingToken()
    {
        // Arrange
        using var bench = new LockHarness();
        await (await bench.Locks.TryAcquireAsync("job", Lease))!.DisposeAsync();
        await (await bench.Locks.TryAcquireAsync("job", Lease))!.DisposeAsync();

        // Act
        await using var third = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(third?.FencingToken).IsEqualTo(3L);
    }

    [Test]
    public async Task TryAcquireAsync_HolderSilentForAWholeLease_TakesOverWithAHigherToken()
    {
        // Arrange (the holder runs on a clock that stands still: from here it looks like a process that crashed)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var seen = await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((seen, taken?.FencingToken, bench.ItemOf("job")!.GetString("owner") == taken?.Owner)).IsEqualTo(((Lease?)null, 2L, true));
    }

    [Test]
    public async Task TryAcquireAsync_LeaseNotWaitedOut_ReturnsNull()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease - TimeSpan.FromTicks(1));

        // Act
        var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(taken).IsNull();
    }

    [Test]
    public async Task TryAcquireAsync_HolderRenewedDuringTheWait_DoesNotTakeOver()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease - TimeSpan.FromSeconds(1));
        await holder!.RenewAsync();
        bench.Clock.Advance(TimeSpan.FromSeconds(1));

        // Act
        var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((taken, holder.Lost.IsCancellationRequested)).IsEqualTo(((Lease?)null, false));
    }

    [Test]
    public async Task TryAcquireAsync_HolderRenewedDuringTheWait_TakesOverAWholeLeaseAfterItsLastRenewal()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        await holder!.RenewAsync();
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(taken?.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task TryAcquireAsync_HolderReleasedWhileTheLockWasWatched_AcquiresAtOnce()
    {
        // Arrange
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        await holder!.DisposeAsync();

        // Act
        await using var next = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(next?.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task AcquireAsync_HolderReleasesBetweenTheWriteAndTheRead_TriesAgainAtOnce()
    {
        // Arrange (the acquire fails its condition, and the holder releases before the read that follows)
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        bench.Intercept = async (request, _) =>
        {
            if (request.Operation == "GetItem")
            {
                bench.Intercept = null;
                await holder!.DisposeAsync();
            }

            return null;
        };

        // Act
        await using var lease = await bench.Locks.AcquireAsync("job", Lease);

        // Assert (no time passed: a free lock is tried again without a wait)
        await Assert.That(lease.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task AcquireAsync_ItemDeletedBetweenTheWriteAndTheRead_StartsTheTokenAgain()
    {
        // Arrange (the acquire fails its condition, and someone deletes the item before the read that follows)
        using var bench = new LockHarness();
        await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        bench.Intercept = (request, _) =>
        {
            if (request.Operation == "GetItem")
            {
                bench.Intercept = null;
                bench.Fake.Delete(bench.TableName, bench.KeyOf("job"));
            }

            return Task.FromResult<StubResponse?>(null);
        };

        // Act
        await using var lease = await bench.Locks.AcquireAsync("job", Lease);

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(1L);
    }

    [Test]
    public async Task TryAcquireAsync_TakeOverRacesAnotherContender_LosesTheRace()
    {
        // Arrange (two contenders waited out the same holder; the other one writes first)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var other = bench.OtherProcess(bench.Clock);
        await bench.Locks.TryAcquireAsync("job", Lease);
        await other.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        await using var winner = await other.TryAcquireAsync("job", Lease);

        // Act
        var loser = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((winner?.FencingToken, loser)).IsEqualTo(((long?)2L, (Lease?)null));
    }

    [Test]
    public async Task TryAcquireAsync_TakeOver_LogsIt()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        var entry = bench.LockLogs.Single();
        await Assert.That((entry.Category, entry.Level, entry.EventId.Id, entry.Message)).IsEqualTo(
            ("DynaSharp.Coordination.LockTable", LogLevel.Information, 41, $"Lock job in table {bench.TableName} was taken over from a holder that did not renew it for its lease of 00:00:30."));
    }

    [Test]
    public async Task TryAcquireAsync_ResponseArrivesAfterTheLease_ReturnsALostLease()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Intercept = (_, _) =>
        {
            bench.Clock.Advance(Lease);
            return Task.FromResult<StubResponse?>(null);
        };

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease!.Lost.IsCancellationRequested, lease.LossReason)).IsEqualTo((true, "expired"));
    }

    [Test]
    public async Task TryAcquireAsync_ResponseLostAndRetried_HoldsTheLockTheFirstAttemptTook()
    {
        // Arrange (the first acquire is applied, its answer lost, and the retry of the client fails the condition the first attempt met)
        using var bench = new LockHarness(maxAttempts: 2);
        var lost = false;
        bench.Intercept = (request, _) =>
        {
            if (lost || request.Operation != "UpdateItem")
            {
                return Task.FromResult<StubResponse?>(null);
            }

            lost = true;
            bench.Fake.Answer(request);
            return Task.FromResult<StubResponse?>(StubResponse.DynamoDbError("InternalServerError", "Lost.", HttpStatusCode.InternalServerError));
        };

        // Act
        await using var lease = await bench.RunAsync(bench.Locks.TryAcquireAsync("job", Lease), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That((lease!.FencingToken, lease.Owner, lease.Lost.IsCancellationRequested)).IsEqualTo((1L, bench.ItemOf("job")!.GetString("owner")!, false));
    }

    [Test]
    public async Task TryAcquireAsync_TableMissing_ThrowsResourceNotFoundException()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.DeleteTable(bench.TableName);

        // Act
        var act = () => bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(act).ThrowsExactly<ResourceNotFoundException>();
    }

    [Test]
    public async Task TryAcquireAsync_Free_RecordsASpanAndAnAcquisition()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.acquisitions");

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire");
        await Assert.That((ActivityCapture.Tags(span), metrics.Single().TagText, metrics.Single().Value)).IsEqualTo(
            ($"db.system.name=aws.dynamodb,db.collection.name={bench.TableName},dynasharp.lock.name=job,dynasharp.lock.acquired=True,dynasharp.lock.takeover=False,dynasharp.lock.fencing_token=1",
             $"db.collection.name={bench.TableName},dynasharp.lock.name=job,dynasharp.lock.takeover=False",
             1L));
    }

    [Test]
    public async Task TryAcquireAsync_Held_RecordsASpanThatDidNotAcquire()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("job", Lease);
        using var capture = new ActivityCapture();

        // Act
        await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire");
        await Assert.That((ActivityCapture.Tags(span), capture.All.Count)).IsEqualTo(
            ($"db.system.name=aws.dynamodb,db.collection.name={bench.TableName},dynasharp.lock.name=job,dynasharp.lock.acquired=False", 3));
    }

    [Test]
    public async Task TryAcquireAsync_Fails_MarksTheSpanAsFailed()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Fake.DeleteTable(bench.TableName);
        using var capture = new ActivityCapture();

        // Act
        await Assert.That(() => bench.Locks.TryAcquireAsync("job", Lease)).Throws<ResourceNotFoundException>();

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire");
        await Assert.That((span.Status, span.GetTagItem("error.type"))).IsEqualTo((ActivityStatusCode.Error, (object?)"ResourceNotFoundException"));
    }

    [Test]
    public async Task TryAcquireAsync_FailsWithoutAnErrorCode_NamesTheExceptionType()
    {
        // Arrange
        using var bench = new LockHarness();
        bench.Intercept = (_, _) => throw new HttpRequestException("down");
        using var capture = new ActivityCapture();

        // Act
        await Assert.That(() => bench.Locks.TryAcquireAsync("job", Lease)).Throws<HttpRequestException>();

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire");
        await Assert.That(span.GetTagItem("error.type")).IsEqualTo("System.Net.Http.HttpRequestException");
    }

    [Test]
    public async Task AcquireAsync_FreeLock_AcquiresWithoutWaiting()
    {
        // Arrange
        using var bench = new LockHarness();
        using var capture = new ActivityCapture();

        // Act
        await using var lease = await bench.Locks.AcquireAsync("job", Lease);

        // Assert
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Acquire");
        await Assert.That((lease.FencingToken, span.GetTagItem("dynasharp.lock.acquired"))).IsEqualTo((1L, (object?)true));
    }

    [Test]
    public async Task AcquireAsync_HolderCrashed_TakesOverOnceTheLeaseIsWaitedOut()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var start = bench.Clock.GetTimestamp();

        // Act
        await using var lease = await bench.RunAsync<Lease>(bench.Locks.AcquireAsync("job", Lease), TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That((lease.FencingToken, bench.Clock.GetElapsedTime(start) >= Lease)).IsEqualTo((2L, true));
    }

    [Test]
    public async Task AcquireAsync_HolderAlive_WaitsWhileItRenews()
    {
        // Arrange (the holder renews on the same clock, every third of its lease)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);
        using var stop = new CancellationTokenSource();
        var waiting = bench.Locks.AcquireAsync("job", Lease, stop.Token);

        // Act
        for (var second = 0; second < 120; second++)
        {
            var beat = holder!.NextBeat;
            bench.Clock.Advance(TimeSpan.FromSeconds(1));
            if (second % 10 == 9)
            {
                await beat;
            }
        }

        // Assert
        await Assert.That((waiting.IsCompleted, holder!.Lost.IsCancellationRequested)).IsEqualTo((false, false));
        await stop.CancelAsync();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task AcquireAsync_HolderReleases_AcquiresWithinAThirdOfItsLease()
    {
        // Arrange
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var waiting = bench.Locks.AcquireAsync("job", Lease);
        await holder!.DisposeAsync();
        var start = bench.Clock.GetTimestamp();

        // Act
        await using var lease = await bench.RunAsync<Lease>(waiting, TimeSpan.FromSeconds(1));

        // Assert
        await Assert.That((lease.FencingToken, bench.Clock.GetElapsedTime(start) <= Lease / 3)).IsEqualTo((2L, true));
    }

    [Test]
    public async Task AcquireAsync_Cancelled_ThrowsAndMarksTheSpan()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.Locks.TryAcquireAsync("job", Lease);
        using var stop = new CancellationTokenSource();
        using var capture = new ActivityCapture();
        var waiting = new LockTable(bench.Table).AcquireAsync("job", Lease, stop.Token);

        // Act
        await stop.CancelAsync();

        // Assert
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
        var span = capture.All.Single(activity => activity.OperationName == "Lock.Acquire");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
    }
}
