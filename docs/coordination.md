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

The table is opened without a model, and its key attributes must be strings. The item of the lock `nightly-report` has the partition key `LOCK#nightly-report` and, if the table has a sort key, the sort key `LOCK`. Its attributes have fixed names, because every process that contends for a lock must read the item the same way:

| Attribute | Holds |
|---|---|
| `_type` | `LOCK`, so that a scan of a single table can tell a lock from an entity |
| `owner` | The holder, a new id for every acquire. Absent while the lock is free |
| `rvn` | The record version number, a new id on every acquire and renewal |
| `fence` | The fencing token, raised by one with `ADD` on every acquire |
| `leaseMs` | The duration of the lease in milliseconds. Absent while the lock is free |

A release keeps the item, and it has no time to live, so the fencing token never goes back. Deleting the item starts the token at 1 again.

Hold one `LockTable` per table in a process. It remembers the holders it has seen, which a wait for a lock depends on. Two instances in one process contend like two processes.

## Acquire and release

`AcquireAsync` waits until it has the lock, `TryAcquireAsync` tries once and returns `null` if another holder has it. Disposing the lease releases the lock:

```csharp
await using var lease = await locks.AcquireAsync("nightly-report", TimeSpan.FromSeconds(30), ct);
using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Lost);

await foreach (var placed in byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed)).ItemsAsync(stop.Token))
{
    await client.TransactWrite().IsHeld(lease).Put(orders, placed with { Status = OrderStatus.Shipped }).SendAsync(stop.Token);
}
```

```csharp
var lease = await locks.TryAcquireAsync("nightly-report", TimeSpan.FromSeconds(30), ct);
if (lease is null)
{
    return; // Another process runs the report.
}

await using (lease)
{
    // ...
}
```

The lease duration runs from 1 second to 1 day. A lease renews itself every third of its duration until it is disposed or lost, so it outlasts two renewals that fail. `RenewAsync` renews it at once, before a step that must not start on a lease that is about to run out, and returns `false` if the lease is lost.

A release is one conditional `UpdateItem` that removes the owner, on condition that the owner is still this lease, so a lock that someone else took over is left alone. A release that fails is logged, not thrown, and the lock is free once the lease runs out.

## How a lease ends

`Lost` is a `CancellationToken` that is cancelled once the lease is no longer held. Pass it to the work that needs the lock. The lease is lost:

- when a renewal finds another owner in the item, because a contender took it over or someone wrote the item;
- when a renewal fails twice in a row, after the retries of the client, or DynamoDB answers `ResourceNotFoundException`;
- when the lease runs out on the monotonic clock without a renewal that succeeded, for example while DynamoDB does not answer.

Disposing the lease cancels `Lost` too. Losing it is final: acquire the lock again for a new lease.

## Clocks and takeover

A holder that crashed does not release its lock. A contender takes the lock over once it has seen the same record version for a whole lease, measured on its own monotonic clock (`TimeProvider.GetTimestamp` of the client) from the moment it first read that version. The holder counts its lease from the moment it sent the write, which is before the contender can have read it. So on the holder's clock the lease always ends before a contender may take the lock over, whatever the wall clocks of the two machines say. What this assumes is that both clocks run at about the same rate.

While another holder has the lock, `AcquireAsync` polls every third of that holder's lease, or sooner if the lease runs out before. A lock that is released is acquired at the next poll; a lock whose holder crashed is taken over after a whole lease.

A takeover raises the fencing token, as every acquire does. If an acquire is retried by the client after its response was lost, the retry finds its own owner in the item, and the lease is the one the first attempt took. An acquire that is cancelled after its write reached DynamoDB leaves a lock that nobody holds until its lease runs out.

## Fencing

A lease can run out while its holder is paused, by a garbage collection or a slow network, and the holder does not know it until it next looks at `Lost`. By then another process may hold the lock. A fencing token is how a write refuses the old holder: every acquire raises it, so the newer holder always has the higher one.

For writes to DynamoDB, `IsHeld(lease)` adds a condition check on the lock item to a transaction, so the transaction commits only while the lease still holds the lock, with this owner and this fencing token:

```csharp
await client.TransactWrite()
    .IsHeld(lease)
    .Put(orders, order)
    .SendAsync(ct);
```

The check reads the item when the transaction commits, which is what makes it safe; `Lost` is only what this process knows. A transaction that fails it throws `TransactionCanceledException`, and the marker tells a lost lease from another reason:

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

The lock item must be in the same account and region as the items of the transaction. A system outside DynamoDB, such as a file store or another database, protects itself only if it checks the token: it keeps the highest `FencingToken` it has seen and refuses a write with a lower one. DynaSharp cannot enforce that for it.

## Leader election

`LeaderElection.RunAsync` contends for a lock until the caller cancels. The process that holds it runs the work, the others wait, and when the leader stops, one of them takes over:

```csharp
await LeaderElection.RunAsync(locks, "order-sweeper", TimeSpan.FromSeconds(30), async (lease, stop) =>
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
- The caller cancels: the lease is released, and `RunAsync` throws `OperationCanceledException`.
- The work returns or throws on its own: the lease is released, and `RunAsync` returns or throws that exception. A leader that is done hands the lock on.

An acquire that fails with a transient error, after the retries of the client, is logged, and the election contends again after a third of the lease. Any other error ends it. The lease duration is also about how long the work of a leader that crashed stays undone.

Work that ignores the token runs on after the lease is lost, and another process may lead at the same time. Writes that must not happen then go through `IsHeld`, or carry the fencing token to a system that checks it. In a hosted application, call `RunAsync` from `ExecuteAsync` of a `BackgroundService` with its stopping token; the package has no hosting integration of its own.

## Observability

The spans and metrics go to the activity source and the meter named `DynaSharp`, the ones of the client, so the subscription of [Observability](observability.md#turning-it-on) covers them. A span of a lock is the parent of the spans of the requests it sends.

| Span | For |
|---|---|
| `Lock.TryAcquire`, `Lock.Acquire` | An acquire, with all its polls |
| `Lock.Renew` | A renewal, by the heartbeat or by `RenewAsync` |
| `Lock.Release` | The release of `DisposeAsync` |

| Metric | Unit | Records |
|---|---|---|
| `dynasharp.lock.acquisitions` | `{lease}` | Each lease acquired, with `dynasharp.lock.takeover` |
| `dynasharp.lock.renewals` | `{renewal}` | Each renewal, with `error.type` on one that failed |
| `dynasharp.lock.losses` | `{lease}` | Each lease lost before it was released, with `dynasharp.lock.loss_reason`: `taken_over`, `expired`, `renew_failed` or `table_not_found` |

Spans and metrics carry `db.collection.name`, the table, and `dynasharp.lock.name`, the name of the lock. A span of an acquire also has `dynasharp.lock.acquired`, and for a lease `dynasharp.lock.takeover` and `dynasharp.lock.fencing_token`. Lock names go into telemetry as table names do: they are names chosen in code, so put no data in them, such as a user id.

The log category is `DynaSharp.Coordination.LockTable`, and it logs what no caller sees:

| Id | Level | Event |
|---|---|---|
| 40 | Warning | A lease was lost, with the reason |
| 41 | Information | A lock was taken over from a holder that stopped renewing it |
| 42 | Warning | A renewal failed and is tried once more at once |
| 43 | Warning | A release failed; the lock is free once its lease runs out |
| 44 | Warning | An acquire of the election failed with a transient error; it contends again |
| 45 | Information | This process leads, with its fencing token |
| 46 | Information | This process no longer leads: `lease_lost`, `cancelled`, `work_returned` or `work_failed` |

## Testing

A `LockTable` over a client of [`FakeDynamoDb`](testing.md) works as it does against DynamoDB, and a fake `TimeProvider` in the options of the client moves the heartbeat and the waits of a contender without a sleep.
