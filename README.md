# DynaSharp

A high-performance DynamoDB client for .NET. Items are mapped by a source generator, requests go straight to the wire as UTF-8, and the API speaks DynamoDB: every verb is an operation or an expression clause.

- **Lean:** a `PutItem` allocates 1.5 KB against 5.1 KB for EfficientDynamoDb and 44.4 KB for the AWS SDK's `DynamoDBContext`. See [Benchmarks](#benchmarks).
- **Native AOT:** no reflection and no runtime code generation.
- **Single-table first:** key templates, global and sparse indexes, mixed-type reads, fan-out queries, transactions with typed failure reasons.
- **Compile-time safety:** a key of the wrong entity or a clause an operation does not have fails at build time, and a broken expression is a build warning.
- **No AWS SDK:** no runtime package dependencies.
- **Testable:** every request to DynamoDB goes through an `HttpMessageHandler`, and `DynaSharp.Testing` fakes the wire. Credential requests do not, so a test sets `Credentials` too.

## Getting started

### Installation

DynaSharp needs .NET 11. The package brings its source generator and analyzers along.

There is no public feed. Pack a clone of this repository into a folder, make that folder a source, and add the package from it. A clone without a tag packs a prerelease version.

```shell
dotnet pack src/DynaSharp -c Release -o /path/to/packages
dotnet pack src/DynaSharp.Testing -c Release -o /path/to/packages
dotnet nuget add source /path/to/packages --name dynasharp
dotnet add package DynaSharp --prerelease
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
2. the web identity token file named by `AWS_WEB_IDENTITY_TOKEN_FILE`, with `AWS_ROLE_ARN`
3. the profile selected by `AWS_PROFILE` in the shared credentials and config files
4. the container credentials endpoint of ECS and EKS, which is also EKS Pod Identity
5. the instance metadata service of EC2

A source that applies but is broken fails the request and does not fall through to the next one, so a misconfigured pod never signs with the node's role. To use one source only, pass `EnvironmentCredentialsProvider`, `WebIdentityCredentialsProvider`, `ProfileCredentialsProvider`, `ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider` or `StaticCredentialsProvider`. To test the way through STS without AWS, `StubSts` of `DynaSharp.Testing` stands in for it on a port of the machine.

IRSA on EKS sets those two variables in the pod, so `new DynamoDbClient()` needs no more. The client trades the token for credentials at STS with `AssumeRoleWithWebIdentity`, reads the file again for each exchange because the kubelet replaces it, and renews the credentials before they expire. Any other OpenID Connect issuer the account trusts works the same way.

Assume role, SSO, `credential_process` and web identity through the profile keys `web_identity_token_file` and `role_arn` are not supported.

### Defining an entity

Declare a `partial` type and say how it maps. The generator writes the code that reads and writes the item, plus `Keys` and `Attrs`.

```csharp
using DynaSharp.Mapping;

[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
[GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt:ms}")]
public partial record Order
{
    public required string CustomerId { get; init; }
    public required int Number { get; init; }
    public required OrderStatus Status { get; init; }
    public required DateTimeOffset PlacedAt { get; init; }
    public required decimal Total { get; init; }
}
```

`{Number:d6}` pads to six digits, so that keys sort by number. A timestamp in a template is in UTC and keeps milliseconds unless it says otherwise: `{PlacedAt:ms}` and a bare `{PlacedAt}` both store `2026-10-31T12:34:56.789Z`. `{PlacedAt:ticks}` keeps seven digits, and only that format keeps the whole value. Such an order and a customer of the same shop share a table and a partition:

| `pk` | `sk` | `bystatuspk` | `bystatussk` | `_type` | Other attributes |
|---|---|---|---|---|---|
| `CUSTOMER#alice` | `PROFILE` | | | `CUSTOMER` | `Id`, `Name`, `Email` |
| `CUSTOMER#alice` | `ORDER#000001` | `STATUS#Placed` | `2026-10-31T12:34:56.789Z` | `ORDER` | `CustomerId`, `Number`, `Status`, `PlacedAt`, `Total` |

Those are the default names. The inputs of a key are stored as attributes of their own too, unless `[KeyOnly]` says that the key is enough. No attribute holds such a property, so it has no handle in `Attrs`.

What is the same on every type can be said once for the project. This stores `CustomerId` as `customerId`, leaves empty strings and collections out of an item, lets an entity without `[Discriminator]` go without one, and stores every `DateTimeOffset` as epoch seconds, also in a list:

```csharp
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, OmitEmpty = true, NoDiscriminator = true)]
[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(EpochSecondsConverter))]
```

An attribute on a type or a property still wins. A stored attribute takes its name from its property, so changing `Naming` renames stored attributes, and so does renaming a property: a rename refactoring compiles, items written before keep the old name, and a read takes the new one for missing. A property whose name may change takes `[DynamoDbAttribute]`. `pk`, `sk` and `_type` stay as they are.

A key of an index can be computed. Say what the getter reads, so that an update that changes it throws instead of leaving the index behind. A `Put` of the changed entity moves the key.

```csharp
[GlobalIndexKey("ByDay", PartitionKey = "DAY#{ArrivalDay}")]
public partial record Visit
{
    // ...
    public required DateTimeOffset Arrival { get; init; }

    [ComputedFrom(nameof(Arrival))]
    public DateOnly ArrivalDay => DateOnly.FromDateTime(Arrival.UtcDateTime);
}
```

Open a table once and keep it. Opening makes no request. Each entity on a table opens it under its own type, and `client.Table(name, "pk", "sk")` opens it without a model, as a `Table<Item>` that names its keys.

```csharp
var orders = client.Table<Order>("Shop");
var customers = client.Table<Customer>("Shop");
var shop = client.Table("Shop", "pk", "sk");
var byStatus = orders.Index(Order.Indexes.ByStatus);
```

`CreateTable()` creates the table from what the entities declare, billed on demand, with every index projecting all attributes. One entity does not describe a table of several, so `Including` names the others.

```csharp
await orders.CreateTable()
    .Including<Customer>()
    .Including<Product>()
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);
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

Which write to take:

| You want to | Write | An item exists | No item exists |
|---|---|---|---|
| Create, and never overwrite | `Put(entity).IfNotExists()` | `ConditionalCheckFailedException` | Creates it |
| Create or replace | `Put(entity)` | Replaces all of it | Creates it |
| Replace what is there | `Put(entity).IfExists()` | Replaces all of it | `ConditionalCheckFailedException` |
| Patch, or create | `Update(key).Set(...)` | Changes what it names | Creates it from the key and what it names |
| Patch what is there | `Update(key).Set(...).IfExists()` | Changes what it names | `ConditionalCheckFailedException` |

An `Update` that creates an item leaves out the inputs of the key, and `.StoreKeyInputs()` writes them, as a `Put` does. A `[Version]` adds a condition of its own, see [Optimistic locking](#highlights).

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

A file that works on one entity can shorten this with `using Attrs = Shop.Order.Attrs;` at the top, and write `Attrs.Total.Gt(100m)`.

`Limit(n)` is per page and counts what DynamoDB reads before the filter, so it is no cap on items. `Take(n)` is, and it stops reading once it has them:

```csharp
var firstTen = orders.Query(Order.Keys.Pk.Of("alice"))
    .Filter(Order.Attrs.Total.Gt(100m))
    .ItemsAsync(ct)
    .Take(10);
```

An index is read through its own type, which has no writes:

```csharp
var placed = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusSk.Ge(since));
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

`Set` of null removes the attribute, and `SetIfNotNull` adds nothing for null, which suits a change where only some fields came in:

```csharp
await customers.Update(Customer.Keys.Of("alice"))
    .SetIfNotNull(Customer.Attrs.ShipTo, customer.ShipTo)
    .IfExists()
    .SendAsync(ct);
```

Each `.If` adds one condition, and they join with `AND`. In a transaction it belongs to the step before it. A condition can also return what stopped it, which saves the read:

```csharp
try
{
    await products.Update(Product.Keys.Of("KB-01"))
        .Set(Product.Attrs.Price, 79.00m)
        .If(Product.Attrs.Price.Eq(95.00m))
        .If(Product.Attrs.Stock.Gt(0))
        .ReturnOldOnFailure()
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException exception)
{
    Console.WriteLine(exception.Item!.To<Product>().Price);
}
```

`ConditionalCheckFailedException.Key` says which item it was, as it is stored. A caller that holds the key gains nothing from it. It is for code that does not, such as a wrapper around a write it was handed. It is in no message and no log of the client.

```csharp
async Task SaveAsync(Func<CancellationToken, Task> save, CancellationToken token)
{
    try
    {
        await save(token);
    }
    catch (ConditionalCheckFailedException exception) when (exception.Key is { } key)
    {
        Console.WriteLine(Order.Keys.Pk.Parse(key.GetString("pk")!).CustomerId);
        throw;
    }
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

**Retry on a conflict.** `RetryOnConflictAsync`, on the client or on a table, runs a read-modify-write again when it loses to another writer, up to 5 times. The delegate reads again itself, or the next run changes nothing.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

**Paging for web APIs.** A token holds the position, and comes back with the next request.

```csharp
var page = await orders.Query(Order.Keys.Pk.Of("alice")).Limit(25).PageAsync(ContinuationToken.Parse(after), ct);
string? next = page.HasMore ? page.Next.ToString() : null;
```

**See what goes over the wire.** `ToRequestJson` renders what a chain would send, without sending it. It ends the chain as `SendAsync` does, so a command is rendered or sent, not both.

```csharp
string json = products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).ToRequestJson();
```

**A stored form of your own.** `[DynamoDbConverter]` puts an `IDynamoDbConverter<T>` on a property, and [the migration section](#what-changes-stored-data) has examples. A converter that answers `CountsAsMissing` says that a stored value counts as if the attribute were not there, so a name an enum no longer has keeps the initial value and the rest of the item reads.

**Tests without DynamoDB.** [`DynaSharp.Testing`](https://github.com/msallin/DynaSharp/tree/main/src/DynaSharp.Testing) fakes the wire, so a test needs no mock of the client. Each request is a `RecordedRequest`, whose `UpdateText` and `ConditionText` give an expression with its values in place, such as `SET Price = 79.00`.

```csharp
using var handler = new StubHandler(_ => StubResponse.Item(order));
using var client = StubClient.Create(handler);
```

## Adopting an existing table

A table that other code wrote, or that is older than your entities, can be used as it is. Four things stand between it and an entity, and the first is a trap.

**Items without a version.** A `[Version]` property reads an item without the attribute as 0, as it reads one that holds 0, and version 0 means that no item has the key. So the first `Put` of such an item fails with `ConditionalCheckFailedException`, and so does every one after it. Say that the table keeps such items:

```csharp
[Version(Missing = VersionMissing.Allow)]
public long Revision { get; init; }
```

Version 0 then means an item without a version, one that holds 0, or none. `Allow` takes over an item the AWS SDK saved at version 0 as well, because that is what its first `SaveAsync` stores. `Put`, `IfVersion(0)`, a `Delete` of an entity and a transaction step look at the version attribute instead of the key, so the first write replaces the item and stores version 1, a second write at 0 fails, and from there on the lock holds. A write at 0 no longer proves that the key was free: `IfNotExists()` still asks for that, and `IfExists()` at 0 asks for an item that reads as 0.

**Items without a discriminator.** A typed read filters on `_type`, which nobody wrote into these items, so it would find none of them. `[Discriminator(OptOut = true)]` drops the filter.

**Names of your own.** `AttributeName` gives a key the name it has in the table, and `[DynamoDbAttribute]` does the same for any other property. A key with no template is the value of its property.

```csharp
[DynamoDbEntity]
[Discriminator(OptOut = true)]
public partial record Invoice
{
    [PartitionKey(AttributeName = "customer_id")]
    public required string CustomerId { get; init; }

    [SortKey(AttributeName = "invoice_no")]
    public required int Number { get; init; }

    [DynamoDbAttribute("total_amount")]
    public decimal Total { get; init; }

    [Version(Missing = VersionMissing.Allow)]
    public long Revision { get; init; }
}
```

`Select(Invoice.Attrs.Revision)` gives 0 for an item without a version, as a read of the entity does. That is about an item that exists. No item is `Found == false`, and `Value` then throws `InvalidOperationException`, so look first:

```csharp
var invoices = client.Table<Invoice>("Invoices");
var revision = await invoices.Get(Invoice.Keys.Of("alice", 7)).Select(Invoice.Attrs.Revision).SendAsync(ct);
long seenRevision = revision.Found ? revision.Value : 0;
```

**Keys that hold your delimiter.** A `#` in a value throws `KeyFormatException` in `CUSTOMER#{Id}`. The format `raw` takes it, on a string that ends the template. `[KeyOnly]` goes with it, because an input of a template is stored as an attribute too, so without it every item silently gets an extra `Key`. With it `Key` has no handle, so a condition or a filter cannot name it:

```csharp
[DynamoDbEntity]
[Discriminator(OptOut = true)]
[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]
public partial record Appointment
{
    [KeyOnly]
    public required string Key { get; init; }
}
```

`raw` lifts the delimiter check and nothing else. An empty value and a control character still throw `KeyFormatException`, on a write and through `Keys.Of` on a read, so `Keys.TryOf` is the tool for a value from outside. A stored key that holds one, or that does not fit the template, gives `DynamoDbMappingException` on a read, because `Key` is restored from it.

A plain key is the better choice when one entity has keys of several shapes, or when a value must miss and not throw. It takes what is stored as it is, so a `Get` of a value nobody stored gives null. An empty string stays DynamoDB's to reject.

**Timestamps and index keys.** The keys of a table show what its attributes do not: compare a stored key with what `Keys` builds, and see the two rows on keys under [What changes stored data](#what-changes-stored-data).

**Before you switch.** Capture items with the old code first, so that there is something to compare with. Turn time to live off in such a comparison, because the sweeper of DynamoDB Local deletes rows with past dates, which looks like a read that finds nothing.

A program that creates its table at the start can keep doing so over a table that exists:

```csharp
var invoices = client.Table<Invoice>("Invoices");
await invoices.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
```

`IfNotExists()` describes first, and a table it finds ends the command there. So a role needs `dynamodb:DescribeTable` and no `dynamodb:CreateTable` for a table that exists, and no failed call shows in CloudTrail; creating the table takes both. It compares no schemas and changes nothing about a table it finds. `WaitUntilActive()` waits for every global index the entities declare, so over a table that lacks one it times out with a `TableNotActiveException` that names the index. `WaitUntilActive(timeout)` bounds the wait and nothing else, not the calls before it and not `TimeToLive` after it. A `CancellationToken` on the same timeout races it, and when the token wins the caller gets a bare `TaskCanceledException` or `OperationCanceledException`, which names no table and no index, so give the wait less time.

**Adding an index.** The indexes and `.Stream(...)` of `CreateTable()` apply only to a table it creates. To add an index to a table that exists, keep this order:

```csharp
var declared = orders.CreateTable().ToRequest();
var added = declared.GlobalSecondaryIndexes!.Single(index => index.IndexName == Order.Indexes.ByStatus);

await orders.CreateTable().IfNotExists().SendAsync(ct);
var table = await shop.WaitUntilActiveAsync(ct);

if (table.GlobalSecondaryIndexes.All(index => index.IndexName != added.IndexName))
{
    await client.UpdateTableAsync(new UpdateTableRequest
    {
        TableName = "Shop",
        AttributeDefinitions = declared.AttributeDefinitions,
        GlobalSecondaryIndexUpdates = [new GlobalSecondaryIndexUpdate { Create = added }],
    }, ct);
}

await orders.CreateTable()
    .IfNotExists()
    .WaitUntilActive(TimeSpan.FromHours(2))
    .TimeToLive("ExpiresAt")
    .SendAsync(ct);
```

1. `CreateTable().IfNotExists()` without a wait creates a missing table with all its indexes and leaves one that exists alone.
2. `shop` is `client.Table("Shop", "pk", "sk")`, which has no entity, so its wait takes what the table lists, and `UpdateTable` needs an active table. A typed wait here would time out on the index that is not there yet.
3. `UpdateTableAsync`, from `DynaSharp.Requests`, adds one index per call. With a second one to add, `AttributeDefinitions` holds only the keys this call needs, and the next call goes through the wait on `shop` first.
4. Only now does a typed wait end, once DynamoDB lists the index and has backfilled it, so give it a timeout that fits the size of the table. `TimeToLive` implies that wait, which is why it comes last.

`UpdateTable` of a table that is updating throws `ResourceInUseException`, so one instance does this, such as a deployment step. A backfill finds the items that hold the attributes of the index. An older item enters it when a `Put` of the entity writes them.

## Migrating from the AWS SDK

The tables compare with `DynamoDBContext` of `AWSSDK.DynamoDBv2` 4.0 in its default conversion, V1. With `Conversion = DynamoDBEntryConversion.V2` a `bool` is a `BOOL` and a list is an `L`, as here, so those rows fall away.

### What changes stored data

| What you wrote | What to write |
|---|---|
| A string that can be empty. `SaveAsync` removes the attribute for `""`, also in a nested document and with `IgnoreNullValues`, unless `IsEmptyStringValueEnabled` is on. | `OmitEmpty = true` in `[assembly: DynamoDbDefaults]`, which reaches nested documents, or `[OmitEmpty]` on a type or a property. Without it DynaSharp stores `{"S":""}`. A `required` property that is not nullable stays written even so. Compare captured items before you claim the format is unchanged: an item the old code wrote against the `ToRequestJson()` of the new code, attribute by attribute. |
| An empty list of documents, dictionary or `byte[]`. The SDK leaves out empty strings and keeps these. | `OmitEmpty` also covers lists, sets, dictionaries and arrays, so an empty list is not written, and reads back as null or as the initial value. Null and `[]` can mean different things, and `[OmitEmpty(false)]` on the property keeps the empty one. |
| `SaveAsync`, which sends an `UpdateItem`: it sets what the class maps and leaves every other attribute. | A `Put` replaces the whole item, so attributes the entity does not map are gone. Map them, or send `Update(key)` with a `Set` per attribute. |
| A timestamp in a key, where stored rows hold seven digits: `2026-10-09T10:00:00.1234567Z`. | A bare `{At}` keeps milliseconds, `2026-10-09T10:00:00.123Z`, so a key built from that value misses the stored row, and a `Put` writes a second item. `{At:ticks}` keeps seven digits. |
| An upsert that names only part of an index key. | A typed update writes the index keys it can compute. On an index that is not sparse, `.Set(Attrs.At, at)` over `SortKey = "{At}"` renders `SET At = :v0, gsk = :v1` with no `gpk`, so an item it creates is not in the index, and nothing throws. With `Sparse = true` the same call throws. An update that can create an item sets every input of the index, ends in `.IfExists()`, or is a `Put`. |
| A null property. `SaveAsync` removes the attribute, and with `IgnoreNullValues = true` leaves it alone. | `Set(Attrs.X, null)` in an `Update` removes it, and `SetIfNotNull(Attrs.X, value)` adds nothing for null. A `Put` leaves a null out of the item it replaces. |
| A `List<string>` or `string[]`, an `SS` in V1. `List<int>` is an `NS`, and an empty one is left out. | `HashSet<string>`, or `[DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))] public string[] Tags { get; init; } = [];`, with `NumberSetElement<int>` for numbers, or `[DynamoDbConverter(typeof(StringListAsSetConverter))] public List<string> Tags { get; init; } = [];`, below. An empty one is left out here too. An array or a list does not stop a duplicate, which writes `"SS":["x","x"]`, and DynamoDB rejects that. A plain list or array is an `L` here, and reading an `SS` into it throws `DynamoDbMappingException`. |
| A `bool`, an `N` of `1` or `0` in V1. | `BoolAsNumberConverter`, below. Here a `bool` is a `BOOL`, and reading an `N` into it throws. |
| A `DateTime`, text in UTC with three fraction digits: `2026-10-31T12:00:00.123Z`. | `SdkDateTimeConverter`, below. DynaSharp reads that text and writes seven digits, `2026-10-31T12:00:00.1230000Z`, so a condition no longer finds an old value, and old and new sort apart in a key. It also rejects `DateTimeKind.Unspecified`, which the SDK and the converter take for local time. A `TimeOnly` has three digits as well, and takes the same kind of converter. |
| `StoreAsEpoch = true` on a `DateTime`, an `N`. | `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset`. |
| An enum, an `N` in V1 and V2. | `StatusAsNumberConverter`, below. DynaSharp stores the name as text, and only a `[Flags]` enum as a number. |
| `[DynamoDBVersion] public long? Revision`, whose first `SaveAsync` stores 0. | `[Version(Missing = VersionMissing.Allow)] public long Revision`, which is not nullable. `Allow` takes over an item the SDK saved at version 0 as well: a write at 0 stores 1, and from there on the lock holds. Under the default, `[Version]`, 0 means no item, so a write at 0 fails on such an item. |
| `[DynamoDBTable("Shop", LowerCamelCaseProperties = true)]` | `Naming = AttributeNaming.CamelCase` in `[assembly: DynamoDbDefaults]`, which takes a plain key along, as the SDK does. |
| Items without `_type`, items without a version, attributes under names of their own. | [Adopting an existing table](#adopting-an-existing-table). |

The rest stores what the SDK stores: numbers, strings, `Guid`, `DateOnly`, `byte[]`, a `HashSet` of strings or numbers, a `Dictionary<string, string>` and a nested document.

The converters are said once for the project. `[DynamoDbConverter]` does the same for one property.

```csharp
using System.Globalization;
using DynaSharp;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[assembly: DynamoDbDefaultConverter(typeof(bool), typeof(BoolAsNumberConverter))]
[assembly: DynamoDbDefaultConverter(typeof(DateTime), typeof(SdkDateTimeConverter))]
[assembly: DynamoDbDefaultConverter(typeof(OrderStatus), typeof(StatusAsNumberConverter))]

public readonly struct BoolAsNumberConverter : IDynamoDbConverter<bool>
{
    public static void Write(DynamoDbWriter writer, bool value) => writer.WriteNumber(value ? 1 : 0);

    public static bool Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);
        return reader.GetNumber<int>() != 0;
    }
}

public readonly struct SdkDateTimeConverter : IDynamoDbConverter<DateTime>
{
    public static void Write(DynamoDbWriter writer, DateTime value) =>
        writer.WriteString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

    public static DateTime Read(ref DynamoDbReader reader, AttributeType type) => DateTimeConverter.Read(ref reader, type);
}

public readonly struct StatusAsNumberConverter : IDynamoDbConverter<OrderStatus>
{
    public static void Write(DynamoDbWriter writer, OrderStatus value) => writer.WriteNumber((int)value);

    public static OrderStatus Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);
        return (OrderStatus)reader.GetNumber<int>();
    }
}

public readonly struct StringListAsSetConverter : IDynamoDbConverter<List<string>>
{
    public static void Write(DynamoDbWriter writer, List<string> value) => SetArrayConverter<string, StringSetElement>.Write(writer, [.. value]);

    public static List<string> Read(ref DynamoDbReader reader, AttributeType type) => [.. SetArrayConverter<string, StringSetElement>.Read(ref reader, type)];

    public static bool IsOmitted(List<string> value) => value.Count == 0;

    public static bool TryReadMissing(out List<string> value)
    {
        value = [];
        return true;
    }
}
```

`IsOmitted` is there because DynamoDB refuses an empty set, and `TryReadMissing` so that a `Select` reads a missing one as empty. Converters are static, because `IDynamoDbConverter<T>` is static abstract, so one that is generic over its value fails `CA1000` in a project that treats it as an error. Write one struct per type, over a helper.

### The rest of the code

| What you wrote | What to write |
|---|---|
| `[DynamoDBHashKey]`, `[DynamoDBRangeKey]` | `[PartitionKey]`, `[SortKey]` on the property. `[DynamoDBHashKey("id")]` is `[PartitionKey(AttributeName = "id")]`. |
| `[DynamoDBProperty("total")]`, `[DynamoDBIgnore]` | `[DynamoDbAttribute("total")]`, `[DynamoDbIgnore]`. |
| `[DynamoDBProperty(typeof(MyConverter))]` with an `IPropertyConverter` | `[DynamoDbConverter(typeof(MyConverter))]` with an `IDynamoDbConverter<T>`. |
| A stored value that no longer fits the property, such as an enum name that is gone. | A converter that answers `CountsAsMissing`, see [Highlights](#highlights). |
| A property computed from others that feeds an index. | `[ComputedFrom]`, see [Defining an entity](#defining-an-entity). |
| A date bucket in a key: `$"DAY#{arrival:yyyy-MM-dd}"`. | `{Arrival:yyyy-MM-dd}` in a template takes the type of the property, so a `DateOnly day` goes in as `new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero)`. A day that is a value of its own is a `DateOnly` property. |
| A key put together by hand: `$"CUSTOMER#{id}"`. | `Order.Keys.Of(id, number)` and `Order.Keys.Pk.Of(id)` check the values at the call, and throw `KeyFormatException` for a value that holds a delimiter. `TryOf` is the tool for a value from outside: `Order.Keys.TryOf(id, number, out var key)` returns false instead. |
| A key whose value holds the delimiter: `$"appointment#{key}"` over `a#b`. | `[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]` with `[KeyOnly] public required string Key { get; init; }`. Without `[KeyOnly]` every item gets an extra `Key` attribute. `raw` lifts the delimiter check alone, so `Keys.TryOf` stays the tool for a value from outside. See [Adopting an existing table](#adopting-an-existing-table). |
| `QueryOperator.Between` over two key strings. | `Keys.Sk.Between(from, to)` includes `to`, and `Keys.Sk.Range(from, to)` leaves it out, both from values and not from strings. |
| An `Expression` with `#names` and `:values`, or a `ScanCondition`. | `Order.Attrs.Total.Gt(100m)`, which is typed. |
| A `SaveAsync` that may create the item, which stores every property. | `Update` with `.StoreKeyInputs()`, see [PutItem](#putitem). |
| A loop that reads, writes and catches `ConditionalCheckFailedException`. | `RetryOnConflictAsync`, see [Highlights](#highlights). |
| Working out in a `catch` which item failed. | `ConditionalCheckFailedException.Key`, see [UpdateItem](#updateitem). |
| `DescribeTable`, `CreateTable`, a catch of `ResourceInUseException`, a wait. | `carts.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct)`. `.Stream(StreamViewType.NewImage)` for a table it creates and `.TimeToLive(Cart.Attrs.ExpiresAt)` go on the same chain. |
| Credentials from `AWSSDK.Core`, with IRSA on EKS. | `new DynamoDbClient()`, which reads the same [chain](#credentials). `AWS_ROLE_SESSION_NAME` names the session, which is otherwise `dynasharp-` and the Unix time. |
| A region found in EC2 instance metadata. | `AWS_REGION`, or `Region` in the options. DynaSharp does not ask the metadata service, see [Region](#region). |
| A mock of `IDynamoDBContext` or `IAmazonDynamoDB`. | `StubClient.Create(stub)` over a `StubHandler`, which fakes the wire, and `StubSts` for the way through STS. |

## API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry.

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
| **Requests** (`using DynaSharp.Requests`) | `client.GetItemAsync(new GetItemRequest { ... })` | Every DynamoDB operation as a request and a response that mirror the service. Table administration beyond `CreateTable()` lives here. |
| **Raw** | `client.SendRawAsync("ListTables", json)` | JSON in, JSON out, through the same signing and retries. |

You can tell which level a line uses by what it names: `Keys` and `Attrs` mean a typed table, strings mean `Table<Item>`, and `new ...Request` means the request API. `item.To<T>()` and `Item.From(entity)` cross between levels.

The first two levels share one command engine: ref struct chains that write JSON as you call them. A chain is built and sent in one expression, so it works in pooled buffers that go back at the end, and [Benchmarks](#benchmarks) shows what a call allocates in all. An analyzer explains the compiler error if you hold on to one across an `await`.

## Design principles

- **Stay true to DynamoDB.** Every verb is an operation or a clause. There is no `Save`, no identity map, no lazy load and no implicit read before a write. `Limit` is per page, `Update` is an upsert, and batches are not atomic, as in DynamoDB.
- **Nothing hidden.** What the client adds, such as a version condition or a discriminator filter, shows up in `ToRequestJson()`.
- **Mistakes belong to the compiler.** Types rule out what they can, analyzers catch mistakes in constant expressions, and the rest fails before a request is sent. A broken expression is `DYNA212`, a warning, so it stops only a build that treats warnings as errors.
- **Pay only for what you use.** Requests are written straight to pooled UTF-8 buffers, and known attribute names cost no string. A cost that shows up in a benchmark is a bug until it is explained.
- **No reflection.** The source generator produces the mapping, so Native AOT works and nothing is discovered at run time.
- **Keys are data on disk.** Encodings are specified and tested against an independent oracle, because changing one is a migration.
- **Retries are honest.** A retry can apply a write twice when a response is lost. The docs say where that matters, and `MaxAttempts(1)` is one call away.
- **Tests answer to someone else.** Wire formats are checked against botocore and real AWS responses, behaviour against DynamoDB Local and Dynoxide. The test suite has not run against AWS itself.

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

Why the rows differ:

- The item has a set of tags. The first table maps it to a `HashSet<string>` in all three clients, so that they compare. The third maps it to a `string[]`, which is lighter, so a read costs less there, and a `PutItem`, which only walks the set, costs the same.
- A read costs more than a write, because a read builds the item it returns and a write has only a request to fill. A query of 100 items is mostly 100 times what one item costs.
- A generated entity reads straight into its properties. `Table<Item>` and the request API hold each attribute as an `AttributeValue`, which is the step between the first column of the third table and the other two. The `Item` of a `PutItem` is the object of the caller, so that cell is n/a.
- The second table and the last column of the third are the same request API, in KB and in bytes. They come from two benchmark classes, and the `Query` of the third runs in an `async` method of the benchmark, which adds its `Task`.

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
