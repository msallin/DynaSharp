# DynaSharp.Coordination

Distributed coordination on DynamoDB for [DynaSharp](https://github.com/msallin/DynaSharp): a lease on a named lock with fencing tokens, and leader election on top of it. The semantics are those of the DynamoDB Lock Client: a relative lease duration, a record version number, and a wait on the monotonic clock of the contender, so the clocks of the machines may differ by any amount.

## A lease

```csharp
var locks = new LockTable(client.Table("Shop", "pk", "sk"));

await using var lease = await locks.AcquireAsync("nightly-report", TimeSpan.FromSeconds(30), ct);
await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(ct);
```

The lock is one item in a table you already have. The lease renews itself every third of its duration, `lease.Lost` is cancelled once it is no longer held, and disposing it releases the lock. `IsHeld(lease)` lets a transaction commit only while the lease holds the lock, and `lease.FencingToken` rises with every acquire, for a system outside DynamoDB that checks it.

## Leader election

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

One process at a time runs the work. When its lease is lost, the work is stopped and another process takes over.

The guide is at [msallin.github.io/DynaSharp/coordination.html](https://msallin.github.io/DynaSharp/coordination.html): the item a lock writes, clocks and takeover, fencing, and the spans, metrics and logs.
