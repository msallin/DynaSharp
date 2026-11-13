# Tables

Open a table once and keep it. `CreateTable()` creates it from what its entities declare, and leaves a table that exists alone, so a program can create its table at every start.

## Opening a table

Opening makes no request. An entity declares the table it lives in, and `client.Table<T>()` opens it under the type of the entity; `client.Table(name, "pk", "sk")` opens a table without a model, as a `Table<Item>` that names its keys. These are the tables of [the shop](getting-started.md#the-shop), whose entities declare `Shop`:

```csharp
var orders = client.Table<Order>();
var customers = client.Table<Customer>();
var shop = client.Table("Shop", "pk", "sk");
var carts = shop.Of<Cart>();
var byStatus = orders.Index(Order.Indexes.ByStatus);
```

`Of<T>()` gives the same table under another entity, so code that holds one table reaches every type in it: `shop.Of<Cart>()` is `client.Table<Cart>("Shop")`, on any table, typed or not, and keeps its name whatever `T` declares. It is cheap enough to call inline, as the [design patterns](patterns.md) do. `Index(...)` reads an index of the table through a type of its own, which has no writes.

> [!NOTE]
> `DYNA221` and `DYNA224` check a query against the keys of what it reads when `Index(...)` is in the chain of the query, or in a variable that holds that index and nothing else, such as `byStatus`: a local not written again, a `readonly` field or a get-only property. They pass over a variable that can hold another index. See [Commands](diagnostics.md#commands) on the diagnostics page.

### Declaring the table

`[DynamoDbEntity(Table = "...")]` names the table of an entity. The entities of one table name it alike, so a single-table model keeps the name in one constant:

```csharp
using DynaSharp;
using DynaSharp.Mapping;

public static class Ledger
{
    public const string Table = "Ledger";
}

[DynamoDbEntity(Table = Ledger.Table)]
[Discriminator("LEDGER_ACCOUNT")]
[PartitionKey("ACCOUNT#{Id}")]
[SortKey("PROFILE")]
public partial record LedgerAccount
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
}

[DynamoDbEntity(Table = Ledger.Table)]
[Discriminator("LEDGER_ENTRY")]
[PartitionKey("ACCOUNT#{AccountId}")]
[SortKey("ENTRY#{At}")]
public partial record LedgerEntry
{
    public required string AccountId { get; init; }
    public required DateTimeOffset At { get; init; }
    public required decimal Amount { get; init; }
}

public static class LedgerTables
{
    public static (Table<LedgerAccount> Accounts, Table<LedgerEntry> Entries) Open(DynamoDbClient client) =>
        (client.Table<LedgerAccount>(), client.Table<LedgerEntry>());
}
```

A service whose entities all live in one table says it once, for every entity of its assembly, and an entity with a table of its own still names it:

```text
[assembly: DynamoDbDefaults(Table = "Ledger")]

[DynamoDbEntity(Table = "Sessions")]
public partial record Session { ... }
```

The example blocks of this guide are compiled together, and [Project defaults](entities.md#project-defaults) holds their assembly attribute, so this one is plain text.

- **What the generator checks.** A name DynamoDB would refuse, empty, outside 3 to 255 characters, or with a character other than `a-z`, `A-Z`, `0-9`, `_`, `-` and `.`, is the error [`DYNA039`](diagnostics.md#entities). Two entities of one table whose keys have other names are the warning [`DYNA040`](diagnostics.md#entities), since a table has one key schema.
- **The name in code.** Each entity that declares a table gets the constant `LedgerAccount.TableName`, here `"Ledger"`, the name as declared, see [TableName](generated-code.md#tablename).
- **No declaration.** `client.Table<T>()` of an entity that declares no table, on itself or through its assembly, is the error [`DYNA238`](diagnostics.md#commands), and an entity from elsewhere throws `InvalidOperationException` there, naming both ways to fix it.

### A table per environment

Accounts that hold several environments tell their tables apart by name. `TableNaming` of the options turns a declared name into the one the client opens, on every `Table<T>()`:

```csharp
var environment = Environment.GetEnvironmentVariable("STAGE") ?? "dev";
using var staged = new DynamoDbClient(new DynamoDbClientOptions
{
    TableNaming = name => $"{environment}-{name}",
});

var accounts = staged.Table<LedgerAccount>(); // dev-Ledger
```

It applies only to declared names. A function that returns null or an empty name keeps the declared name, so it can rename only some tables; any other result is opened as it is, an ARN as well.

Code that names a table and knows no entity, such as a health check, asks the client for the name it opens with `ResolveTableName`. `DescribeTableAsync(name)` of the client, like every member that takes a name, takes the name as deployed and applies no `TableNaming`:

```csharp
var ledger = await client.DescribeTableAsync(client.ResolveTableName("Ledger"), ct); // dev-Ledger, under the TableNaming above
```

### An explicit name

`client.Table<T>(name)` opens the table it is given, exactly: `TableNaming` does not touch it, and it can be the ARN of a table in another account. It is the override for a table the declaration does not know, and what a test uses for a table of its own per run:

```csharp
var scratch = client.Table<LedgerAccount>($"Ledger-{Guid.NewGuid():N}");
```

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

Code that holds the table without a model, as `shop`, starts there and names every entity alike. The keys start as the names the table was opened with, and the entities give their types and indexes, or throw at `Including` when they disagree:

```csharp
await shop.CreateTable()
    .Including<Order>()
    .Including<Customer>()
    .Including<Product>()
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);
```

Without an `Including`, such a table has string keys and no index. Either start gives the same table for the same entities.

`.Stream(StreamViewType.NewImage)` for a table it creates and `.TimeToLive("ExpiresAt")` for an attribute that no entity declares go on the same chain. [Streams](streams.md) says how to read the stream. An entity with a [`[TimeToLive]`](entities.md#time-to-live) property needs no `.TimeToLive(...)`: `CreateTable()` turns time to live on for it, and so does an `Including` of it, while `.TimeToLive(...)` replaces what the entities declare. Two entities that declare different attributes throw `ArgumentException` at `Including`, since a table has time to live on one.

The result is a `CreateTableResult`: `Table` is the description after the call, `Created` says whether this call created the table, `TimeToLiveEnabled` whether this call turned time to live on, and `Drift` how a table that `IfNotExists()` found differs from what the command declares, see [A table that exists](#a-table-that-exists).

A chain that leaves out an entity that declares the same table, in this project or in one it references, is the warning [`DYNA240`](diagnostics.md#commands), whose code fix adds the missing `Including<T>()` calls. It checks the chain as written, also one that a method returns; a command kept in a variable or passed to a method of your own is not checked.

### Every entity of an assembly

An entity left out of the chain is an index left out of the table, and with `IfNotExists()` one that [the drift report](#a-table-that-exists) cannot show, since it compares the table with the entities the chain names. So the generator writes `IncludingAll()` for each assembly whose entities all have the keys of one table: an `Including<T>()` of every `[DynamoDbEntity]` of the assembly, in the order of their full names. It lives in an `internal` class `DynamoDbEntities` in the namespace `DynaSharp`, so `using DynaSharp;` finds it. The [sample](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/Program.cs) creates its table with it:

```text
await orders.CreateTable()
    .IncludingAll()
    .IfNotExists()
    .WaitUntilActive()
    .SendAsync(ct);
```

- **Which assemblies get it.** Only one whose entities share one pair of key names, which `Including<T>` demands. Entities of more than one table, or an entity nested in a `private` type, give none, and the info diagnostic [`DYNA037`](diagnostics.md#entities) says why; name the entities with `Including<T>` there. Two tables whose keys have the same names cannot be told apart: keep their entities in different assemblies, or list them.
- **One per assembly, internal.** No other assembly sees it, so code in another assembly names the entities with `Including<T>()`. A test project that sees the internals through `InternalsVisibleTo` calls it for the entities of the other assembly, unless it has entities of its own, which make the call ambiguous (`CS0121`).
- **The name.** A type of the assembly named `DynaSharp.DynamoDbEntities` is the error [`DYNA038`](diagnostics.md#entities).

The example blocks of this page are compiled beside entities of other tables, so this one is plain text.

## A table that exists

A program that creates its table at the start can keep doing so over a table that exists:

```csharp
var invoices = client.Table<Invoice>("Invoices");
await invoices.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
```

`IfNotExists()` describes the table first, and a table it finds ends the create there. It then checks that table against what the command declares, from the description it already has, so the check costs no call.

- **Permissions.** Over a table that exists, a role needs `dynamodb:DescribeTable` and no `dynamodb:CreateTable`, and no failed call shows in CloudTrail. Creating the table takes both.
- **A key that differs throws.** Another partition or sort key attribute, a sort key on one side only, or a key attribute of another type where both sides know it, is an `InvalidOperationException` that names the table, the key the command expects and the key it found, before any wait or time to live. Every read and write of the entities would fail on that table with a `ValidationException`, and DynamoDB cannot change the key of a table, so a deploy should stop there: give the entities a table of their own, or create a new one and move the items. A table opened without a model, `client.Table(name, "pk", "sk")`, knows its key names and not their types, so only the names count.
- **Drift is reported.** What lets part of the code work does not throw: an index the entities declare that the table lacks, or has with other keys or as the other kind, or with a projection that leaves out the discriminator (`_type`, or the name the entity gives it), time to live on another attribute than the one asked for, and, with `.Stream(...)` on the chain, a stream that is off or of another view. Each is a `TableDrift` in `result.Drift`, with its `Kind`, the `IndexName` where it is about an index, and a `Description` that names what the command expects and what the table has. The client also logs them once as a warning (event 14, see [Observability](observability.md#logs)), or throws them with `FailOnDrift()`, below.
- **Changes.** The command changes nothing about a table it finds unless time to live is asked for, by `TimeToLive` on the chain or a `[TimeToLive]` of an entity. Time to live that is off is turned on, which takes `dynamodb:DescribeTimeToLive` and `dynamodb:UpdateTimeToLive` and is no drift. Time to live on another attribute is left on, since moving it takes two changes an hour apart, and is drift.
- **What is not drift.** An index, an attribute, a stream or time to live that the table has and the command does not ask for: other code may use them.
- **The wait.** `WaitUntilActive()` waits for every global index the entities declare, so over a table that lacks one it fails within seconds with a `TableDriftException` that lists the index, with or without `FailOnDrift()`: no wait adds it, so it is no timeout. [Adding an index](#adding-an-index) says how to add it.
- **Timeouts.** `WaitUntilActive(timeout)` bounds the wait and nothing else: not the calls before it, and not `TimeToLive` after it. `Timeout(timeout)` bounds the whole command, see below.

A program acts on drift where it can, and says so where it cannot:

```csharp
var result = await orders.CreateTable().Including<Customer>().IfNotExists().SendAsync(ct);

foreach (var drift in result.Drift)
{
    Console.Error.WriteLine($"{drift.Kind}: {drift.Description}");
}

var missing = result.Drift.Where(drift => drift.Kind == TableDriftKind.IndexMissing).Select(drift => drift.IndexName).ToList();
```

A start that must stop on a table that does not fit, such as an index the code queries that the table lacks, or time to live on another attribute so that items never expire, adds `FailOnDrift()`. The command then throws a `TableDriftException`, an `InvalidOperationException` like a key that differs, whose `Drift` lists each difference and whose message says them all, and logs no warning:

```csharp
await orders.CreateTable().Including<Customer>().IfNotExists().FailOnDrift().WaitUntilActive().SendAsync(ct);
```

`FailOnDrift()` implies `IfNotExists()`. Drift of an index or a stream throws before the wait and before time to live, so nothing changed; time to live on another attribute is found once the table is active, and is left as it is.

A missing global index goes in with `UpdateTable`, see [Adding an index](#adding-an-index). A missing local index, which only a new table can have, and an index with other keys or without the discriminator in its projection need a decision: a new table, or a global index deleted and created again while the code that queries it waits. A stream goes on with `StreamSpecification` of `UpdateTable`, and time to live moves with `UpdateTimeToLive`, off and then on for the new attribute. A test that pins the schema asserts that `Drift` is empty.

A program that must not hang at its start gives the whole command a bound:

```csharp
await orders.CreateTable()
    .IfNotExists()
    .WaitUntilActive()
    .Timeout(TimeSpan.FromMinutes(2))
    .SendAsync(ct);
```

`Timeout` covers the look, the create, the wait and time to live, and throws a `TimeoutException` that names the table and the step, where a token on a timer gives a bare `OperationCanceledException`. A wait that runs out first throws its `TableNotActiveException`, a `TimeoutException` whose message starts with the index that held it up, or with the table when no index did. A table that DynamoDB accepted stays after a timeout, and the next start goes on from there.

### Describing a table within a time

A health check asks whether the table is there and gives up after a few seconds. `DescribeTableAsync(timeout, ct)` bounds the call, retries included, and throws a `TimeoutException` that names the table:

```csharp
try
{
    var description = await orders.DescribeTableAsync(TimeSpan.FromSeconds(5), ct);
    Console.WriteLine(description.TableStatus);
}
catch (TimeoutException timeout)
{
    Console.WriteLine(timeout.Message); // The DescribeTable of Shop did not answer within its timeout of 00:00:05.
}
```

`client.DescribeTableAsync(name, timeout, ct)` does the same by the deployed name.

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
2. `shop` has no entity, so its wait takes what the table lists, and `UpdateTable` needs an active table. A typed wait here would wait for the index that is not there yet, until its timeout.
3. `UpdateTableAsync`, from `DynaSharp.Requests`, adds one index per call, and `AttributeDefinitions` holds the keys of that index. A second index is a call of its own after the wait on `shop`. So is a stream on a table that exists, with `StreamSpecification`, before the wait of step 4, because it puts the table back into updating.
4. Only now does a typed wait end, once DynamoDB lists the index, has allocated it and has backfilled it. The allocation takes minutes on any table, often more than the default of five, and the backfill as long as the table is large, so give the wait half an hour for a small table and more for a large one. Every `TimeToLive` implies that wait, with a default of five minutes, which is why it comes last.

`UpdateTable` of a table that is updating throws `ResourceInUseException`. Several instances can each run this: one that loses the race catches it, and the `ValidationException` of an index that got there first, and goes on to the typed wait. A backfill finds the items that hold the attributes of the index; an older item enters it when a write gives it them, see [Changing a model](migrating.md#changing-a-model).

## One table per entity

Single-table design is a choice, not a requirement. An entity can have a table of its own, declared, opened, created and read the same way. Its keys need no prefix, since nothing else lives in the table, and `[Discriminator(OptOut = true)]` leaves out `_type`, which a table of one type does not need: a `Query` or a `Scan` of the entity then reads every item without filtering on it. Keep the discriminator if the table may take a second type later.

```csharp
using DynaSharp;
using DynaSharp.Mapping;

[DynamoDbEntity(Table = "Wallets")]
[Discriminator(OptOut = true)]
[PartitionKey("{Id}")]
public partial record Wallet
{
    public required string Id { get; init; }
    public required decimal Balance { get; init; }
}

[DynamoDbEntity(Table = "Deposits")]
[Discriminator(OptOut = true)]
[PartitionKey("{WalletId}")]
[SortKey("{At}")]
public partial record Deposit
{
    public required string WalletId { get; init; }
    public required DateTimeOffset At { get; init; }
    public required decimal Amount { get; init; }
}

public static class Deposits
{
    public static async Task BookAsync(DynamoDbClient client, Deposit deposit, CancellationToken ct)
    {
        var wallets = client.Table<Wallet>();
        var deposits = client.Table<Deposit>();

        await wallets.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
        await deposits.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);

        await client.TransactWrite()
            .Put(deposits, deposit).IfNotExists()
            .Update(wallets, Wallet.Keys.Of(deposit.WalletId)).Add(Wallet.Attrs.Balance, deposit.Amount)
            .SendAsync(ct);
    }
}
```

What spans tables and what does not:

- A transaction belongs to the client, so `client.TransactWrite()` and `client.TransactGet()` take actions on any tables of the account and region, as DynamoDB does.
- A batch starts from a table and spans tables: `BatchWrite()` takes puts and deletes of any table, and `BatchGet()` reads the keys of its table into `Items` and those of other tables through slots, see [Batches](transactions.md#batches).
- Mixed-type reads, fan-out queries and `Of<T>()` are about one table holding several types.
- `FakeDynamoDb` holds any number of tables, and the [locks](coordination.md) and [streams](streams.md) work on any table.

## Other table operations

Table administration beyond `CreateTable()`, such as `UpdateTable` above, lives in the request API, `DynaSharp.Requests`, which mirrors the service. [API levels](api-levels.md) says how the levels share a client.

An on-demand table can have a maximum throughput, above which DynamoDB throttles, and a KMS key of your account instead of the one DynamoDB owns:

```csharp
await client.UpdateTableAsync(new UpdateTableRequest
{
    TableName = "Shop",
    OnDemandThroughput = new OnDemandThroughput { MaxReadRequestUnits = 2_000, MaxWriteRequestUnits = 500 },
}, ct);
```

`CreateTableRequest` and each `GlobalSecondaryIndex` take `OnDemandThroughput` too; an index has maximums of its own. A member left out keeps its value, `-1` removes a maximum, and a table with provisioned capacity takes none. `SSESpecification = new SSESpecification { Enabled = true }` switches to the AWS managed key, with `KMSMasterKeyId` to a key of yours, and `Enabled = false` back. Each change puts the table into updating, so the next waits for it.
