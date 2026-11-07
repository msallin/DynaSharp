<img src="https://msallin.github.io/DynaSharp/images/logo.svg" alt="" width="112" align="right">

# DynaSharp

A high-performance DynamoDB client for .NET. Items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB: every verb is an operation or an expression clause.

**Documentation: [msallin.github.io/DynaSharp](https://msallin.github.io/DynaSharp/)**, with the full guide, the [API reference](https://msallin.github.io/DynaSharp/api/DynaSharp.html) and the [diagnostics](https://msallin.github.io/DynaSharp/diagnostics.html) of the analyzers.

- **Fast:** a mapped `GetItem` or `PutItem` takes about a third of the time of EfficientDynamoDb and a sixth of the AWS SDK's `DynamoDBContext`, and a query of 100 items 9 % less than EfficientDynamoDb and a seventh of `DynamoDBContext`.
- **Lean:** fewer bytes allocated per call than EfficientDynamoDb, from 11 % fewer for a query of 100 items to 3 times fewer for a `PutItem` (1.49 KB against 4.52 KB), and 12 to 29 times fewer than the AWS SDK's `DynamoDBContext`. See [Benchmarks](#benchmarks).
- **Native AOT:** no reflection and no runtime code generation.
- **Single-table first, one table per entity just as well:** key templates, global and sparse indexes, mixed-type reads, fan-out queries, transactions across tables with typed failure reasons.
- **Compile-time safety:** a key of the wrong entity or a clause an operation does not have fails at build time, and a broken expression is a build warning.
- **No AWS SDK:** no runtime package dependencies.
- **Testable:** every request to DynamoDB goes through an `HttpMessageHandler`, and `DynaSharp.Testing` fakes the wire. Credential requests do not, so a test sets `Credentials` too.

## Getting started

### Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along, and has no other dependencies.

```shell
dotnet add package DynaSharp
dotnet add package DynaSharp.Testing        # in a test project
dotnet add package DynaSharp.Coordination   # locks and leader election, optional
```

### A first table

All requests go through `DynamoDbClient`. It is thread-safe, so create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them. [Getting started](https://msallin.github.io/DynaSharp/getting-started.html) has the whole program to copy, a local emulator to run it against, the options and the credential chain.

```csharp
using DynaSharp;

using var client = new DynamoDbClient();
```

Without a region it throws `InvalidOperationException`. To try the code below offline, with no AWS account and no Docker, take a client of `FakeDynamoDb`, the in-memory DynamoDB of [`DynaSharp.Testing`](https://msallin.github.io/DynaSharp/testing.html#a-fake-dynamodb), instead:

```csharp
using DynaSharp.Testing;

var fake = new FakeDynamoDb();
using var client = fake.CreateClient();
```

Declare a `partial` type and say how it maps. The generator writes the code that reads and writes the item, plus `Keys`, which builds keys, and `Attrs`, a typed handle per attribute. [Entities and keys](https://msallin.github.io/DynaSharp/entities.html) has the rest.

```csharp
using DynaSharp.Mapping;

[DynamoDbEntity(Table = "Shop")]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
public partial record Order
{
    public required string CustomerId { get; init; }
    public required int Number { get; init; }
    public required decimal Total { get; init; }
}
```

Open the table the entity declares once and keep it, create it if it is not there, and send commands. Every chain ends in the terminal that sends it.

```csharp
var orders = client.Table<Order>();
await orders.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);

await orders.Put(order).IfNotExists().SendAsync(ct);
Order? found = await orders.Get(Order.Keys.Of("alice", 1)).ItemAsync(ct);

await foreach (var placed in orders.Query(Order.Keys.Pk.Of("alice")).ItemsAsync(ct))
{
    Console.WriteLine(placed.Number);
}
```

The guide and the examples below use [a small shop on one table](https://msallin.github.io/DynaSharp/getting-started.html#the-shop), with `orders`, `customers`, `carts`, `emails` and `shop`. The full version is a runnable tour: start with [`samples`](https://github.com/msallin/DynaSharp/tree/main/samples).

### Coming from the AWS SDK or EfficientDynamoDb

| AWS SDK `DynamoDBContext` | EfficientDynamoDb | DynaSharp |
|---|---|---|
| `LoadAsync` | `GetItemAsync` | `orders.Get(key).ItemAsync(ct)` |
| `SaveAsync` | `PutItemAsync` | `orders.Put(order).SendAsync(ct)`, or `orders.Update(key).Set(...)` |
| `QueryAsync` | `Query<T>()` | `orders.Query(partitionKey).ItemsAsync(ct)` |
| `DeleteAsync` | `DeleteItemAsync` | `orders.Delete(key).SendAsync(ct)` |
| `ScanAsync` | `Scan<T>()` | `orders.Scan().Filter(...).ItemsAsync(ct)` |
| `CreateBatchWrite` | `BatchWrite()` | `orders.BatchWrite().PutAll(items).SendAsync(ct)` |

There is no unit of work, so no `Add` and `SaveChanges`: a command is sent where its chain ends. [Migrating](https://msallin.github.io/DynaSharp/migrating.html) has the full table, what changes for stored data, and [Adopting an existing table](https://msallin.github.io/DynaSharp/adopting.html) the rules for a table that other code wrote. [How DynaSharp compares](https://msallin.github.io/DynaSharp/comparison.html) sets the features of the three side by side.

## Examples

A few things DynaSharp does, on the shop of the sample. The guide has each of them in full: [Reading and writing](https://msallin.github.io/DynaSharp/commands.html), [Queries and scans](https://msallin.github.io/DynaSharp/queries.html), [Expressions](https://msallin.github.io/DynaSharp/expressions.html), [Transactions and batches](https://msallin.github.io/DynaSharp/transactions.html), [Tables](https://msallin.github.io/DynaSharp/tables.html) and [Testing](https://msallin.github.io/DynaSharp/testing.html). [Design patterns](https://msallin.github.io/DynaSharp/patterns.html) shows the common DynamoDB designs, such as single-table, sharding, idempotency and the outbox, each with its code.

**Typed queries**, with keys and filters that the compiler checks. [Query](https://msallin.github.io/DynaSharp/queries.html#query)

```csharp
var bigOrders = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Total.Gt(100m))
    .Descending()
    .ItemsAsync(ct);
```

**Updates that say what they change**, down into a nested document. [UpdateItem](https://msallin.github.io/DynaSharp/commands.html#updateitem)

```csharp
var moved = await customers.Update(Customer.Keys.Of("alice"))
    .Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), "Bern")
    .Add(Customer.Attrs.Interests, ["keyboards"])
    .IfExists()
    .ReturnNew()
    .SendAsync(ct);
```

**Transactions with typed failure reasons**: a marker tells which action failed. [TransactWriteItems](https://msallin.github.io/DynaSharp/transactions.html#transactwriteitems)

```csharp
TransactMarker emailTaken = default;
try
{
    await client.TransactWrite()
        .Unique(emails, new EmailClaim(customer.Email, customer.Id), out emailTaken).ReturnOldOnFailure()
        .Put(customers, customer).IfNotExists()
        .SendAsync(ct);
}
catch (TransactionCanceledException exception) when (exception.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
{
    Console.WriteLine(exception.ItemFor(emailTaken)!.To<EmailClaim>().CustomerId);
}
```

**Several types in one query**, each item as its own type. [Several types in one query](https://msallin.github.io/DynaSharp/queries.html#several-types-in-one-query)

```csharp
await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of<Customer, Order>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(profile => profile.Name, order => $"#{order.Number}"));
}
```

**Optimistic locking, and a retry on a conflict**, for a read-modify-write that loses to another writer. [Optimistic locking](https://msallin.github.io/DynaSharp/commands.html#optimistic-locking)

`Cart` has a `[Version] public long Revision`. A `Put` checks that the stored cart holds the version it was read with and stores the next one, so the losing writer gets a `ConditionalCheckFailedException` and `RetryOnConflictAsync` runs the lambda again with a fresh read.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

**Indexes, pages and batches.** `byStatus` is `orders.Index(Order.Indexes.ByStatus)`, the index that `[GlobalIndexKey("ByStatus", ...)]` declares on `Order`. [Paging for web APIs](https://msallin.github.io/DynaSharp/queries.html#paging-for-web-apis), [Batches](https://msallin.github.io/DynaSharp/transactions.html#batches)

```csharp
var page = await byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed)).Limit(25).PageAsync(ContinuationToken.Parse(after), ct);
string? next = page.HasMore ? page.Next.ToString() : null;

await products.BatchWrite().PutAll(catalog).SendAsync(ct);
var found = await products.BatchGet().GetAll(skus.Select(Product.Keys.Of)).SendAsync(ct);
```

**Also:** batches of any size, one list over many partitions, paging tokens for web APIs, `ToRequestJson()` to see what goes over the wire, converters of your own, `DynamoDbStreamsClient` to read [DynamoDB Streams](https://msallin.github.io/DynaSharp/streams.html), one shard or a whole stream, and the changes of a table as entities ([`ReadChangesAsync`](https://msallin.github.io/DynaSharp/streams.html#typed-changes)), locks with fencing tokens and leader election in [`DynaSharp.Coordination`](https://msallin.github.io/DynaSharp/coordination.html), and tests without DynamoDB through [`DynaSharp.Testing`](https://msallin.github.io/DynaSharp/testing.html), which fakes the wire:

```csharp
using var handler = new StubHandler(_ => StubResponse.Item(order));
using var client = StubClient.Create(handler);
```

## API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry. [API levels](https://msallin.github.io/DynaSharp/api-levels.html) says more.

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `client.Table("Shop", "pk", "sk")`, then `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
| **Requests** (`using DynaSharp.Requests`) | `client.GetItemAsync(new GetItemRequest { ... })` | Every DynamoDB operation as a request and a response that mirror the service. |
| **Raw** | `client.SendRawAsync("ListTables", """{}"""u8.ToArray())` | UTF-8 JSON in, UTF-8 JSON out, through the same signing and retries. |

## Design principles

- **Stay true to DynamoDB.** Every verb is an operation or a clause.
- **Nothing hidden.** What the client adds shows up in `ToRequestJson()`.
- **Mistakes belong to the compiler.** Types rule out what they can, and analyzers check the rest.
- **Pay only for what you use.** A cost that shows up in a benchmark is a bug until it is explained.
- **No reflection.** The source generator produces the mapping, so Native AOT works.
- **Keys are data on disk.** Changing an encoding is a migration.
- **Retries are honest.** The docs say where a retry can apply a write twice.
- **Tests answer to someone else.** Wire formats are checked against botocore and real AWS responses.

[Design principles](https://msallin.github.io/DynaSharp/design-principles.html) says what each one means for your code, and [How it is built](https://msallin.github.io/DynaSharp/design.html) why the client looks the way it does.

## Benchmarks

Allocations are exact. Times depend on the machine: they are the means of one run on an idle 8-core AMD EPYC with .NET 11 RC 3, each within 3 %. A KB is 1,024 bytes. All clients run on the same canned responses with no network, so these numbers show the client's own cost. [Performance](https://msallin.github.io/DynaSharp/performance.html) explains why the rows differ and how to run them.

Time per call, mapped APIs, in µs:

| Call | DynaSharp | EfficientDynamoDb | AWS `DynamoDBContext` |
|---|---|---|---|
| GetItem | 5.5 | 15.7 | 31.8 |
| PutItem | 4.9 | 15.5 | 31.2 |
| Query, 100 items | 126 | 139 | 878 |

Bytes allocated per call, mapped APIs:

| Call | DynaSharp | EfficientDynamoDb | AWS `DynamoDBContext` |
|---|---|---|---|
| GetItem | 2,384 | 5,456 | 40,778 |
| PutItem | 1,528 | 4,632 | 44,403 |
| Query, 100 items | 81,120 | 91,329 | 990,973 |

Low-level APIs, in KB:

| Call | DynaSharp request API | EfficientDynamoDb | AWS SDK |
|---|---|---|---|
| GetItem | 2.55 | 6.69 | 30.49 |
| PutItem | 1.60 | 4.52 | 26.68 |
| Query, 100 items | 98.03 | 201.74 | 379.07 |

Inside DynaSharp, by API level, in bytes:

| Call | Generated entity | `Table<Item>` | Request API |
|---|---|---|---|
| GetItem | 2,256 | 2,576 | 2,608 |
| PutItem | 1,528 | n/a | 1,640 |
| Query, 100 items | 68,320 | 100,320 | 100,456 |

## Compatibility

Until 1.0, a minor release can break the public API, behaviour and analyzers, and DynaSharp prefers the right design over compatibility; the release notes say what to change. A patch release fixes and does not break. What an entity writes to a table changes only with a release note that says how to migrate. [Compatibility](https://msallin.github.io/DynaSharp/compatibility.html) has the details.

## Building

The exact SDK is pinned in `global.json`.

```shell
dotnet tool restore
dotnet build DynaSharp.slnx
dotnet test --project tests/DynaSharp.UnitTests
dotnet test --project tests/DynaSharp.IntegrationTests   # the in-memory fake; DYNASHARP_BACKENDS=all adds the emulators, in Docker
dotnet stryker                                           # mutation testing
```

The documentation site is built with DocFX from `docs/`: `dotnet build src/DynaSharp.Testing -c Release` and `dotnet build src/DynaSharp.Coordination -c Release`, then `dotnet docfx docs/docfx.json --serve`.

## License

MIT
