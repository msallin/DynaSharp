# Coordination

`DynaSharp.Coordination` is a separate package with two things that are easy to get wrong with conditional writes alone: a lease on a named lock, with fencing tokens, and leader election on top of it. Their semantics are those of the [DynamoDB Lock Client](https://github.com/awslabs/amazon-dynamodb-lock-client): a lease has a relative duration and a record version number, and a process that waits for a lock measures the wait on its own monotonic clock, so no two clocks are ever compared.

```shell
dotnet add package DynaSharp.Coordination
```

It depends on `DynaSharp` alone, at the same version, and takes the clock, the logger factory and the retries of the client of the table you give it. A queue and a hi/lo sequence are not in it: they are recipes, see [Queue over a sparse index](patterns-indexes.md#queue-over-a-sparse-index) and [Hi/lo sequence](patterns-writes.md#hilo-sequence).

## A lock table

The locks live in a table you already have, as one item each, so they fit a single-table design:

```csharp
var locks = new LockTable(client.Table("Shop", "pk", "sk"));
```

The table is opened without a model, and its key attributes must be strings. A typed table works as well, since the locks need only its name, its key names and its client, so you do not restate the keys:

```csharp
var locks = LockTable.For(client.Table<Order>("Shop"));
```

A table that holds only locks needs no key names at all. `LockTable.Open` opens it with the string partition key `pk`, `LockTable.PartitionKeyName`, and no sort key, and `CreateTable()` creates it on demand, with time to live on the attribute of `TimeToLiveAttribute` if the options name one:

```csharp
var locks = LockTable.Open(client, "Locks");
await locks.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
```

The item of the lock `nightly-report` has the partition key `LOCK#nightly-report` and, if the table has a sort key, the sort key `LOCK`. Its attributes have fixed names, because every process that contends for a lock must read the item the same way:

| Attribute | Holds |
|---|---|
| `_type` | `LOCK`, so that a scan of a single table can tell a lock from an entity |
| `owner` | The holder, a new id for every acquire. Absent while the lock is free |
| `rvn` | The record version number, a new id on every acquire and renewal |
| `fence` | The fencing token, raised by one with `ADD` on every acquire |
| `leaseMs` | The duration of the lease in milliseconds. Absent while the lock is free |
| `holder` | The holder label of the process that holds it, with `HolderLabel` in the options. Absent while the lock is free |
| Your time to live attribute | The expiry of the item, with `TimeToLiveAttribute` in the options |

A release keeps the item, and by default it has no time to live, so the fencing token never goes back. Deleting the item starts the token at 1 again, also while a lease holds it: that lease is then lost with the reason `deleted`, and the next acquire gets token 1.

The key of a lock belongs to the lock. An item at that key with an `owner` but without the other attributes, such as an entity whose key happens to be `LOCK#` and a name, makes `TryAcquireAsync`, `AcquireAsync` and `RunAsLeaderAsync` throw `InvalidOperationException`, naming the lock and the table, instead of guessing how long that item holds the lock. Give the lock another name.

Hold one `LockTable` per table in a process. It remembers the holders it has seen, which a takeover depends on. Two instances in one process contend like two processes. It forgets a holder that no try has looked at for ten of its leases, so a process that gives up on a lock per job does not remember every lock it ever met; a try that comes back later watches the holder for a whole lease again.

### Holder label

A lock item says who holds it only as an id. A holder label says it in words an operator knows, such as the name of the pod:

```csharp
var locks = new LockTable(client.Table("Shop", "pk", "sk"), new LockTableOptions { HolderLabel = Environment.MachineName });
```

Every lease of that `LockTable` writes the label into the item as `holder`, a release removes it, and an acquire without a label removes the one of the holder before. The logs of a takeover (41) and of the terms of a leader (45, 46) name it. The label is never part of a condition, so processes with and without labels contend as equals, and it is not on spans or metrics. It is shown to operators in the table and in the logs, so it must not carry data: a name of the process, from 1 to 256 characters, nothing else.

### Expiry of lock items

A lock item stays after its release, so that the fencing token never goes back. For a few locks that is what you want. With a lock per entity, thousands of items that are each used once, the items pile up. `TimeToLiveAttribute` lets DynamoDB delete them:

```csharp
var locks = new LockTable(client.Table("Shop", "pk", "sk"), new LockTableOptions { TimeToLiveAttribute = "expiresAt", TimeToLiveRetention = TimeSpan.FromDays(7) });
```

Every write of a lock, its acquire, each renewal and its release, sets that attribute to the wall clock of the process plus the retention, in seconds since the epoch. A held lock is renewed at least every 8 hours and the retention is at least a day, so its item is never due while it is held; a released lock, or one whose holder crashed, is due once nobody has written it for the retention, 30 days by default, and DynamoDB deletes it some time after that, often within days.

Turn on time to live for the table on that attribute yourself, with `TimeToLive(...)` on its `CreateTable()` (see [Creating a table](tables.md#creating-a-table)) or in the console: the option only writes the attribute. The attribute must not be a key of the table or an attribute of the lock.

The trade-off: a lock whose item was deleted starts its fencing token at 1 again. A system outside DynamoDB that keeps the highest token it has seen then refuses the next holders until the count passes what it saw, so fencing across that gap is not guaranteed. `IsHeld` is not affected, because it compares the owner too. Keep locks whose tokens another system checks in a `LockTable` without expiry.

## Acquire and release

`AcquireAsync` waits until it has the lock, `TryAcquireAsync` tries once and returns `null` if another holder has it. Disposing the lease releases the lock.

A process that runs once and stops, such as a scheduled job, a function or a command line tool, gives `AcquireAsync` a wait a little longer than one lease. That gives up on a live holder with a `TimeoutException` that names the lock, and still takes over the lock of a holder that crashed, which needs a whole lease of waiting. The wait runs on the `TimeProvider` of the client, and it never cuts off a request: the last try is the one at the end of the wait. A retry of the same `LockTable`, such as the next delivery of a message, keeps what the try before saw of the holder:

```csharp
Lease lease;
try
{
    lease = await locks.AcquireAsync("nightly-report", TimeSpan.FromSeconds(30), wait: TimeSpan.FromSeconds(40), ct);
}
catch (TimeoutException)
{
    return; // Another process runs the report.
}

await using (lease)
{
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Ended);
    await foreach (var placed in byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed)).ItemsAsync(stop.Token))
    {
        await client.TransactWrite().IsHeld(lease).Put(orders, placed with { Status = OrderStatus.Shipped }).SendAsync(stop.Token);
    }
}
```

`TryAcquireAsync` fits a process that lives longer than a lease and tries again on its own schedule. It never waits, so it takes over a crashed holder's lock only once the same `LockTable` has seen the holder unchanged for a whole lease, over several calls. A short-lived process that calls it once per run never does:

```csharp
var lease = await locks.TryAcquireAsync("cache-refresh", TimeSpan.FromSeconds(30), ct);
if (lease is null)
{
    return; // Another process holds it; this one tries again on its next round.
}

await using (lease)
{
    // ...
}
```

The lease duration runs from 1 second to 1 day, `LockTable.MinLeaseDuration` to `LockTable.MaxLeaseDuration`. A lease renews itself every third of its duration until it is disposed or lost. `RenewAsync` renews it at once, before a step that must not start on a lease that is about to run out, and returns `false` if the lease has ended. It sends one renewal: an error throws and leaves the lease to the heartbeat.

A release is one conditional `UpdateItem` that removes the owner, on condition that the owner is still this lease, so a lock that someone else took over is left alone. A release that fails is logged, not thrown, and the lock is free once the lease runs out. A lease that is never disposed is renewed for as long as the process runs, and holds the lock that long.

## How a lease ends

`Ended` is a `CancellationToken` that is cancelled once the lease is no longer held. Pass it to the work that needs the lock. The lease is lost:

- when a renewal finds another owner in the item, because a contender took it over or someone wrote the item (`taken_over`);
- when a renewal finds the item deleted (`deleted`), which also starts the fencing token at 1 again;
- when DynamoDB answers a renewal with `ResourceNotFoundException`;
- when the lease runs out on the monotonic clock without a renewal that succeeded.

A renewal that fails with any other error, after the retries of the client, is tried again after 100 ms, then after twice as long each time, up to a sixth of the lease, until one succeeds or the lease runs out. So a lease outlasts any number of failed renewals that leave it time. The heartbeat sends the next renewal a third of the lease after the last one that succeeded was sent, not after its answer, so a slow DynamoDB does not push the renewals towards the end of the lease.

Disposing the lease cancels `Ended` too. Losing it is final: acquire the lock again for a new lease. An acquire whose answer arrives only after a whole lease hands out a lease that has ended already.

## Clocks and takeover

A holder that crashed does not release its lock. A contender takes the lock over once it has seen the same record version for a whole lease, measured on its own monotonic clock (`TimeProvider.GetTimestamp` of the client) from the moment it first read that version. The holder counts its lease from the moment it sent the write, which is before the contender can have read it. So on the holder's clock the lease ends before a contender may take the lock over, whatever offset the clocks of the two machines have. What this assumes is that both clocks run at about the same rate.

A holder that stops without knowing it, because its host was suspended, its virtual machine froze, or a garbage collection paused it, does not see its clock run while the contender's does. It can wake up holding a lease that ended a while ago, and act on it before its heartbeat notices. No lease can prevent that; [fencing](#fencing) is what makes it harmless.

While another holder has the lock, `AcquireAsync` polls every third of that holder's lease, but at least every 10 seconds, or sooner if the lease runs out before. A lock that is released is acquired at the next poll; a lock whose holder crashed is taken over after a whole lease.

A poll is a read. A `LockTable` that has not seen the lock held yet writes at once, which costs a free lock a single request; if the lock is held, the failed write brings the item along, and from then on each poll is one strongly consistent `GetItem`. It writes again only when it reads the lock free or when the holder is due for a takeover. So a process that waits for a live leader costs one read per poll and no failed conditional write, which keeps `ConditionalCheckFailedRequests` in CloudWatch for real conflicts: a failed write happens when contenders race for a lock that is free or due, and once when a `LockTable` first meets a held lock.

A takeover raises the fencing token, as every acquire does. If an acquire is retried by the client after its response was lost, the retry finds its own owner in the item, and the lease is the one the first attempt took. An acquire that is cancelled after its write reached DynamoDB leaves a lock that nobody holds until its lease runs out.

## Fencing

A lease can run out while its holder is paused, by a garbage collection, a suspended host or a slow network, and the holder does not know it until it next looks at `Ended`. By then another process may hold the lock. A fencing token is how a write refuses the old holder: every acquire raises it, so the newer holder always has the higher one.

For writes to DynamoDB, `IsHeld(lease)` adds a condition check on the lock item to a transaction, so the transaction commits only while the lease still holds the lock, with this owner and this fencing token:

```csharp
await client.TransactWrite()
    .IsHeld(lease)
    .Put(orders, order)
    .SendAsync(ct);
```

The check reads the item when the transaction commits, which is what makes it safe; `Ended` is only what this process knows. A transaction that fails it throws `TransactionCanceledException`, and the marker tells a lost lease from another reason:

```csharp
TransactMarker held = default;
try
{
    await client.TransactWrite().IsHeld(lease, out held).Put(orders, order).IfNotExists().SendAsync(ct);
}
catch (TransactionCanceledException exception) when (exception.ReasonFor(held) == TransactionFailure.ConditionalCheckFailed)
{
    // The lease is lost: stop, and leave the work to the next holder.
}
```

Work of a leader, see [Leader election](#leader-election), does not have to pass the lease down to the code that writes. `IsHeld()` without a lease checks `Lease.Current`, the lease of the term the code runs in:

```csharp
await client.TransactWrite()
    .IsHeld()
    .Put(orders, order)
    .SendAsync(ct);
```

`RunAsLeaderAsync` sets `Lease.Current` before it calls the work and clears it when the term ends. It flows as an `AsyncLocal` does: into everything the work calls, awaits or starts with `Task.Run` during the term, and never back to the caller. Where it is null, `IsHeld()` throws `InvalidOperationException` ("No lease is current; pass the lease, or start this work inside RunAsLeaderAsync"):

- Code that started before the term, in a constructor or a `StartAsync`, such as a poller that a hosted service started, does not see it. Start that work inside the work of the leader, or pass the lease.
- Code under `ExecutionContext.SuppressFlow()`, and the heartbeat of a lease, do not see it.
- A lease from `TryAcquireAsync` or `AcquireAsync` is never current: pass it to `IsHeld(lease)`.

Work that outlives its term, such as a task the work started and did not wait for, keeps the lease that ended, so `IsHeld()` fails its transaction at DynamoDB, which is the fencing it is there for. A `RunAsLeaderAsync` inside the work of another makes the inner lease current; pass the outer one explicitly. `IsHeld(out marker)` hands out the marker as `IsHeld(lease, out marker)` does.

The lock item must be in the same account and region as the items of the transaction. A system outside DynamoDB, such as a file store or another database, protects itself only if it checks the token: it keeps the highest `FencingToken` it has seen and refuses a write with a lower one. DynaSharp cannot enforce that for it.

The token rises as long as the item of the lock lives. Deleting the item starts it at 1 again, even while a lease holds it, which loses that lease with the reason `deleted`, and restoring the table from a backup or to a point in time brings an older token back: a system that checks the token then refuses every holder until the count passes what it has seen. A global table that takes writes in more than one region can grant a lock twice with the same token, because each region applies its conditional write before the other one's arrives. Keep the lock table to a single region that writes.

## Leader election

`RunAsLeaderAsync` contends for a lock until the work ends or the caller cancels. The process that holds it runs the work, the others wait, and when the leader stops, one of them takes over:

```csharp
await locks.RunAsLeaderAsync("order-sweeper", TimeSpan.FromSeconds(30), async (lease, stop) =>
{
    while (true)
    {
        await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(stop);
        await Task.Delay(TimeSpan.FromMinutes(1), stop);
    }
}, ct);
```

The token the work gets is cancelled when the caller cancels or the lease is lost. A term ends in one of three ways:

- The lease is lost: whatever the work returned or threw, the election waits for it and contends again.
- The caller cancels: the lease is released, and `RunAsLeaderAsync` throws `OperationCanceledException`, whatever the work returned or threw.
- The work returns or throws on its own: the lease is released, and `RunAsLeaderAsync` returns or throws that exception. A leader that is done hands the lock on.

An acquire that fails with a transient error, after the retries of the client, is logged, and the election contends again after a third of the lease; credentials that are not there yet count as transient. During a long outage that is a failure every third of a lease, so only the first one logs a warning (44), and the next answer of DynamoDB, the lock found free or held, logs an information (49) with how long it took and how many retries; the metric `dynasharp.lock.contention_retries` counts every one. Any other error ends it. A lease that has ended by the time its acquire answers starts no term, and the election contends again. The lease duration is also about how long the work of a leader that crashed stays undone.

Work that ignores the token runs on after the lease is lost, and another process may lead at the same time. Writes that must not happen then go through `IsHeld`, or carry the fencing token to a system that checks it.

### In a hosted service

The package has no hosting integration of its own. A service that should lead for as long as it runs calls `RunAsLeaderAsync` in a loop from a `BackgroundService`. Two kinds of failure reach that loop, and they want different answers:

- **A fault of the work**, such as a sweep that hit a bad item or a downstream service that was down. The next leader, or this process after a pause, may well succeed, so the service logs it and contends again.
- **An error of the election**, which `RunAsLeaderAsync` throws when an acquire fails with an error that is not transient: a missing table (`ResourceNotFoundException`), a role without the permissions (`AccessDeniedException`), an item at the key of the lock that is no lock (`InvalidOperationException`). Contending again changes nothing, and a service that keeps running without ever leading looks healthy while its work stays undone. So the service lets it end `ExecuteAsync`, and the host stops.

`RunAsLeaderAsync` throws what the work threw as well, so the work catches its own faults and leaves a note of them, and whatever `RunAsLeaderAsync` throws then is the election's:

```csharp
using DynaSharp.Coordination;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed partial class OrderSweeper(LockTable locks, ILogger<OrderSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Exception? fault = null;
            try
            {
                await locks.RunAsLeaderAsync("order-sweeper", Lease, async (lease, stop) =>
                {
                    try
                    {
                        await SweepAsync(lease, stop);
                    }
                    catch (Exception exception) when (!stop.IsCancellationRequested)
                    {
                        fault = exception; // A fault of the work: the lease is released, and the loop pauses.
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // The host stops: the lease is released.
            }

            // Any other exception of RunAsLeaderAsync is an error of the election. It leaves ExecuteAsync, and the host stops.
            if (fault is not null)
            {
                LogWorkFailed(logger, fault, Pause);
                await Task.Delay(Pause, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private static async Task SweepAsync(Lease lease, CancellationToken stop)
    {
        while (true)
        {
            // One round of work, each write under IsHeld(lease).
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The order sweeper failed. It contends again in {Pause}.")]
    private static partial void LogWorkFailed(ILogger logger, Exception exception, TimeSpan pause);
}
```

Register one `LockTable` per table as a singleton and the service with `AddHostedService<OrderSweeper>()`.

- **Shutdown** cancels the stopping token, which stops the work and releases the lease, so another instance takes over at its next poll, and `ExecuteAsync` returns without an exception.
- **A fault of the work** is caught inside the work while its token is not cancelled. The work then returns, `RunAsLeaderAsync` releases the lease and returns, and the loop logs the fault, pauses and contends again. Another instance may lead in the meantime. A fault after the lease was lost, while the token is cancelled, is the election's to handle: it contends again on its own, as [Leader election](#leader-election) says.
- **An error of the election** leaves `ExecuteAsync`. Since .NET 6 that stops the host (`BackgroundServiceExceptionBehavior.StopHost`, the default), which logs the exception as the failure of the service first, so an orchestrator restarts the process or reports it, and the error is not lost among the logs of a loop.
- **A transient error of an acquire**, such as throttling or a network failure, never reaches the loop: `RunAsLeaderAsync` retries it inside, with log 44 once per outage and 49 when DynamoDB answers again.

A service that should outlast an error of the election as well, such as one whose table another deployment creates later, catches `Exception` after the `OperationCanceledException`, logs it and pauses like a fault of the work. It then never stops the host, and what it logs is the only sign that it does not lead.

## Observability

The spans and metrics go to the activity source and the meter named `DynaSharp`, the ones of the client, so the subscription of [Observability](observability.md#turning-it-on) covers them. A span of a lock is the parent of the spans of the requests it sends. The renewals of the heartbeat run for the life of the lease, so each one is the root of a trace of its own, linked to the span of the acquire, and does not join the trace of the call that acquired the lease.

| Span | For |
|---|---|
| `Lock.TryAcquire`, `Lock.Acquire` | An acquire, with all its polls |
| `Lock.Renew` | A renewal, by the heartbeat or by `RenewAsync` |
| `Lock.Release` | The release of `DisposeAsync` |

| Metric | Unit | Records |
|---|---|---|
| `dynasharp.lock.acquisitions` | `{lease}` | Each lease acquired, with `dynasharp.lock.takeover` |
| `dynasharp.lock.renewals` | `{renewal}` | Each renewal, with `error.type` on one that failed |
| `dynasharp.lock.losses` | `{lease}` | Each lease lost before it was released, with `dynasharp.lock.loss_reason`: `taken_over`, `deleted`, `expired` or `table_not_found` |
| `dynasharp.lock.contention_retries` | `{retry}` | Each time `RunAsLeaderAsync` contends again after an acquire failed with a transient error, with `error.type`, also while log 44 is quiet |

The requests of a lock have spans of their own under the span of the lock, so the polls of a waiting process show as `GetItem` spans under `Lock.Acquire`, and the client's request metrics count them.

Spans and metrics carry `db.collection.name`, the table. The name of the lock is on the spans, as `dynasharp.lock.name`, and in the logs, so that an operator sees which lock it was, but not on the metrics, where it would be a dimension per lock. A span of an acquire also has `dynasharp.lock.acquired`, and for a lease `dynasharp.lock.takeover` and `dynasharp.lock.fencing_token`. Lock names are names chosen in code, as table names are, so put no data in them, such as a user id. The holder label is in the logs only, never on spans or metrics.

The log category is `DynaSharp.Coordination.LockTable`, and it logs what no caller sees:

| Id | Level | Event |
|---|---|---|
| 40 | Warning | A lease was lost, with the reason |
| 41 | Information | A lock was taken over from a holder that stopped renewing it, with the holder labels of both |
| 42 | Warning | A renewal of the heartbeat failed and is tried again after a wait, until the lease runs out |
| 43 | Warning | A release failed; the lock is free once its lease runs out |
| 44 | Warning | An acquire of `RunAsLeaderAsync` failed with a transient error; it contends again. Once per outage: the failures after it log nothing until DynamoDB answers |
| 45 | Information | This process leads, with its fencing token and its holder label |
| 46 | Information | This process no longer leads: `lease_lost`, `cancelled`, `work_returned` or `work_failed`, with its holder label |
| 47 | Debug | The work of the leader threw after its lease was lost, with the type of the exception |
| 48 | Debug | The work of the leader threw after the caller cancelled, with the type of the exception |
| 49 | Information | Contending works again after log 44: DynamoDB answered an acquire, with the time since the failure and the retries |

## Testing

A `LockTable` over a client of [`FakeDynamoDb`](testing.md) works as it does against DynamoDB, and a fake `TimeProvider` in the options of the client moves the heartbeat and the waits of a contender without a sleep. `fake.CreateTable("Shop", "pk", "sk")` creates a table with string keys for it, and `fake.CreateTable(locks.CreateTable())` the table of its own of `LockTable.Open`, from the command the service sends, at once and without a request:

```csharp
using DynaSharp.Coordination;
using DynaSharp.Testing;

var fake = new FakeDynamoDb();
using var client = fake.CreateClient();
var locks = LockTable.Open(client, "Locks");
fake.CreateTable(locks.CreateTable());
```

A test that sets up the fake before it has a `LockTable` writes `fake.CreateTable("Locks", LockTable.PartitionKeyName)`.

What other processes do to a lock, a test does with the extensions of `DynaSharp.Coordination.Testing`, without knowing the format of the item. They write through the table of the `LockTable`, so they work on the fake and on an emulator alike:

| Method | Does what |
|---|---|
| `SimulateHolderAsync(name, leaseDuration, holderLabel)` | Another process holds the lock, acquired just now and never renewed, as a holder that crashed. Returns its fencing token. A lease of this process is lost at its next renewal (`taken_over`), and a contender takes over once it has seen this holder for a whole lease |
| `SimulateReleaseAsync(name)` | The holder released the lock, whoever it was: the next acquire finds it free and raises the token. A lease of this process is lost (`taken_over`) |
| `SimulateDeleteAsync(name)` | The item is deleted, as time to live or an operator would: a lease is lost (`deleted`) and the next acquire starts the token at 1 |
| `GetStateAsync(name)` | Reads the item and returns a `LockState`: `IsHeld`, `HolderLabel`, `LeaseDuration` and `FencingToken`, or `null` for a lock without an item. A record, so a test compares it with one it builds |
| `WaitForRefusalAsync(name)` | Completes when a try of this `LockTable` next finds the lock held and is turned away: a `TryAcquireAsync` that returns `null`, or a poll of `AcquireAsync` or `RunAsLeaderAsync` that waits on. Take the task before the contender starts |

```csharp
using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;
using DynaSharp.Testing;

var fake = new FakeDynamoDb();
fake.CreateTable("Shop", "pk", "sk");
using var client = fake.CreateClient();
var locks = new LockTable(client.Table("Shop", "pk", "sk"));

await using var lease = await locks.AcquireAsync("order-sweeper", TimeSpan.FromSeconds(30));
await locks.SimulateHolderAsync("order-sweeper", TimeSpan.FromSeconds(30), "another-pod");
var stillHeld = await lease.RenewAsync(); // false: the lease is lost, and lease.Ended is cancelled
var state = await locks.GetStateAsync("order-sweeper"); // held by "another-pod", with fencing token 2
```

A test of a service that leads checks that a second instance contends and waits, instead of counting its requests, which change with how a contender polls. The second instance has a `LockTable` of its own, as a process does:

```csharp
using DynaSharp.Coordination;
using DynaSharp.Coordination.Testing;
using DynaSharp.Testing;

var fake = new FakeDynamoDb();
fake.CreateTable("Shop", "pk", "sk");
using var first = fake.CreateClient();
using var second = fake.CreateClient();
var leader = new LockTable(first.Table("Shop", "pk", "sk"));
var follower = new LockTable(second.Table("Shop", "pk", "sk"));

await using var held = await leader.AcquireAsync("order-sweeper", TimeSpan.FromSeconds(30));
var refused = follower.WaitForRefusalAsync("order-sweeper");
using var stop = new CancellationTokenSource();
var election = follower.RunAsLeaderAsync("order-sweeper", TimeSpan.FromSeconds(30), (lease, token) => Task.Delay(Timeout.Infinite, token), stop.Token);
await refused; // the follower tried, found the lock held, and waits for the next poll
await stop.CancelAsync();
await election.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); // OperationCanceledException: the caller cancelled
```

They write without a condition, so they are for tests, not for a table in production.
