<img src="https://msallin.github.io/DynaSharp/images/logo.svg" alt="" width="112" align="right">

# DynaSharp

A high-performance DynamoDB client for .NET. Items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB: every verb is an operation or an expression clause.

**Documentation: [msallin.github.io/DynaSharp](https://msallin.github.io/DynaSharp/)**, with the full guide, the [API reference](https://msallin.github.io/DynaSharp/api/DynaSharp.html) and the [diagnostics](https://msallin.github.io/DynaSharp/diagnostics.html) of the analyzers.

- **Fast:** a mapped `GetItem` or `PutItem` takes about a third of the time of EfficientDynamoDb and a sixth of the AWS SDK's `DynamoDBContext`, and a query of 100 items 9 % less than EfficientDynamoDb and a seventh of `DynamoDBContext`.
- **Lean:** fewer bytes allocated per call than EfficientDynamoDb, from 11 % fewer for a query of 100 items to 3 times fewer for a `PutItem` (1.49 KB against 4.52 KB), and 12 to 29 times fewer than the AWS SDK's `DynamoDBContext`. See [Performance](https://msallin.github.io/DynaSharp/performance.html#benchmarks).
- **Native AOT:** no reflection and no runtime code generation, see [Native AOT](https://msallin.github.io/DynaSharp/getting-started.html#native-aot).
- **Single-table first, one table per entity just as well:** key templates, global and sparse indexes, mixed-type reads, fan-out queries, transactions across tables with typed failure reasons.
- **Compile-time safety:** a key of the wrong entity or a clause an operation does not have fails at build time, and a broken expression is a build warning.
- **No AWS SDK:** no runtime package dependencies.
- **Testable:** every request to DynamoDB goes through an `HttpMessageHandler`, and `DynaSharp.Testing` fakes the wire, with a client that reads nothing from the environment.

## Getting started

### Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along, and has no other dependencies.

```shell
dotnet add package DynaSharp
dotnet add package DynaSharp.Testing        # in a test project
dotnet add package DynaSharp.Coordination   # locks and leader election, optional
```

### A first table

All requests go through `DynamoDbClient`: create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them. To try things offline, with no AWS account and no Docker, take a client of the in-memory [`FakeDynamoDb`](https://msallin.github.io/DynaSharp/testing.html#a-fake-dynamodb) instead.

```csharp
using DynaSharp;
using DynaSharp.Testing;

using var client = new DynamoDbClient();
// or, in memory: using var client = new FakeDynamoDb().CreateClient();
```

Declare a `partial` type and say how it maps. The generator writes the code that reads and writes the item, plus `Keys`, which builds keys, and `Attrs`, a typed handle per attribute, see [Entities and keys](https://msallin.github.io/DynaSharp/entities.html).

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

Open the table, create it if it is not there, and send commands. A command goes out with its last call: `SendAsync`, `ItemAsync` or `ItemsAsync`.

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

[Getting started](https://msallin.github.io/DynaSharp/getting-started.html) has the whole program, a local emulator, the options and the credential chain. The examples below use [a small shop on one table](https://msallin.github.io/DynaSharp/getting-started.html#the-shop), which [`samples`](https://github.com/msallin/DynaSharp/tree/main/samples) runs as a tour.

## Examples

A few things DynaSharp does, on the shop of the sample; the guide has each in full. [Design patterns](https://msallin.github.io/DynaSharp/patterns.html) shows the common DynamoDB designs, such as single-table, sharding, idempotency and the outbox, each with its code.

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

**Several types in one query**, each item as its own type. [Mixed reads](https://msallin.github.io/DynaSharp/queries.html#several-types-in-one-query)

```csharp
await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of<Customer, Order>().ItemsAsync(ct))
{
    Console.WriteLine(item switch
    {
        Customer profile => profile.Name,
        Order placed => $"#{placed.Number}",
    });
}
```

**Optimistic locking, and a retry on a conflict.** With `[Version]` on a property, a write of a copy that changed since it was read throws `ConditionalCheckFailedException`, and `RetryOnConflictAsync` runs the read-modify-write again with a fresh read. [Optimistic locking](https://msallin.github.io/DynaSharp/commands.html#optimistic-locking), [Retry on a conflict](https://msallin.github.io/DynaSharp/commands.html#retry-on-a-conflict)

**Indexes, pages and batches.** `byStatus` is `orders.Index(Order.Indexes.ByStatus)`, which `[GlobalIndexKey("ByStatus", ...)]` declares on `Order`. [Paging for web APIs](https://msallin.github.io/DynaSharp/queries.html#paging-for-web-apis), [Batches](https://msallin.github.io/DynaSharp/transactions.html#batches)

```csharp
ContinuationToken? position = null;
Page<Order> page;
do
{
    page = await byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed)).Limit(25).PageAsync(position, ct);
    Console.WriteLine(page.Items.Count);
    position = page.Next;
}
while (page.HasMore);

await products.BatchWrite().PutAll(catalog).SendAsync(ct);
var found = await products.BatchGet().GetAll(skus.Select(Product.Keys.Of)).SendAsync(ct);
```

**Also:** one list over many partitions, `ToRequestJson()` to see what goes over the wire, converters of your own, [DynamoDB Streams](https://msallin.github.io/DynaSharp/streams.html) read as entities, locks with fencing tokens and leader election in [`DynaSharp.Coordination`](https://msallin.github.io/DynaSharp/coordination.html), and tests without DynamoDB through [`DynaSharp.Testing`](https://msallin.github.io/DynaSharp/testing.html), which fakes the wire:

```csharp
using var handler = new StubHandler(_ => StubResponse.Item(order));
using var client = StubClient.Create(handler);
```

## API levels

One client and one pipeline, with four ways in. Use the highest level that fits, and drop down for one call: all levels share a client, its retries and its telemetry, see [API levels](https://msallin.github.io/DynaSharp/api-levels.html).

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

## Compatibility

Until 1.0, a minor release can break the public API, behaviour and analyzers, and the release notes say what to change; a patch release does not break. What an entity writes to a table changes only with a release note that says how to migrate. [Compatibility](https://msallin.github.io/DynaSharp/compatibility.html) has the details.

## Building

The exact SDK is pinned in `global.json`.

```shell
dotnet tool restore
dotnet build DynaSharp.slnx
dotnet test --project tests/DynaSharp.UnitTests
dotnet test --project tests/DynaSharp.IntegrationTests   # the in-memory fake; DYNASHARP_BACKENDS=all adds the emulators, in Docker
dotnet stryker                                           # mutation testing
```

The documentation site is built with DocFX from `docs/`: build `src/DynaSharp.Testing` and `src/DynaSharp.Coordination` with `-c Release`, then `dotnet docfx docs/docfx.json --serve`.
