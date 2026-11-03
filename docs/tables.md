# Tables

A table is opened once and kept. `CreateTable()` creates it from what its entities declare, and over a table that exists it can stay out of the way, which is what a program wants that creates its table at the start.

## Opening a table

Open a table once and keep it. Opening makes no request. Each entity on a table opens it under its own type, and `client.Table(name, "pk", "sk")` opens it without a model, as a `Table<Item>` that names its keys. These are the tables of [the shop](getting-started.md#the-shop).

```csharp
var orders = client.Table<Order>("Shop");
var customers = client.Table<Customer>("Shop");
var shop = client.Table("Shop", "pk", "sk");
var carts = shop.Of<Cart>();
var byStatus = orders.Index(Order.Indexes.ByStatus);
```

`Of<T>()` gives the same table under another entity, so code that holds one table reaches every type in it: `shop.Of<Cart>()` is `client.Table<Cart>("Shop")`, and works on any table, typed or not. It is cached per type, so calling it inline costs a lookup and no allocation. The [design patterns](patterns.md) hold `shop` and write `shop.Of<Account>()` where they need a type. `Index(...)` reads an index of the table through a type of its own, which has no writes. `DYNA221` and `DYNA224`, which check a query against the keys of what it reads, know the index only from an `Index(...)` in the chain of the query, so they pass over a query of an index kept in a field or a variable, such as `byStatus`: see [Commands](diagnostics.md#commands) on the diagnostics page.

## Creating a table

`CreateTable()` creates the table from what the entities declare, billed on demand, with every index projecting all attributes. One entity does not describe a table of several, so `Including` names the others.

```csharp
await orders.CreateTable()
    .Including<Customer>()
    .Including<Product>()
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);
```

`.Stream(StreamViewType.NewImage)` for a table it creates and `.TimeToLive(Cart.Attrs.ExpiresAt)` go on the same chain. [Streams](streams.md) says how to read the stream.

The result is a `CreateTableResult`: `Table` is the description after the call, `Created` says whether this call created the table, and `TimeToLiveEnabled` whether this call turned time to live on.

## A table that exists

A program that creates its table at the start can keep doing so over a table that exists:

```csharp
var invoices = client.Table<Invoice>("Invoices");
await invoices.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
```

`IfNotExists()` describes the table first, and a table it finds ends the create there.

- **Permissions.** Over a table that exists, a role needs `dynamodb:DescribeTable` and no `dynamodb:CreateTable`, and no failed call shows in CloudTrail. Creating the table takes both.
- **Changes.** It compares no schemas, and it changes nothing about a table it finds unless `TimeToLive` is on the chain, which goes on and takes `dynamodb:DescribeTimeToLive` and `dynamodb:UpdateTimeToLive`.
- **The wait.** `WaitUntilActive()` waits for every global index the entities declare, so over a table that lacks one it fails within seconds with a `TableNotActiveException` that names the index. [Adding an index](#adding-an-index) says how to add it.
- **Timeouts.** `WaitUntilActive(timeout)` bounds the wait and nothing else: not the calls before it, and not `TimeToLive` after it. A `CancellationToken` on the same timeout races it, and when the token wins the caller gets a bare `TaskCanceledException` or `OperationCanceledException`, which names no table and no index. So give the wait less time than the token.

## Adding an index

The indexes and `.Stream(...)` of `CreateTable()` apply only to a table it creates. To add an index to a table that exists, keep this order:

```csharp
var declared = orders.CreateTable().ToRequest();
var added = declared.GlobalSecondaryIndexes!.Single(index => index.IndexName == Order.Indexes.ByStatus);
var keys = declared.AttributeDefinitions.Where(attribute => added.KeySchema.Any(key => key.AttributeName == attribute.AttributeName)).ToList();

await orders.CreateTable().IfNotExists().SendAsync(ct);
var table = await shop.WaitUntilActiveAsync(ct);

if (table.GlobalSecondaryIndexes.All(index => index.IndexName != added.IndexName))
{
    await client.UpdateTableAsync(new UpdateTableRequest
    {
        TableName = "Shop",
        AttributeDefinitions = keys,
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
2. `shop` is `client.Table("Shop", "pk", "sk")`, which has no entity, so its wait takes what the table lists, and `UpdateTable` needs an active table. A typed wait here would not return: `orders.WaitUntilActiveAsync()` keeps polling until its timeout for the index that is not there yet, and `CreateTable().WaitUntilActive()` fails within seconds naming it.
3. `UpdateTableAsync`, from `DynaSharp.Requests`, adds one index per call, and `AttributeDefinitions` holds the keys of that index. A second index is a call of its own after the wait on `shop`. So is a stream on a table that exists, with `StreamSpecification`, before the wait of step 4, because it puts the table back into updating.
4. Only now does a typed wait end, once DynamoDB lists the index and has backfilled it, so give it a timeout that fits the size of the table. Every `TimeToLive` implies that wait, with a default of five minutes, which is why it comes last.

`UpdateTable` of a table that is updating throws `ResourceInUseException`. Several instances can each run this: one that loses the race catches it, and the `ValidationException` of an index that got there first, and goes on to the typed wait, which fails if the index is not there. A backfill finds the items that hold the attributes of the index. An older item enters it when a `Put` of the entity writes them.

## Everything else

Table administration beyond `CreateTable()`, such as `UpdateTable` above, lives in the request API, `DynaSharp.Requests`, which mirrors the service. [API levels](api-levels.md) says how the levels share a client.
