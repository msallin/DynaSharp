# Design patterns

DynamoDB has no joins and few indexes, and it finds an item by its key and nothing else. So a table is designed from the questions it must answer, not from the shape of the data: list the access patterns first, such as "the orders of a customer, newest first" or "the open tickets of an agent, oldest first", and give each one a key or an index that answers it with one `Query`. Single-table design follows from that. Items that are read together share a table and a partition, whatever their type, so one request reads them, and the discriminator `_type` tells them apart.

DynaSharp was designed around a catalog of such patterns. Each one runs as a scenario test, in [the recipes](https://github.com/msallin/DynaSharp/tree/main/tests/DynaSharp.IntegrationTests/Recipes), and the examples on these pages come from those tests. They share one table: `shop` is `client.Table("Shop", "pk", "sk")`, and `shop.Of<T>()` opens it for one type. [Tables](tables.md) says how to create it.

| Pattern | What it is for |
|---|---|
| [Composite sort keys](patterns-keys.md#composite-sort-keys) | A hierarchy in one partition, read at any depth with `begins_with` |
| [Newest first with version-7 GUID sort keys](patterns-keys.md#newest-first-with-version-7-guid-sort-keys) | Sort keys that are unique and in time order, read newest first |
| [Write sharding](patterns-keys.md#write-sharding) | A key that takes more writes than one partition can, spread over several |
| [Time-bucketed partitions](patterns-keys.md#time-bucketed-partitions) | A stream of events that must not grow one partition without end |
| [Large objects](patterns-keys.md#large-objects) | A value over the item limit of 400 KB, in chunks |
| [Adjacency list and inverted index](patterns-indexes.md#adjacency-list-and-inverted-index) | Several types in one partition, and a relation walked both ways |
| [Index overloading](patterns-indexes.md#index-overloading) | One global index that answers a question per type |
| [Sparse index](patterns-indexes.md#sparse-index) | The few items in a rare state, read at the cost of those |
| [Status-keyed index](patterns-indexes.md#status-keyed-index) | "My open items, oldest first" |
| [Queue over a sparse index](patterns-indexes.md#queue-over-a-sparse-index) | Jobs that workers claim one at a time, with a reaper for a worker that stopped |
| [Soft delete](patterns-indexes.md#soft-delete) | Items that leave the lists when deleted, and can come back |
| [Optimistic locking](patterns-writes.md#optimistic-locking) | A read-modify-write that loses no update, without a lock |
| [Uniqueness constraints](patterns-writes.md#uniqueness-constraints) | A value other than the key that must be unique |
| [Idempotency keys](patterns-writes.md#idempotency-keys) | A request that a client retries, and that takes effect once |
| [Counters](patterns-writes.md#counters) | Counting on the server, and a counter that a retry cannot count twice |
| [Hi/lo sequence](patterns-writes.md#hilo-sequence) | Numeric ids without a write per id |
| [Transactional outbox](patterns-writes.md#transactional-outbox) | A change of state and the event about it, both or neither |

## More recipes

These run as tests too, with the same primitives, and each file explains its pattern at the top.

- [Vertical partitioning](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/VerticalPartitioningTests.cs): what changes often goes into an item of its own in the same partition, so a write pays for that item alone.
- [Version-0 history](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/VersionZeroHistoryTests.cs): the current state at `v0` and an immutable item per change, written in one transaction.
- [Bitemporal sort key](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/BitemporalSortKeyTests.cs): when a fact holds and when it was recorded, both in the key, so a correction overwrites nothing.
- [Sharded counter](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ShardedCounterTests.cs): a counter hotter than one item, spread over shards and summed by a fan-out.
- [Sharded "latest across everything"](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ShardedLatestTests.cs): the newest n of the whole table, from a global index over a few shards.
- [Gapless sequence](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/GaplessSequenceTests.cs): numbers without holes, as invoices need, in the transaction of the record that takes one.
- [Table per period](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/TablePerPeriodTests.cs): a table per month, dropped as a whole when its data expires.
- [Inverted index](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/InvertedIndexTests.cs): an item per term and document, for search at trivial scale.
- [Autocomplete](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/AutocompleteTests.cs): suggestions under a folded sort key, read with `begins_with` on what was typed.
- [Top-N / leaderboard](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/LeaderboardTests.cs): scores in order in a sharded global index, with a fan-out for the top.
- [Geohash proximity](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/GeohashProximityTests.cs): places in the partition of their grid cell, and "near here" as nine cells.
- [Delayed jobs](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/DelayedJobsTests.cs): jobs in the partition of the hour they are due, claimed with a conditional delete.
- [Pub/sub via subscription items](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/PubSubTests.cs): a copy of each message in an inbox per subscriber.
- [Fixed-window rate limit](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/FixedWindowRateLimitTests.cs): a counter per caller and minute, raised with a condition on the limit.
- [Token bucket](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/TokenBucketTests.cs): a rate with bursts, refilled on the client under a condition on what it read.
- [Reservation with auto-release](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ReservationTests.cs): a hold that ends by its time, with nothing that has to release it.
- [Service registry with liveness](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/ServiceRegistryTests.cs): instances that renew a time, and readers that filter on it.
- [Counting semaphore](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/CountingSemaphoreTests.cs): at most n holders across processes, an item per permit with a lease. It has no fencing token, so a holder whose lease ran out can still act while the next one holds the permit: it bounds concurrency only among holders that stop at the end of their lease.
- [Barrier](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/BarrierTests.cs): n parties add their names to a set, and exactly one sees it complete.
- [HyperLogLog](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/HyperLogLogTests.cs): distinct counts in a fixed kilobyte, merged on the client under `[Version]`.

## Locks and leader election

A distributed lock needs more than a conditional write: a heartbeat, a wait on a monotonic clock, fencing tokens. The package `DynaSharp.Coordination` has a lease on a named lock with fencing tokens, and leader election on top of it, see [Coordination](coordination.md). The [counting semaphore](https://github.com/msallin/DynaSharp/blob/main/tests/DynaSharp.IntegrationTests/Recipes/CountingSemaphoreTests.cs) above is the lighter form without fencing.

## Not in DynaSharp

Rollups through DynamoDB Streams need a consumer that keeps its position and shares the shards among processes, such as a Lambda function. [`ReadChangesAsync`](streams.md#typed-changes) reads the changes in one process, and is no such consumer.
