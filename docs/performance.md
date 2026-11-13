# Performance

What a call costs comes in two parts: what the client allocates and spends on the CPU, and what DynamoDB charges in capacity and time. The benchmarks measure the first. The list below says what drives both.

## Benchmarks

Allocations are exact. Times depend on the machine: they are the means of one run on an idle 8-core AMD EPYC with .NET 11 (SDK 11.0.100), each within 3 %. A KB is 1,024 bytes. All clients run on the same canned responses with no network, so these numbers show the client's own cost.

Time per call, mapped APIs, in µs:

| Call | DynaSharp | EfficientDynamoDb | AWS `DynamoDBContext` |
|---|---|---|---|
| GetItem | 4.9 | 14.0 | 24.9 |
| PutItem | 4.5 | 13.6 | 25.5 |
| Query, 100 items | 113 | 119 | 701 |

Bytes allocated per call, mapped APIs:

| Call | DynaSharp | EfficientDynamoDb | AWS `DynamoDBContext` |
|---|---|---|---|
| GetItem | 2,384 | 5,456 | 36,041 |
| PutItem | 1,528 | 4,632 | 39,667 |
| Query, 100 items | 81,120 | 91,329 | 986,217 |

Low-level APIs, in KB:

| Call | DynaSharp request API | EfficientDynamoDb | AWS SDK |
|---|---|---|---|
| GetItem | 2.55 | 6.69 | 25.86 |
| PutItem | 1.60 | 4.52 | 22.06 |
| Query, 100 items | 98.03 | 201.74 | 374.44 |

Inside DynaSharp, by API level, in bytes:

| Call | Generated entity | `Table<Item>` | Request API |
|---|---|---|---|
| GetItem | 2,256 | 2,576 | 2,608 |
| PutItem | 1,528 | n/a | 1,640 |
| Query, 100 items | 68,320 | 100,320 | 100,456 |

### Why the rows differ

- The item has a set of tags. The first two tables map it to a `HashSet<string>` in all three clients, so that they compare. The fourth maps it to a `string[]`, which is lighter, so a read costs less there, and a `PutItem`, which only walks the set, costs the same.
- A read costs more than a write, because a read builds the item it returns and a write has only a request to fill. A query of 100 items is mostly 100 times what one item costs.
- A generated entity reads straight into its properties. `Table<Item>` and the request API hold each attribute as an `AttributeValue`, which is the step between the first column of the fourth table and the other two. The `Item` of a `PutItem` is the object of the caller, so that cell is n/a.
- The third table and the last column of the fourth are the same request API, in KB and in bytes. They come from two benchmark classes, and the `Query` of the fourth runs in an `async` method of the benchmark, which adds its `Task`.

### Running them

To run them, from the repository root:

```shell
dotnet run -c Release --project benchmarks/DynaSharp.Benchmarks
```

The benchmarks live in [`benchmarks/DynaSharp.Benchmarks`](https://github.com/msallin/DynaSharp/tree/main/benchmarks/DynaSharp.Benchmarks).

## What costs what

**In the client**

- **One client**, kept for the life of the application, see [Configure the client](getting-started.md#configure-the-client). A second client opens connections of its own and, unless both get one provider, makes a second set of requests to STS or the metadata service.
- **A command** writes its request as you call it, into buffers from a pool that go back when it is sent. That is why it is built and sent in one expression (`DYNA201`).
- **A key.** `Order.Keys.Of(...)` holds its values and makes no string until the command writes it, and `TryOf` makes none either. `Order.Keys.Pk.Of(...)` and `Format` make the string of the key. A plain string or number key costs nothing beyond itself.
- **A condition** of handles, `Order.Attrs.Total.Gt(100m).And(...)`, is a struct and allocates nothing. `Condition.Of(...)` puts one on the heap, for a field. A sort key condition of constant text allocates nothing, and one that formats a value costs its strings.
- **A typed read** goes from JSON straight into the entity. `Table<Item>` and the request API build an `AttributeValue` per attribute first, and `To<T>()` reads that again, see [Without a model](without-a-model.md).
- **An `Item` built by hand** is cheap at the usual sizes, and above 32 attributes costs more than a `Dictionary` of known capacity.
- **Telemetry** without a listener costs a null check per call. With one, the tags of each span and measurement, see [Observability](observability.md).
- **`ToRequestJson()`** renders the whole request into a string, for tests and debugging.

**In DynamoDB**

- **A filter** runs after the read, so a `Query` or a `Scan` pays for every item it reads, whatever the filter leaves: `ScannedCount` against `Count`. A key condition is what reads less. The filter on `_type` that a typed read of a shared table adds costs nothing beyond the read.
- **A `Scan`** reads the whole table. A lookup that runs often needs a key: an index, or a claim item.
- **`Limit(n)`** is per page and counts before the filter, so it bounds a request, not the result. `Take(n)` stops reading once it has `n`, and asks each page for no more than are still to come, so the last page reads no more than it gives. `ItemAsync` on a query without a filter, `Limit` or `Take` sends `Limit: 1`, so it reads and pays for one item; with a filter it adds no limit and follows empty filtered pages. A read of an entity with a discriminator filters on it, so there `.Limit(1)` is yours to add.
- **`CountAsync`** reads like a query of the items and charges for them, without returning them.
- **`Project` and `Select`** save bandwidth and the cost of reading into the entity, not capacity: DynamoDB charges for the whole item.
- **`Consistent()`** reads cost twice the capacity of an eventually consistent read.
- **A transaction** costs twice: a write is prepared and committed, a read is the same.
- **A batch** sends up to 25 writes or 100 gets per request, one request after the other, and sends again what DynamoDB left unprocessed. It saves round trips, not capacity.
- **A fan-out query** sends one query per partition, up to `MaxConcurrency`, 8 by default, reads a partition only when the merge needs it, and holds at most one page per partition.
- **`Parallel(segments)`** scans that many segments at once, which is faster and charges the same.
- **A retry** waits before it goes out, and a throttled table makes every call wait, see [Errors and retries](errors.md#retries). `.ReturnCapacity()` says what a call consumed, and the metric `dynasharp.client.consumed_capacity` sums it.
