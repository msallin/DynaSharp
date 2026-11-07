# DynaSharp.Coordination

Distributed coordination on DynamoDB for [DynaSharp](https://github.com/msallin/DynaSharp): a lease on a named lock with fencing tokens, and leader election on top of it. The semantics are those of the DynamoDB Lock Client: a relative lease duration, a record version number, and a wait on the monotonic clock of the contender, so the clocks of the machines may be set apart by any offset, as long as they run at about the same rate.

## A lease

```csharp
var locks = new LockTable(client.Table("Shop", "pk", "sk"));

await using var lease = await locks.AcquireAsync("nightly-report", TimeSpan.FromSeconds(30), ct);
await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(ct);
```

The lock is one item in a table you already have; `LockTable.For(client.Table<Order>("Shop"))` takes a typed table without restating its keys. A table of locks alone is `LockTable.Open(client, "Locks")`, whose `CreateTable()` creates it. `AcquireAsync(name, lease, wait)` gives up with a `TimeoutException` after the wait. The lease renews itself every third of its duration, `lease.Ended` is cancelled once it is no longer held, and disposing it releases the lock. `IsHeld(lease)` lets a transaction commit only while the lease holds the lock, and `lease.FencingToken` rises with every acquire, for a system outside DynamoDB that checks it.

## Leader election

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

One process at a time runs the work. When its lease is lost, the work is stopped and another process takes over. Code deep in the work fences its writes with `IsHeld()`, which checks `Lease.Current`, the lease of the term it runs in, without the lease passed down to it. The processes that wait read the lock item and do not write it while the leader renews, so they cause no failed conditional writes.

The guide is at [msallin.github.io/DynaSharp/coordination.html](https://msallin.github.io/DynaSharp/coordination.html): the item a lock writes, a holder label for operators, expiry of lock items, clocks and takeover, fencing, a `BackgroundService` that leads for as long as it runs, the spans, metrics and logs, and the helpers of `DynaSharp.Coordination.Testing` that let a test hold, release or delete a lock as another process would, read its state, and see a contender turned away.
