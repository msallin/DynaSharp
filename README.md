# DynaSharp

A high-performance DynamoDB client for .NET. Items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB: every verb is an operation or an expression clause.

- **Lean:** a `PutItem` allocates 1.5 KB against 5.1 KB for EfficientDynamoDb and 44.4 KB for the AWS SDK's `DynamoDBContext`. See [Benchmarks](#benchmarks).
- **Native AOT:** no reflection and no runtime code generation.
- **Single-table first:** key templates, global and sparse indexes, mixed-type reads, fan-out queries, transactions with typed failure reasons.
- **Compile-time safety:** a key of the wrong entity, a clause an operation does not have, or a broken expression fails at build time.
- **No AWS SDK:** no runtime package dependencies.
- **Testable:** every request goes through an `HttpMessageHandler`, and `DynaSharp.Testing` fakes the wire.

> **Status:** early development. Nothing is published to NuGet yet, and DynaSharp has only been tested against DynamoDB Local and Dynoxide, not yet against AWS itself.

## Getting started

### Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along.

```shell
dotnet add package DynaSharp
```

### Creating the client

All requests go through `DynamoDbClient`. It is thread-safe, so create one and keep it. With no options, region, endpoint and credentials come from the environment, the way the AWS SDKs read them.

```csharp
using DynaSharp;

using var client = new DynamoDbClient();
```

Every setting can be given explicitly:

```csharp
using DynaSharp;
using DynaSharp.Credentials;

using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    Region = AwsRegion.FromName("eu-central-1"),
    Credentials = new ProfileCredentialsProvider("shop"),
    MaxAttempts = 4,
    AttemptTimeout = TimeSpan.FromSeconds(30),
});
```

`HttpHandler`, `TimeProvider` and `LoggerFactory` are there too, for tests and logging.

### Region

Without `Region`, the client reads `AWS_REGION`, then `AWS_DEFAULT_REGION`, then the `region` setting of the profile in the shared AWS files. The region is required even for an emulator, because it is part of the request signature.

For a local emulator, set `ServiceUrl`, or `AWS_ENDPOINT_URL_DYNAMODB`:

```csharp
using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    ServiceUrl = new Uri("http://localhost:8000"),
    Region = AwsRegion.FromName("us-east-1"),
    Credentials = new StaticCredentialsProvider("local", "local"),
});
```

### Credentials

Without `Credentials`, the client uses the default chain of the AWS SDKs and takes the first source that has credentials:

1. `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_SESSION_TOKEN`
2. the profile selected by `AWS_PROFILE` in the shared credentials and config files
3. the container credentials endpoint of ECS and EKS
4. the instance metadata service of EC2

A source that applies but is broken fails the request and does not fall through to the next one, so a misconfigured pod never signs with the node's role. To use one source only, pass `EnvironmentCredentialsProvider`, `ProfileCredentialsProvider`, `ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider` or `StaticCredentialsProvider`.

Assume role, SSO, web identity and `credential_process` are not supported yet.

### Defining an entity

Declare a `partial` type and say how it maps. The generator writes the code that reads and writes the item, plus `Keys` and `Attrs`.

```csharp
using DynaSharp.Mapping;

[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
[GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
public partial record Order
{
    public required string CustomerId { get; init; }
    public required int Number { get; init; }
    public required OrderStatus Status { get; init; }
    public required DateTimeOffset PlacedAt { get; init; }
    public required decimal Total { get; init; }
}
```

Open a table once and keep it. Opening makes no request.

```csharp
var orders = client.Table<Order>("Shop");
var byStatus = orders.Index(Order.Indexes.ByStatus);
```

## Examples

The rest of this page uses a small shop on one table. The full version is a runnable tour: start with [`samples`](https://github.com/msallin/DynaSharp/tree/main/samples).

### GetItem

`ItemAsync` returns the item, or null when there is none. `SendAsync` is the same request, with `Found` and the consumed capacity.

```csharp
Order? order = await orders.Get(Order.Keys.Of("alice", 1)).ItemAsync(ct);

var result = await orders.Get(Order.Keys.Of("alice", 1)).Consistent().SendAsync(ct);
```

### PutItem

A put replaces the whole item. Conditions are part of the same atomic step.

```csharp
await orders.Put(order).IfNotExists().SendAsync(ct);
```

### Query

`ItemsAsync` follows pages for you, including pages a filter left empty. `Keys` builds the key, and `Attrs` makes the filter typed, so comparing a total with a string does not compile.

```csharp
var bigOrders = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Total.Gt(100m))
    .Descending()
    .ItemsAsync(ct);

await foreach (var order in bigOrders)
{
    Console.WriteLine(order.Number);
}
```

An index is read through its own type, which has no writes:

```csharp
var placed = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed), SortKey.Ge(Order.Keys.ByStatusSk.Format(since)));
```

### UpdateItem

An update changes what it names and leaves the rest. `At` walks into a nested document, and `ReturnNew` gives back the item in one round trip.

```csharp
var moved = await customers.Update(Customer.Keys.Of("alice"))
    .Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), "Bern")
    .Add(Customer.Attrs.Interests, ["keyboards"])
    .IfExists()
    .ReturnNew()
    .SendAsync(ct);
```

A condition can also return what stopped it, which saves the read:

```csharp
try
{
    await products.Update(Product.Keys.Of("KB-01"))
        .Set(Product.Attrs.Price, 79.00m)
        .If(Product.Attrs.Price.Eq(95.00m))
        .ReturnOldOnFailure()
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException exception)
{
    Console.WriteLine(exception.Item!.To<Product>().Price);
}
```

### TransactWriteItems

Everything is written together or not at all. A marker tells which action failed, and each step type only offers what that action can do, so `.Add` after `.Put` does not compile.

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

### Highlights

**Batches of any size.** A batch cuts its work into requests of 25 puts or 100 gets and sends again what DynamoDB left unprocessed, with bounded retries.

```csharp
var written = await products.BatchWrite().PutAll(catalog).SendAsync(ct);
var found = await products.BatchGet().Keys(skus.Select(Product.Keys.Of)).SendAsync(ct);
```

**Several types in one query.** Each item arrives as what it is, without a dictionary in between.

```csharp
await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of<Customer, Order>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(profile => profile.Name, order => $"#{order.Number}"));
}
```

**One list over many partitions.** A fan-out query merges partitions by sort key in DynamoDB's own order. A partition is read only when the merge needs it, so breaking out of the loop reads no more.

```csharp
PartitionKey<Order>[] open = [Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)];

await foreach (var order in byStatus.Query(open).Descending().ItemsAsync(ct))
{
    Console.WriteLine(order.Number);
}
```

**Optimistic locking.** A `[Version]` property turns every write of the entity into a compare-and-swap. A write that says nothing about the version throws before anything is sent.

```csharp
var saved = await carts.Update(Cart.Keys.Of("bob"))
    .Set(Cart.Attrs.Lines, lines)
    .IfVersion(seen.Revision)
    .SendAsync(ct);
```

**Paging for web APIs.** A token holds the position, and comes back with the next request.

```csharp
var page = await orders.Query(Order.Keys.Pk.Of("alice")).Limit(25).PageAsync(ContinuationToken.Parse(after), ct);
string? next = page.HasMore ? page.Next.ToString() : null;
```

**See what goes over the wire.** `ToRequestJson` renders what a chain would send, without sending it.

```csharp
string json = products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).ToRequestJson();
```

**Tests without DynamoDB.** `DynaSharp.Testing` fakes the wire, so a test needs no mock of the client.

```csharp
using var handler = new StubHandler(_ => StubResponse.Item(order));
using var client = new DynamoDbClient(new DynamoDbClientOptions
{
    HttpHandler = handler,
    Region = AwsRegion.FromName("us-east-1"),
    Credentials = new StaticCredentialsProvider("test", "test"),
});
```

## API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry.

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
| **Requests** (`using DynaSharp.Requests`) | `client.GetItemAsync(new GetItemRequest { ... })` | Every DynamoDB operation as a request and a response that mirror the service. Table administration lives here. |
| **Raw** | `client.SendRawAsync("ListTables", json)` | JSON in, JSON out, through the same signing and retries. |

You can tell which level a line uses by what it names: `Keys` and `Attrs` mean a typed table, strings mean `Table<Item>`, and `new ...Request` means the request API. `item.To<T>()` and `Item.From(entity)` cross between levels.

The first two levels share one command engine: ref struct chains that write JSON as you call them. A chain is built and sent in one expression, which is what keeps it free of allocations, and an analyzer explains the compiler error if you hold on to one across an `await`.

## Design principles

- **Stay true to DynamoDB.** Every verb is an operation or a clause. There is no `Save`, no identity map, no lazy load and no implicit read before a write. `Limit` is per page, `Update` is an upsert, and batches are not atomic, as in DynamoDB.
- **Nothing hidden.** What the client adds, such as a version condition or a discriminator filter, shows up in `ToRequestJson()`.
- **Mistakes belong to the compiler.** Types rule out what they can, analyzers catch mistakes in constant expressions, and the rest fails before a request is sent.
- **Pay only for what you use.** Requests are written straight to pooled UTF-8 buffers, and known attribute names cost no string. A cost that shows up in a benchmark is a bug until it is explained.
- **No reflection.** The source generator produces the mapping, so Native AOT works and nothing is discovered at run time.
- **Keys are data on disk.** Encodings are specified and tested against an independent oracle, because changing one is a migration.
- **Retries are honest.** A retry can apply a write twice when a response is lost. The docs say where that matters, and `MaxAttempts(1)` is one call away.
- **Tests answer to someone else.** Wire formats are checked against botocore and real AWS responses, behaviour against DynamoDB Local and Dynoxide.

## Benchmarks

Allocations are exact. All clients run on the same canned responses with no network, so these numbers show the client's own cost.

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

To run them, from the repository root:

```shell
dotnet run -c Release --project benchmarks/DynaSharp.Benchmarks
```

## Building

The exact SDK is pinned in `global.json`.

```shell
dotnet tool restore
dotnet build DynaSharp.slnx
dotnet test --project tests/DynaSharp.UnitTests
dotnet test --project tests/DynaSharp.IntegrationTests   # needs Docker
dotnet stryker                                           # mutation testing
```

## License

MIT
