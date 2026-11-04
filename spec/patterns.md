# DynaSharp - design pattern support

Condensed from the DynamoDB Design Pattern Catalog (31 Oct 2026). Each pattern maps to the core primitives it needs
and to where it lives: **Recipe** = a scenario test showing the pattern with core primitives only;
**Coordination** = a first-class type in `DynaSharp.Coordination`, which holds the lease and leader election only
(decision of 2026-11-04 in `tasks.md`).

## Primitives

| # | Primitive | Milestone |
|---|---|---|
| P1 | Typed conditions and update expressions (`ADD`, set `ADD`/`DELETE`, `REMOVE`, `if_not_exists`), typed `ReturnValues` | M3 |
| P2 | Transactions with raw marker items, `.Unique(...)`, cancellation reasons mapped to actions | M3 |
| P3 | Key grammar: templates, format / parse / prefix builders, padding, order-preserving numbers, validation of separators | M2 (spike first) |
| P4 | Fan-out query over N partition keys with ordered merge (write shards, time buckets) | M3 |
| P5 | Single-table: multiple entity types, discriminator, multiple GSIs, sparse GSI attributes | M2/M3 |
| P6 | Optimistic locking via `[Version]` | M2/M3 |
| P7 | TTL epoch-seconds converter | M2 |

Sparse-attribute toggle needs no primitive of its own: it is `SET`/`REMOVE` of a GSI attribute (P1 + P5).

## Patterns

| Pattern | Primitives | Where | Caveats |
|---|---|---|---|
| Composite / hierarchical sort keys | P3 | Recipe | Zero-padding orders only non-negative integers; timestamps UTC, fixed format |
| Adjacency list (+ inverted GSI) | P3, P5 | Recipe | Mixed result types per query |
| Index overloading | P5 | Recipe | |
| Sparse index | P5, P1 | Recipe | Membership = presence of the GSI attribute |
| Write sharding | P3, P4 | Recipe | Random suffix for write spread, hash suffix for targeted reads |
| Vertical partitioning | P3, P5 | Recipe | |
| Large object pointer / chunking | P3 | Recipe | 400 KB item limit |
| Version-0 item (history) | P2, P6 | Recipe | Transactional writes cost 2x |
| Optimistic locking | P6 | Core | Opt-in per type, enforced per write |
| Uniqueness constraints | P2 | Core helper | Caller must learn which marker failed |
| Distributed lock / lease | P1, P2 | Coordination (`LockTable`, `Lease`) | Relative lease, RVN, heartbeat at lease/3, contender waits on its monotonic clock; fencing token by `ADD`, `IsHeld` in a transaction |
| Leader election | lease | Coordination (`LeaderElection`) | Fencing tokens; they only protect systems that check them |
| Counting semaphore | lease | Recipe | Counter variant leaks permits on crash |
| Barrier / rendezvous | P1 | Recipe | |
| Service registry with liveness | P3, P1 | Recipe | TTL only for cleanup, deletion lags days |
| Atomic counter | P1 | Recipe | Retries double-count; ~1000 WCU/s per item |
| Idempotent counter | P1 | Recipe | Write cost grows with the ID set (billed on the larger image); window the set |
| Hi/lo sequence | P1 | Recipe | One `ADD` claims a block; gaps on crash: fine for IDs, not invoice numbers |
| Gapless sequence | P2 | Recipe | Serialized on one item |
| Sharded counter | P3, P4 | Recipe | |
| HyperLogLog | P6 | Recipe | Binary attribute, merged client-side |
| Time-bucketed partitions | P3, P4 | Recipe | Bucket size is a parameter |
| Table per period | - | Recipe | Couples code to table naming |
| Sharded "latest across everything" | P3, P4, P5 | Recipe | |
| Rollups via Streams | Streams | Out of scope (Lambda) | |
| Time-ordered sort keys (version-7 GUID, ULID, KSUID) | P3 | Recipe | `ScanIndexForward=false` for newest first; `Guid.FirstVersion7` and `Guid.LastVersion7` bound a range of time |
| Queue via sparse GSI | P1, P5 | Recipe | Shard the GSI key; GSI is eventually consistent, the conditional claim gives correctness; GSI throttling throttles base writes; for more (visibility timeouts, delays, DLQ, FIFO groups) use SQS |
| Priority queue | P3, P5 | Recipe (queue variant) | Priority in the GSI sort key |
| Delayed / scheduled jobs | P3, P4 | Recipe | Never TTL as a timer |
| Pub/sub via subscription items | P3 | Recipe | Streams are CDC, not a bus (max ~2 readers per shard) |
| Transactional outbox | P2 | Recipe | Often the table's stream already is the outbox |
| Idempotency keys | P2, P7 | Recipe | TTL longer than the client's retry horizon |
| Fixed-window rate limit | P1, P7 | Recipe | |
| Token bucket | P1 | Recipe | Condition on observed `lastRefill` and tokens: with `lastRefill` alone, 8 concurrent takers spent 3 tokens on a frozen clock |
| Reservation with auto-release | P1, P2 | Recipe | |
| Top-N / leaderboard | P3, P4, P5 | Recipe | Score change = GSI delete + insert |
| Geohash proximity | P3, P4 | Recipe | Edge cells near poles and antimeridian |
| Autocomplete | P3 | Recipe | Hot prefixes need sharding |
| Inverted index | P3 | Recipe | Trivial scale only; use OpenSearch beyond |
| Status-keyed GSI ("my open items") | P3, P5 | Recipe | Status change rewrites the GSI key |
| Soft delete | P1, P5 | Recipe | Base-table reads still see the item |
| Bitemporal sort key | P3 | Recipe | |
