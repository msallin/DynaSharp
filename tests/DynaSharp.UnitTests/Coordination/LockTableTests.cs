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
        await Assert.That((lease!.Name, lease.FencingToken, lease.Ended.IsCancellationRequested)).IsEqualTo(("job", 1L, false));
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
        await Assert.That((taken, holder.Ended.IsCancellationRequested)).IsEqualTo(((Lease?)null, false));
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
    public async Task TryAcquireAsync_LockTakenBetweenTheReadAndTheWrite_ReturnsNullAndRemembersTheNewHolder()
    {
        // Arrange (the contender saw a holder, which released; the poll reads the lock free, and another process acquires before the write)
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        await holder!.DisposeAsync();
        var other = bench.OtherProcess(new ManualTimeProvider());
        var winner = new TaskCompletionSource<Lease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bench.Intercept = async (request, cancellationToken) =>
        {
            if (request.Operation != "GetItem" || winner.Task.IsCompleted)
            {
                return null;
            }

            var free = bench.Fake.Answer(request);
            winner.SetResult(await other.TryAcquireAsync("job", Lease, cancellationToken));
            return free;
        };
        var before = bench.Handler.Requests.Count;

        // Act
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        var won = await winner.Task;
        await Assert.That((lease, won?.FencingToken, string.Join(",", bench.Handler.Requests.Skip(before).Select(static request => request.Operation))))
            .IsEqualTo(((Lease?)null, (long?)2L, "GetItem,UpdateItem"));
        await won!.DisposeAsync();
    }

    [Test]
    public async Task TryAcquireAsync_SeenLockDeleted_StartsTheTokenAgain()
    {
        // Arrange (the contender saw a holder, and then someone deleted the item)
        using var bench = new LockHarness();
        await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Fake.Delete(bench.TableName, bench.KeyOf("job"));

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(lease?.FencingToken).IsEqualTo(1L);
    }

    [Test]
    public async Task TryAcquireAsync_FailedConditionWithoutTheItem_ReadsTheItem()
    {
        // Arrange (a service that does not return the item of a failed condition, as an emulator may not)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem"
            ? StubResponse.DynamoDbError("ConditionalCheckFailedException", "The conditional request failed")
            : null);

        // Act
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease, string.Join(",", bench.Handler.Requests.Select(static request => request.Operation)))).IsEqualTo(((Lease?)null, "UpdateItem,GetItem"));
    }

    [Test]
    public async Task TryAcquireAsync_FailedConditionWithoutTheItemOnALockReleasedSince_ReturnsNullAndTriesAgainByWriting()
    {
        // Arrange (the failed condition comes without the item, and the read after it finds the lock free)
        using var bench = new LockHarness();
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem" && bench.Handler.Requests.Count == 1
            ? StubResponse.DynamoDbError("ConditionalCheckFailedException", "The conditional request failed")
            : null);
        var first = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await using var second = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((first, second?.FencingToken, string.Join(",", bench.Handler.Requests.Select(static request => request.Operation))))
            .IsEqualTo(((Lease?)null, (long?)1L, "UpdateItem,GetItem,UpdateItem"));
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
            ("DynaSharp.Coordination.LockTable", LogLevel.Information, 41, $"Lock job in table {bench.TableName} was taken over from a holder that did not renew it for its lease of 00:00:30 (holder label (null), now (null))."));
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
        await Assert.That((lease!.Ended.IsCancellationRequested, lease.LossReason)).IsEqualTo((true, "expired"));
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
        await Assert.That((lease!.FencingToken, lease.Owner, lease.Ended.IsCancellationRequested)).IsEqualTo((1L, bench.ItemOf("job")!.GetString("owner")!, false));
    }

    [Test]
    public async Task TryAcquireAsync_WriteMeetsTheHoldersTransaction_ReturnsNullAfterReadingTheItem()
    {
        // Arrange (the holder fences a write with IsHeld, and the acquire meets its transaction on the lock item)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem" ? TransactionConflict() : null);

        // Act
        var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease, string.Join(",", bench.Handler.Requests.Select(static request => request.Operation)))).IsEqualTo(((Lease?)null, "UpdateItem,GetItem"));
    }

    [Test]
    public async Task TryAcquireAsync_WriteMeetsATransactionOnAFreeLock_ReturnsNullAndTriesAgainByWriting()
    {
        // Arrange (a transaction holds the item of a free lock, and the read after the conflict finds it free)
        using var bench = new LockHarness();
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem" && bench.Handler.Requests.Count == 1 ? TransactionConflict() : null);
        var first = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await using var second = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((first, second?.FencingToken, string.Join(",", bench.Handler.Requests.Select(static request => request.Operation))))
            .IsEqualTo(((Lease?)null, (long?)1L, "UpdateItem,GetItem,UpdateItem"));
    }

    [Test]
    public async Task TryAcquireAsync_TakeOverMeetsATransaction_TakesOverOnTheNextTry()
    {
        // Arrange (the contender waited out a silent holder; its takeover meets a transaction, which changes no record version)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        var conflicts = 0;
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem" && conflicts++ == 0 ? TransactionConflict() : null);
        var refused = await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((refused, taken?.FencingToken)).IsEqualTo(((Lease?)null, (long?)2L));
    }

    [Test]
    public async Task AcquireAsync_WriteMeetsTheHoldersTransaction_WaitsAndAcquiresOnceReleased()
    {
        // Arrange (the first write meets the holder's transaction; the contender waits as for a held lock, and the holder releases)
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var conflicts = 0;
        bench.Intercept = (request, _) => Task.FromResult<StubResponse?>(request.Operation == "UpdateItem" && conflicts++ == 0 ? TransactionConflict() : null);
        var waiting = bench.Locks.AcquireAsync("job", Lease);
        SpinWait.SpinUntil(() => bench.Clock.ActiveTimers == 1, TimeSpan.FromSeconds(10));
        await holder!.DisposeAsync();

        // Act
        bench.Clock.Advance(Lease / 3);
        await using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task TryAcquireAsync_ResponseLostAndRetryMeetsATransaction_HoldsTheLockTheFirstAttemptTook()
    {
        // Arrange (the first acquire is applied and its answer lost, and the retry of the client meets a transaction on the item)
        using var bench = new LockHarness(maxAttempts: 2);
        var updates = 0;
        bench.Intercept = (request, _) =>
        {
            if (request.Operation != "UpdateItem")
            {
                return Task.FromResult<StubResponse?>(null);
            }

            switch (updates++)
            {
                case 0:
                    bench.Fake.Answer(request);
                    return Task.FromResult<StubResponse?>(StubResponse.DynamoDbError("InternalServerError", "Lost.", HttpStatusCode.InternalServerError));
                case 1:
                    return Task.FromResult<StubResponse?>(TransactionConflict());
                default:
                    return Task.FromResult<StubResponse?>(null);
            }
        };

        // Act
        await using var lease = await bench.RunAsync(bench.Locks.TryAcquireAsync("job", Lease), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That((lease!.FencingToken, lease.Owner, lease.Ended.IsCancellationRequested)).IsEqualTo((1L, bench.ItemOf("job")!.GetString("owner")!, false));
    }

    [Test]
    [Arguments("no rvn", "rvn")]
    [Arguments("owner a number", "owner")]
    [Arguments("rvn a number", "rvn")]
    [Arguments("no leaseMs", "leaseMs")]
    [Arguments("leaseMs a string", "leaseMs")]
    [Arguments("leaseMs a fraction", "leaseMs")]
    [Arguments("leaseMs zero", "leaseMs")]
    [Arguments("leaseMs over a day", "leaseMs")]
    public async Task TryAcquireAsync_ItemAtTheKeyIsNoLock_ThrowsNamingTheLockAndTheTable(string item, string attribute)
    {
        // Arrange (an item another writer put at the key of the lock, with an owner)
        using var bench = new LockHarness();
        var foreign = new Item(bench.KeyOf("b")) { ["owner"] = "someone", ["rvn"] = "v1", ["leaseMs"] = 30_000 };
        switch (item)
        {
            case "no rvn":
                foreign.Remove("rvn");
                break;
            case "owner a number":
                foreign["owner"] = 7;
                break;
            case "rvn a number":
                foreign["rvn"] = 7;
                break;
            case "no leaseMs":
                foreign.Remove("leaseMs");
                break;
            case "leaseMs a string":
                foreign["leaseMs"] = "30000";
                break;
            case "leaseMs a fraction":
                foreign["leaseMs"] = 1.5m;
                break;
            case "leaseMs zero":
                foreign["leaseMs"] = 0;
                break;
            default:
                foreign["leaseMs"] = 86_400_001L;
                break;
        }

        bench.Fake.Put(bench.TableName, foreign);

        // Act
        var act = () => bench.Locks.TryAcquireAsync("b", Lease);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessage(
            $"The item of lock \"b\" in table {bench.TableName} is not a lock item written by LockTable: its attribute {attribute} is missing or not "
            + (attribute == "leaseMs" ? "a whole number from 1 to 86400000" : "a string") + ". Another item uses the key of this lock; give the lock another name.",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task TryAcquireAsync_FreeItemWithAFenceThatIsNoWholeNumber_Throws()
    {
        // Arrange (no owner, so the acquire writes, and the old item it reads back has a fence no lock writes)
        using var bench = new LockHarness();
        bench.Fake.Put(bench.TableName, new Item(bench.KeyOf("b")) { ["fence"] = 1.5m });

        // Act
        var act = () => bench.Locks.TryAcquireAsync("b", Lease);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>().WithMessageContaining("its attribute fence is missing or not a whole number", StringComparison.Ordinal);
    }

    [Test]
    public async Task TryAcquireAsync_FreeItemWithoutAFence_StartsTheTokenAtOne()
    {
        // Arrange (an item at the key without owner and fence, such as one written before by hand)
        using var bench = new LockHarness();
        bench.Fake.Put(bench.TableName, new Item(bench.KeyOf("b")) { ["note"] = "reserved" });

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("b", Lease);

        // Assert
        await Assert.That(lease!.FencingToken).IsEqualTo(1L);
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
             $"db.collection.name={bench.TableName},dynasharp.lock.takeover=False",
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

        // Assert (the span of the lock and the one of its write, whose failed condition brings the item along, so there is no read)
        var span = capture.All.Single(activity => activity.OperationName == "Lock.TryAcquire");
        await Assert.That((ActivityCapture.Tags(span), capture.All.Count)).IsEqualTo(
            ($"db.system.name=aws.dynamodb,db.collection.name={bench.TableName},dynasharp.lock.name=job,dynasharp.lock.acquired=False", 2));
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
        await Assert.That((waiting.IsCompleted, holder!.Ended.IsCancellationRequested)).IsEqualTo((false, false));
        await stop.CancelAsync();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task AcquireAsync_HolderReleases_AcquiresWithinAThirdOfItsLease()
    {
        // Arrange (the contender found the lock held and armed its wait, then the holder released)
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        var waiting = bench.Locks.AcquireAsync("job", Lease);
        SpinWait.SpinUntil(() => bench.Clock.ActiveTimers == 1, TimeSpan.FromSeconds(10));
        await holder!.DisposeAsync();

        // Act
        bench.Clock.Advance(Lease / 3);
        await using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task AcquireAsync_HolderWithALongLeaseReleases_AcquiresWithinTenSeconds()
    {
        // Arrange (a third of the holder's lease is 8 hours; the contender polls every 10 seconds all the same)
        using var bench = new LockHarness();
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", TimeSpan.FromDays(1));
        var waiting = bench.Locks.AcquireAsync("job", Lease);
        SpinWait.SpinUntil(() => bench.Clock.ActiveTimers == 1, TimeSpan.FromSeconds(10));
        await holder!.DisposeAsync();

        // Act
        bench.Clock.Advance(TimeSpan.FromSeconds(10));
        await using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That(lease.FencingToken).IsEqualTo(2L);
    }

    [Test]
    public async Task TryAcquireAsync_WaitedOutHolderReleasedFirst_IsNoTakeover()
    {
        // Arrange (the contender waited out the holder's lease, but the holder released before the next try)
        using var bench = new LockHarness();
        using var metrics = MeasurementCapture<long>.ForTable(bench.TableName, "dynasharp.lock.acquisitions");
        var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);
        await holder!.DisposeAsync();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((lease!.FencingToken, bench.LockLogs.Count(static entry => entry.EventId.Id == 41), metrics.All[^1].TagText)).IsEqualTo(
            (2L, 0, $"db.collection.name={bench.TableName},dynasharp.lock.takeover=False"));
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

    [Test]
    public async Task LeaseDurationBounds_AreASecondAndADay()
    {
        // Arrange
        var bounds = (LockTable.MinLeaseDuration, LockTable.MaxLeaseDuration);

        // Act
        var expected = (TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));

        // Assert
        await Assert.That(bounds).IsEqualTo(expected);
    }

    [Test]
    public async Task Options_NoneGiven_AreTheDefaults()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var options = bench.Locks.Options;

        // Assert
        await Assert.That((options.HolderLabel, options.TimeToLiveAttribute, options.TimeToLiveRetention)).IsEqualTo(((string?)null, (string?)null, TimeSpan.FromDays(30)));
    }

    [Test]
    [Arguments("")]
    [Arguments("x257")]
    public async Task Constructor_HolderLabelOutOfRange_ThrowsArgumentException(string label)
    {
        // Arrange
        using var bench = new LockHarness();
        var options = new LockTableOptions { HolderLabel = label == "x257" ? new string('x', 257) : label };

        // Act
        var act = () => new LockTable(bench.Table, options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("options");
    }

    [Test]
    public async Task Constructor_HolderLabelOfTheLongestLength_IsKept()
    {
        // Arrange
        using var bench = new LockHarness();
        var label = new string('x', LockTableOptions.MaxHolderLabelLength);

        // Act
        var locks = new LockTable(bench.Table, new() { HolderLabel = label });

        // Assert
        await Assert.That(locks.Options.HolderLabel).IsEqualTo(label);
    }

    [Test]
    [Arguments("")]
    [Arguments("a.b")]
    [Arguments("a[0]")]
    [Arguments("_type")]
    [Arguments("owner")]
    [Arguments("rvn")]
    [Arguments("fence")]
    [Arguments("leaseMs")]
    [Arguments("holder")]
    [Arguments("pk")]
    [Arguments("sk")]
    public async Task Constructor_TimeToLiveAttributeNotFree_ThrowsArgumentException(string attribute)
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var act = () => new LockTable(bench.Table, new() { TimeToLiveAttribute = attribute });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithParameterName("options");
    }

    [Test]
    public async Task Constructor_RetentionBelowADay_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var bench = new LockHarness();
        var options = new LockTableOptions { TimeToLiveAttribute = "expires", TimeToLiveRetention = TimeSpan.FromDays(1) - TimeSpan.FromTicks(1) };

        // Act
        var act = () => new LockTable(bench.Table, options);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("options");
    }

    [Test]
    public async Task For_TypedTable_OpensTheSameTableWithoutAModel()
    {
        // Arrange
        using var bench = new LockHarness();
        var typed = bench.Client.Table<DynaSharp.UnitTests.Fluent.Customer>(bench.TableName);

        // Act
        var locks = LockTable.For(typed, new() { HolderLabel = "pod-1" });

        // Assert
        await Assert.That((locks.Table.Name, locks.Table.PartitionKeyName, locks.Table.SortKeyName, locks.Table.Client, locks.Options.HolderLabel))
            .IsEqualTo((bench.TableName, typed.PartitionKeyName, typed.SortKeyName, bench.Client, (string?)"pod-1"));
    }

    [Test]
    public async Task For_TableOfItems_IsTheTableGiven()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        var locks = LockTable.For(bench.Table);

        // Assert
        await Assert.That(locks.Table).IsSameReferenceAs(bench.Table);
    }

    [Test]
    public async Task For_NullTable_ThrowsArgumentNullException()
    {
        // Arrange
        Table<Item>? table = null;

        // Act
        var act = () => LockTable.For(table!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("table");
    }

    [Test]
    public async Task TryAcquireAsync_LockSeenHeld_ReadsWithoutWriting()
    {
        // Arrange (the first try writes, fails its condition and remembers the holder)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        var before = bench.Handler.Requests.Count;

        // Act
        var second = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((second, string.Join(",", bench.Handler.Requests.Skip(before).Select(static request => request.Operation)))).IsEqualTo(((Lease?)null, "GetItem"));
    }

    [Test]
    public async Task TryAcquireAsync_LockSeenHeld_ReadsStronglyConsistent()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);

        // Act
        await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.Handler.Requests[^1].ConsistentRead == true).IsTrue();
    }

    [Test]
    public async Task AcquireAsync_HolderAlive_PollsWithReadsOnlyAfterTheFirstTry()
    {
        // Arrange (the holder renews on the same clock, every third of its lease, for four leases)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(bench.Clock).TryAcquireAsync("job", Lease);
        using var stop = new CancellationTokenSource();
        var waiting = bench.Locks.AcquireAsync("job", Lease, stop.Token);
        SpinWait.SpinUntil(() => bench.Clock.ActiveTimers == 2, TimeSpan.FromSeconds(10));

        // Act
        for (var second = 0; second < 120; second++)
        {
            var beat = holder!.NextBeat;
            bench.Clock.Advance(TimeSpan.FromSeconds(1));
            if (second % 10 == 9)
            {
                await beat;
                SpinWait.SpinUntil(() => bench.Handler.Requests.Count >= 1 + ((second + 1) / 10), TimeSpan.FromSeconds(10));
            }
        }

        await stop.CancelAsync();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();

        // Assert (one failed write to find the holder, then a read every 10 seconds)
        var operations = bench.Handler.Requests.Select(static request => request.Operation).ToList();
        await Assert.That((operations[0], operations.Skip(1).Distinct().Single(), operations.Count >= 12)).IsEqualTo(("UpdateItem", "GetItem", true));
    }

    [Test]
    public async Task AcquireAsync_HolderCrashed_WritesOnlyToFindItAndToTakeOver()
    {
        // Arrange
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);

        // Act
        await using var lease = await bench.RunAsync<Lease>(bench.Locks.AcquireAsync("job", Lease), TimeSpan.FromSeconds(1));

        // Assert
        var operations = bench.Handler.Requests.Select(static request => request.Operation).ToList();
        await Assert.That((lease.FencingToken, operations[0], operations[^1], operations.Count(static operation => operation == "UpdateItem"), operations[^2]))
            .IsEqualTo((2L, "UpdateItem", "UpdateItem", 2, "GetItem"));
    }

    [Test]
    public async Task TryAcquireAsync_HolderLabel_IsWrittenIntoTheItem()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-1" });

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetString("holder")).IsEqualTo("pod-1");
    }

    [Test]
    public async Task TryAcquireAsync_NoLabelAfterALabelledHolder_RemovesTheLabel()
    {
        // Arrange
        using var bench = new LockHarness();
        await (await bench.OtherProcess(bench.Clock, new() { HolderLabel = "pod-1" }).TryAcquireAsync("job", Lease))!.DisposeAsync();
        bench.Fake.Put(bench.TableName, new Item(bench.ItemOf("job")!) { ["holder"] = "stale" });

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.ContainsKey("holder")).IsFalse();
    }

    [Test]
    public async Task TryAcquireAsync_TakeOverFromALabelledHolder_LogsBothLabels()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { HolderLabel = "pod-2" });
        await using var holder = await bench.OtherProcess(new ManualTimeProvider(), new() { HolderLabel = "pod-1" }).TryAcquireAsync("job", Lease);
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That((bench.LockLogs.Single().Message, bench.ItemOf("job")!.GetString("holder"))).IsEqualTo(
            ($"Lock job in table {bench.TableName} was taken over from a holder that did not renew it for its lease of 00:00:30 (holder label pod-1, now pod-2).", "pod-2"));
    }

    [Test]
    public async Task TryAcquireAsync_HolderLabelOfAnotherType_IsLoggedAsNone()
    {
        // Arrange (a label that is not a string is diagnostic only, and does not make the item foreign)
        using var bench = new LockHarness();
        await using var holder = await bench.OtherProcess(new ManualTimeProvider()).TryAcquireAsync("job", Lease);
        bench.Fake.Put(bench.TableName, new Item(bench.ItemOf("job")!) { ["holder"] = 7 });
        await bench.Locks.TryAcquireAsync("job", Lease);
        bench.Clock.Advance(Lease);

        // Act
        await using var taken = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.LockLogs.Single().Message).Contains("(holder label (null), now (null))");
    }

    [Test]
    public async Task TryAcquireAsync_TimeToLive_StampsTheRetentionFromNow()
    {
        // Arrange
        using var bench = new LockHarness(options: new() { TimeToLiveAttribute = "expires", TimeToLiveRetention = TimeSpan.FromDays(2) });

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.GetNumber<long>("expires")).IsEqualTo(bench.Clock.GetUtcNow().AddDays(2).ToUnixTimeSeconds());
    }

    [Test]
    public async Task TryAcquireAsync_NoTimeToLive_WritesNoExpiry()
    {
        // Arrange
        using var bench = new LockHarness();

        // Act
        await using var lease = await bench.Locks.TryAcquireAsync("job", Lease);

        // Assert
        await Assert.That(bench.ItemOf("job")!.Count).IsEqualTo(7);
    }

    private static StubResponse TransactionConflict() =>
        StubResponse.DynamoDbError("TransactionConflictException", "Transaction is ongoing for the item");
}
