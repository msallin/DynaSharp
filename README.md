# DynaSharp

A high-performance DynamoDB client for .NET. Items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB: every verb is an operation or an expression clause.

**Documentation: [msallin.github.io/DynaSharp](https://msallin.github.io/DynaSharp/)**, with the full guide, the [API reference](https://msallin.github.io/DynaSharp/api/DynaSharp.html) and the [diagnostics](https://msallin.github.io/DynaSharp/diagnostics.html) of the analyzers.

- **Lean:** a `PutItem` allocates 1.5 KB against 5.1 KB for EfficientDynamoDb and 44.4 KB for the AWS SDK's `DynamoDBContext`. See [Benchmarks](#benchmarks).
- **Native AOT:** no reflection and no runtime code generation.
- **Single-table first:** key templates, global and sparse indexes, mixed-type reads, fan-out queries, transactions with typed failure reasons.
- **Compile-time safety:** a key of the wrong entity or a clause an operation does not have fails at build time, and a broken expression is a build warning.
- **No AWS SDK:** no runtime package dependencies.
- **Testable:** every request to DynamoDB goes through an `HttpMessageHandler`, and `DynaSharp.Testing` fakes the wire. Credential requests do not, so a test sets `Credentials` too.

## Getting started

### Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along.

There is no public feed. Pack a clone of this repository into a folder, make that folder a source, and add the package from it. A clone without a tag packs a prerelease version, so pin the version the folder holds: `--prerelease` takes the newest.

```shell
dotnet pack src/DynaSharp -c Release -o /path/to/packages
dotnet pack src/DynaSharp.Testing -c Release -o /path/to/packages
dotnet nuget add source /path/to/packages --name dynasharp
dotnet add package DynaSharp --version 0.1.0-preview.4
```

The default .NET `.gitignore` holds `*.nupkg`, so a folder source in a repository needs `git add -f`, and a filled package cache hides the loss until a clean machine restores. On Windows `PublishAot` needs the C++ workload of Visual Studio.

### A first table

All requests go through `DynamoDbClient`. It is thread-safe, so create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them. [Getting started](https://msallin.github.io/DynaSharp/getting-started.html) has the options, the credential chain and a local emulator.

```csharp
using DynaSharp;

using var client = new DynamoDbClient();
```

Declare a `partial` type and say how it maps. The generator writes the code that reads and writes the item, plus `Keys`, which builds keys, and `Attrs`, a typed handle per attribute. [Entities and keys](https://msallin.github.io/DynaSharp/entities.html) has the rest.

```csharp
using DynaSharp.Mapping;

[DynamoDbEntity]
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

Open a table once and keep it, create it if it is not there, and send commands. Every chain ends in the terminal that sends it.

```csharp
var orders = client.Table<Order>("Shop");
await orders.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);

await orders.Put(order).IfNotExists().SendAsync(ct);
Order? found = await orders.Get(Order.Keys.Of("alice", 1)).ItemAsync(ct);

await foreach (var placed in orders.Query(Order.Keys.Pk.Of("alice")).ItemsAsync(ct))
{
    Console.WriteLine(placed.Number);
}
```

The guide uses a small shop on one table. The full version is a runnable tour: start with [`samples`](https://github.com/msallin/DynaSharp/tree/main/samples).

### Coming from the AWS SDK or EfficientDynamoDb

| AWS SDK `DynamoDBContext` | EfficientDynamoDb | DynaSharp |
|---|---|---|
| `LoadAsync` | `GetItemAsync` | `orders.Get(key).ItemAsync(ct)` |
| `SaveAsync` | `PutItemAsync` | `orders.Put(order).SendAsync(ct)`, or `orders.Update(key).Set(...)` |
| `QueryAsync` | `Query<T>()` | `orders.Query(partitionKey).ItemsAsync(ct)` |
| `DeleteAsync` | `DeleteItemAsync` | `orders.Delete(key).SendAsync(ct)` |
| `ScanAsync` | `Scan<T>()` | `orders.Scan().Filter(...).ItemsAsync(ct)` |
| `CreateBatchWrite` | `BatchWrite()` | `orders.BatchWrite().PutAll(items).SendAsync(ct)` |

There is no unit of work, so no `Add` and `SaveChanges`: a command is sent where its chain ends. [Migrating](https://msallin.github.io/DynaSharp/migrating.html) has the full table, what changes for stored data, and [Adopting an existing table](https://msallin.github.io/DynaSharp/adopting.html) the rules for a table that other code wrote.

## Examples

A few things DynaSharp does, on the shop of the sample. The guide has each of them in full: [Reading and writing](https://msallin.github.io/DynaSharp/commands.html), [Queries and scans](https://msallin.github.io/DynaSharp/queries.html), [Expressions](https://msallin.github.io/DynaSharp/expressions.html), [Transactions and batches](https://msallin.github.io/DynaSharp/transactions.html), [Tables](https://msallin.github.io/DynaSharp/tables.html) and [Testing](https://msallin.github.io/DynaSharp/testing.html).

**Typed queries.** `ItemsAsync` follows pages for you, including pages a filter left empty. `Keys` builds the key, and `Attrs` makes the filter typed, so comparing a total with a string does not compile.

```csharp
var bigOrders = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Total.Gt(100m))
    .Descending()
    .ItemsAsync(ct);
```

**Updates that say what they change.** An update changes what it names and leaves the rest. `At` walks into a nested document, and `ReturnNew` gives back the item in one round trip.

```csharp
var moved = await customers.Update(Customer.Keys.Of("alice"))
    .Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), "Bern")
    .Add(Customer.Attrs.Interests, ["keyboards"])
    .IfExists()
    .ReturnNew()
    .SendAsync(ct);
```

**Transactions with typed failure reasons.** Everything is written together or not at all. A marker tells which action failed, and each step type only offers what that action can do, so `.Add` after `.Put` does not compile.

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

**Several types in one query.** Each item arrives as what it is, without a dictionary in between.

```csharp
await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of<Customer, Order>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(profile => profile.Name, order => $"#{order.Number}"));
}
```

**Optimistic locking, and a retry on a conflict.** A `[Version]` property turns every write of the entity into a compare-and-swap, and `RetryOnConflictAsync` runs a read-modify-write again when it loses to another writer.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

**Also:** batches of any size, one list over many partitions, paging tokens for web APIs, `ToRequestJson()` to see what goes over the wire, converters of your own, and tests without DynamoDB through [`DynaSharp.Testing`](https://msallin.github.io/DynaSharp/testing.html), which fakes the wire:

```csharp
using var handler = new StubHandler(_ => StubResponse.Item(order));
using var client = StubClient.Create(handler);
```

## API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry. [API levels](https://msallin.github.io/DynaSharp/api-levels.html) says more.

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
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

[Design principles](https://msallin.github.io/DynaSharp/design-principles.html) says what each one means for your code.

## Benchmarks

Allocations are exact. All clients run on the same canned responses with no network, so these numbers show the client's own cost. [Benchmarks](https://msallin.github.io/DynaSharp/benchmarks.html) explains why the rows differ and how to run them.

Bytes allocated per call, mapped APIs:

| Call | DynaSharp | EfficientDynamoDb | AWS `DynamoDBContext` |
|---|---|---|---|
| GetItem | 2,384 | 5,936 | 40,794 |
| PutItem | 1,528 | 5,112 | 44,420 |
| Query, 100 items | 81,120 | 91,810 | 991,052 |

Low-level APIs, in KB:

| Call | DynaSharp request API | EfficientDynamoDb | AWS SDK |
|---|---|---|---|
| GetItem | 2.55 | 7.16 | 30.51 |
| PutItem | 1.60 | 4.99 | 26.71 |
| Query, 100 items | 98.03 | 202.21 | 379.10 |

Inside DynaSharp, by API level, in bytes:

| Call | Generated entity | `Table<Item>` | Request API |
|---|---|---|---|
| GetItem | 2,256 | 2,576 | 2,608 |
| PutItem | 1,528 | n/a | 1,640 |
| Query, 100 items | 68,320 | 100,320 | 100,456 |

## Building

The exact SDK is pinned in `global.json`.

```shell
dotnet tool restore
dotnet build DynaSharp.slnx
dotnet test --project tests/DynaSharp.UnitTests
dotnet test --project tests/DynaSharp.IntegrationTests   # needs Docker
dotnet stryker                                           # mutation testing
```

The documentation site is built with DocFX from `docs/`: `dotnet build src/DynaSharp.Testing -c Release`, then `dotnet docfx docs/docfx.json --serve`.

## License

MIT
