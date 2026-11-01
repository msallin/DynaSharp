# DynaSharp - public API

The public API as shipped, and the contract for changing it. A change to the public surface changes this file in the same commit, and a
change of contract goes through the lead. The key grammar is in `requirements.md`, the compile-time rules in `diagnostics.md`, the internals
in `architecture.md`. The XML docs say what each member does in detail; this file says what the surface is and which rules hold across it.

The surface itself is tracked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`, build time only, on `DynaSharp` and `DynaSharp.Testing`: `PublicAPI.Shipped.txt`
in each project holds what is released, a change goes into `PublicAPI.Unshipped.txt`, and `RS0016` and `RS0017` fail a build that does not say so.

Examples use this model.

```csharp
public enum OrderStatus { Open, Shipped }

[DynamoDbDocument]
public partial record Address(string City, string? Zip);

[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{OrderId}")]
[GlobalIndexKey("StatusIndex", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
public partial record Order
{
    public required string UserId { get; init; }
    public required Guid OrderId { get; init; }
    public required OrderStatus Status { get; init; }
    public required DateTimeOffset PlacedAt { get; init; }
    public decimal Total { get; init; }
    public int Lines { get; init; }
    public Address? ShipTo { get; init; }

    [Version]
    public long Revision { get; init; }
}

[DynamoDbEntity]
[Discriminator("CUSTOMER")]
[PartitionKey("USER#{Id}")]
[SortKey("PROFILE")]
public partial record Customer
{
    public required string Id { get; init; }
    public string? Email { get; init; }
    public int OrderCount { get; init; }
}
```

## 1. Namespaces and layers

| Namespace | Holds |
|---|---|
| `DynaSharp` | `DynamoDbClient`, `DynamoDbClientOptions`, `AwsRegion`, `Table<T>`, `Index<T>`, every command and result, `Item`, `AttributeValue`, `AttributeType`, `Attr`, `Attr<T, TConverter>`, `Expr`, `Condition`, `SortKey`, `PartitionKey<T>`, `ContinuationToken`, `Page<T>`, `Mixed<...>`, `ConsumedCapacity`, `CapacityDetail`, `ItemCollectionMetrics`, `CancellationReason`, `ReturnConsumedCapacity`, `ReturnItemCollectionMetrics`, `ReturnValue`, `ReturnValuesOnConditionCheckFailure`, `Select`, `TableStatus`, `DynamoDbException` and the exceptions of the service, `KeyFormatException` |
| `DynaSharp.Expressions` | The structs that conditions compose from, which are return types and not written by hand: `Compare<,>`, `And<,>`, `Or<,>`, `Not<>`, `Between<,>`, `AttributeExists`, `BeginsWith`, `Arg<,>`; `ICondition`, `ExpressionSink`, `ExpressionHandler`, `CommandHandle` |
| `DynaSharp.Requests` | Every `XxxRequest` and `XxxResponse` record and its structure types, and the `XxxAsync(request)` methods as extension methods on `DynamoDbClient` (static class `DynamoDbRequests`) |
| `DynaSharp.Streams` | `DynamoDbStreamsClient` with its records and enums |
| `DynaSharp.Mapping` | Attributes, `IDynamoDbDocument<T>`, `IDynamoDbEntity<T>`, `IDynamoDbDiscriminated<T>`, `IKey`, `IKey<T>`, `DynamoDbEntityInfo`, `DynamoDbIndexInfo`, `DynamoDbKeyTemplate`, `DynamoDbKeyInput` |
| `DynaSharp.Keys` | `KeyWriter`, `KeyReader`, `KeyTimeFormat` |
| `DynaSharp.Serialization` | `DynamoDbReader`, `DynamoDbWriter`, `IDynamoDbConverter<T>`, `IDynamoDbSetElementConverter<T>`, `IAddableConverter`, built-in converters, `ConverterChecks` |
| `DynaSharp.Credentials` | `AwsCredentials`, `ICredentialsProvider`, `IRefreshableCredentialsProvider` (optional: a provider that can drop credentials DynamoDB rejected), the providers |
| `DynaSharp.Testing` | The test package, section 16 |

A type is in `DynaSharp` if a user of `Table<T>` has to name it. A type only the streams operations reach is in `DynaSharp.Streams`, and a
type table administration also uses stays in `DynaSharp.Requests`, so `Streams` depends on `Requests` and never the reverse. `DescribeAsync`
returns `TableDescription` from `DynaSharp.Requests`, and `BatchIncompleteException` holds `WriteRequest` from there.

Members that exist because generated code lives in the consumer's assembly, such as `KeyWriter` and `ConverterChecks`, are public, hidden from
IntelliSense and infrastructure: they serve the generator and converter authors, and they change with the package.

There are three ways in, and the shape of a call tells them apart.

| | Request API | `Table<Item>` | `Table<Entity>` |
|---|---|---|---|
| Entry | `client.GetItemAsync(new GetItemRequest { ... })` | `client.Table("Shop", "pk", "sk")` | `shop.Of<Order>()`, `client.Table<Order>("Shop")` |
| Arguments | Request records | `AttributeValue` keys, `string` paths, `Item` | `Order.Keys.Of(...)`, `Order.Keys.Pk.Of(...)`, `Order.Attrs.X`, `Order` |
| Results | `XxxResponse` | `GetResult<Item>`, `WriteResult<Item>`, `Page<Item>` | The same over `Order` |
| Async type | `Task<T>` | `ValueTask<T>` | `ValueTask<T>` |
| A bad expression fails | At DynamoDB | At DynamoDB, with the rendered expression attached | At compile time for handles, at DynamoDB for raw paths |

`Table<T>` is the primary API. The request API is a sibling, for administration, unmodeled parameters and migration, not a layer beneath it:
both run on the same pipeline with the same telemetry. Its member names follow the service model in botocore. An `Item` goes into any request, because it implements
`IReadOnlyDictionary<string, AttributeValue>`, and `ContinuationToken.ToKey()` and `FromKey(...)` bridge paging.

## 2. Client

```csharp
using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2") });
```

`DynamoDbClient` is sealed and `IDisposable`. Its members are `Table(name, partitionKey, sortKey = null)`, `Table<T>(name)`, `TransactWrite()`,
`TransactGet()`, `SendRawAsync(operation, requestJson, ct)` and `Dispose()`. `new DynamoDbClient()` takes everything from the environment.
There is no interface: the test seam is the `HttpMessageHandler`.

| Option | Default | Note |
|---|---|---|
| `Region` | `AWS_REGION`, `AWS_DEFAULT_REGION`, the selected profile | Required, also with `ServiceUrl`: the signature contains it |
| `ServiceUrl` | `AWS_ENDPOINT_URL_DYNAMODB`, `AWS_ENDPOINT_URL`, the public endpoint | Absolute http or https, no user info, path, query or fragment |
| `Credentials` | The default chain | The client does not dispose a provider it is given |
| `HttpHandler` | An owned pooled `SocketsHttpHandler` | The client does not dispose a handler it is given |
| `AttemptTimeout` | 30 s | Per attempt, credentials lookup included; surfaces as `TimeoutException` |
| `MaxAttempts` | 4 | Including the first; 1 means no retries. The remarks of this property are the public description of the retry rules |
| `LoggerFactory` | None | |
| `TimeProvider` | `TimeProvider.System` | Timeouts, retry delays, batch backoff, signing time, `WaitUntilActiveAsync` |

`DynamoDbClientOptions` is a sealed record, so a test writes `options with { HttpHandler = stub, MaxAttempts = 1 }`.

## 3. Tables and indexes

```csharp
Table<Item> shop = client.Table("Shop", "pk", "sk");
Table<Order> orders = shop.Of<Order>();                       // the same table, key names from Order, cached per type
Table<Customer> customers = client.Table<Customer>("Shop");   // the same from the client
Index<Order> byStatus = orders.Index(Order.Indexes.StatusIndex);   // key names from the entity
Index<Item> byStatusRaw = shop.Index("StatusIndex", "status", "placedAt");
Index<Item> byStatusMixed = shop.Index<Order>(Order.Indexes.StatusIndex);   // no schema, key names from the entity
```

`Table<T>` is sealed, immutable and thread-safe. `T` is `Item` or a generated type, and it decides only how an item is written and read.
`Of<T>()` and `client.Table<T>()` require an entity, not a keyless document. Opening a table makes no request. `DescribeAsync(ct)` and
`WaitUntilActiveAsync(timeout, ct)` cover the two administrative calls a table user makes; the timeout defaults to 5 minutes.

`Index<T>` has `Query` and `Scan`, so a write through an index does not compile. `orders.Index(name)` and `shop.Index<Order>(name)` throw `ArgumentException` for
an index the entity does not declare.

## 4. Generated surface

For `[DynamoDbEntity]` and `[DynamoDbDocument]` types, which must be `partial`:

| Member | Content |
|---|---|
| `T.Keys.Of(...)` | A `readonly struct PrimaryKey : IKey<T>` holding the placeholder values; costs no string. A key of another entity does not compile |
| `T.Keys.Pk`, `Sk`, `Gsi1Pk`, ... | One static class per template: `Format`, `Parse`, `TryParse`; on a partition key, of the table or of an index, `Of`, with the parameters of `Format`; on a sort key `Prefix`, `UpperBound`, `BeginsWith`, each at every depth `Prefix` has, and, when the last placeholder is a string, `PrefixPartial` |
| `T.Attrs.X` | A typed handle `Attr<TValue, TConverter>` per stored attribute, also on documents. `internal` unless the property, its type and its converters are public. An attribute name holding `.` or `[` and a `[KeyOnly]` property have none |
| `T.Indexes.X` | A `const string` per index, on an entity with an index: `orders-by_date` gives `OrdersByDate`, `GSI1` gives `Gsi1` |

`Order.Keys.Pk.Of(userId)` returns a `PartitionKey<Order>`, a readonly struct over the formatted key, one type for every key builder of an entity. It has no implicit conversion to `AttributeValue`,
because that would bring the mistake back that it rules out; `.Value` is the explicit way out, and the formatted string. `Order.Keys.Sk.BeginsWith()` and `BeginsWith(leading...)` return a `SortKey`.

`Keys`, `Attrs` and `Indexes` are reserved member names on every mapped type (`DYNA014`). A type with the name of a class generated into it is
`DYNA025`. A string before a delimiter in a sort key is `DYNA115` at severity `Info`: the ordering caveat is real but rare, and a warning would fail a
warnings-as-errors build on a hierarchical key. Attributes:

| Attribute | On | Meaning |
|---|---|---|
| `[DynamoDbEntity]`, `[DynamoDbDocument]` | type | A type with keys, a nested type without |
| `[PartitionKey]`, `[SortKey]` | type with a template, property for a plain key | `AttributeName` overrides the name. Defaults are `pk` and `sk`; a plain key keeps the attribute of its property |
| `[GlobalIndexKey(name)]` | type | `PartitionKey`, `SortKey`, `Sparse`, `PartitionKeyAttributeName`, `SortKeyAttributeName` |
| `[LocalIndexKey(name, sortKey)]` | type | `Sparse`, `SortKeyAttributeName` |
| `[Discriminator("X")]` | type | Required on an entity. `AttributeName` overrides `_type`; `OptOut = true` is for tables written by other tools, and keeps the type out of mixed reads |
| `[Version]` | property | Section 8 |
| `[KeyOnly]` | property | Stored only in a key, restored by parsing it, so a key must hold it without loss |
| `[DynamoDbAttribute(name)]` | property | Renames, and stores a computed property, which is then written and never read |
| `[DynamoDbIgnore]` | property | Not mapped |
| `[DynamoDbConverter(type)]` | property, type | First in converter selection |
| `[DynamoDbConstructor]` | constructor | Chooses the constructor reads use |

TTL is `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset`. There is no `[TimeToLive]`.

A read leaves `Item` out: the generated code goes from JSON to the type. A typed read checks `_type` when it is present and throws
`DynamoDbMappingException` naming the type it found. An unknown attribute is skipped. A missing `required` member throws, except a nullable one,
which keeps its default because null is omitted on write. A missing non-nullable set reads as empty. A key input whose attribute is missing is
restored from the key it feeds when that format is lossless, which is what an item created by an `Update` looks like.

Custom converters implement `IDynamoDbConverter<T>` with static members on a struct or class. A converter of a number or set implements
`IAddableConverter` to allow a typed `Add`.

## 5. Commands

Every method of a table that reads or writes items starts a command, a `readonly ref struct` over pooled state. `CreateTable()` starts an ordinary class, `CreateTableCommand`, because administration is no hot path, and it has no `MaxAttempts`. A command is built and sent in one expression: `diagnostics.md`
lists what the compiler refuses and what to write instead. Every fluent call uses up its handle and returns a new one, so using one twice
throws `InvalidOperationException`. The method that returns a command is the template of a request, and a helper takes and returns one:

```csharp
public static UpdateCommand<T> Audited<T>(this UpdateCommand<T> command, string user)
    where T : IDynamoDbDocument<T> => command.Set("updatedBy", user);

public static TCommand OwnedBy<TCommand>(this TCommand command, string owner)
    where TCommand : IConditional<TCommand>, allows ref struct => command.If(Attr("owner").Eq(owner));
```

| Interface | Implemented by | Member |
|---|---|---|
| `IConditional<TSelf>` | Put, Update, Delete commands and transaction steps | `If` |
| `IFilterable<TSelf>` | Query, Scan, fan-out and mixed reads | `Filter` |
| `IUpdatable<TSelf>` | `UpdateCommand<T>`, `UpdateStep` | `Set`, `SetIfNotExists`, `Add`, `Delete`, `Remove`, by raw path and by handle, and `Set` by handle of a nullable value |
| `IRetryable<TSelf>` | Every command and step, not `ParallelScanCommand<T>` or `ReturnCommand<,>` | `MaxAttempts` |
| `ITransactWriteChain<TSelf>`, `ITransactWriteStep<TSelf>` | `TransactWriteCommand` and the steps | Carry the verbs of section 11. Nothing outside the library implements them |

Their type parameter has `allows ref struct`, so a generic helper costs no box. A condition worth keeping in a field is a `Condition`, section 9.

| Command | Terminals |
|---|---|
| `GetCommand<T>` | `SendAsync`, `ItemAsync`, `ToRequestJson` |
| `PutCommand<T>`, `UpdateCommand<T>`, `DeleteCommand<T>`, `ReturnCommand<,>` | `SendAsync`, `ToRequestJson` |
| `QueryCommand<T>`, `ScanCommand<T>` | `ItemsAsync`, `PageAsync(after, ct)`, `PagesAsync`, `CountAsync`, `ToRequestJson` |
| `ParallelScanCommand<T>` | `ItemsAsync` |
| `FanOutQueryCommand<T>` | `ItemsAsync`, `PageAsync(pageSize, after, ct)`, `CountAsync`, `ToRequestJson` |
| `MixedQueryCommand<...>`, `MixedScanCommand<...>` | `ItemsAsync`, `PageAsync`, `PagesAsync`, `ToRequestJson` |
| `BatchGetCommand<T>`, `BatchWriteCommand<T>`, `TransactGetCommand`, a transaction chain | `SendAsync`, `ToRequestJson` |

Every async terminal takes a `CancellationToken`. `await command` is `SendAsync()` for the commands that have `SendAsync`; the awaiter is hidden
from IntelliSense and takes no token. A read has no default terminal (`DYNA202`), so `await shop.Query(pk)` does not compile. `ToRequestJson()` ends the
command, sends nothing and renders the body with everything enforced, for tests and debugging; on a batch it renders the first request.

| Clause | On |
|---|---|
| `MaxAttempts(n)` | This call only |
| `ReturnCapacity(detail = Total)` | All commands; the result carries it, except on `ReturnCommand<,>`, where only the metric does |
| `ReturnMetrics()` | Put, Update, Delete, `BatchWrite`, a transaction |
| `Consistent()` | Get, Query, Scan, fan-out, mixed reads, `BatchGet` |
| `Project(paths)`, `Project(attrs)` | Get, Query, Scan, fan-out, mixed reads, `BatchGet`. A typed read adds the discriminator and the keys it needs |
| `Filter`, `Limit(n)` | Query, Scan, fan-out, mixed reads. `Limit` is per page, as DynamoDB means it |
| `Descending()` | Query, fan-out, mixed query |

## 6. Reads

```csharp
GetResult<Item> got = await shop.Get("USER#alice", "PROFILE").Consistent().Project("fullName", "city").SendAsync(ct);
Customer? alice = await customers.Get(Customer.Keys.Of("alice")).ItemAsync(ct);
Customer? reloaded = await customers.Get(alice!).ItemAsync(ct);    // key from the entity

await foreach (Order order in orders
    .Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Status.Eq(OrderStatus.Shipped))
    .Descending()
    .ItemsAsync(ct))
{
}

Page<Order> page = await orders.Query(pk).Limit(25).PageAsync(ContinuationToken.Parse(fromClient), ct);
string? next = page.HasMore ? page.Next.ToString() : null;
int total = await orders.Query(pk).CountAsync(ct);
await foreach (Page<Order> p in orders.Query(pk).PagesAsync(ct)) { }

await foreach (Order order in byStatus.Query(Order.Keys.StatusIndexPk.Of(OrderStatus.Open), SortKey.Ge("2026-01-01")).ItemsAsync(ct)) { }
Page<Item> segment = await shop.Scan().Segment(2, of: 8).PageAsync(after, ct);
await foreach (Item item in shop.Scan().Parallel(segments: 8).ItemsAsync(ct)) { }
```

`Table<T>` and `Index<T>` take a `PartitionKey<T>` in `Query`, so `orders.Query(Customer.Keys.Pk.Of("a"))` does not compile; the partition key of a table and of its index have one type, so the
compiler does not tell those two apart. `Table<Item>` and `Index<Item>` take a `PartitionKey<TEntity>` of any entity, so `shop.Query(Order.Keys.Pk.Of(id)).Of<Customer, Order>()` reads
naturally. These are extension methods of `TableExtensions`, so a call that binds to a member keeps binding to it, and the `AttributeValue` overload stays as the escape hatch.

`ItemsAsync` and `PagesAsync` follow pages, filtered empty ones included, and can be enumerated once. `SortKey` offers only what a key
condition allows: `Eq`, `Lt`, `Le`, `Gt`, `Ge`, `Between`, `BeginsWith`. `CountAsync` is `Select: COUNT` summed over pages, and throws
`InvalidOperationException` with a projection. `Parallel(segments)` returns `ParallelScanCommand<T>`, whose only terminal is `ItemsAsync`, because a merged
page has no single key; concurrency is the segment count, and paging across requests goes through `Segment(n, of: m)`.

A typed `Query` or `Scan` on an entity with a discriminator filters on it: `#_type = :t`, visible in `ToRequestJson()`. It costs no capacity, because a
filter runs after the read, and `ScannedCount` can then exceed `Count`. On a query of a table or index keyed by the discriminator the key condition
does that job, because DynamoDB refuses a filter on a key.

`Page<T>` has `Items`, `Count`, `ScannedCount`, `ConsumedCapacity`, `HasMore` and `Next`. `ItemsAsync` drops capacity; a caller who wants it pages.

`ContinuationToken` is a position: `default` is the start, `Parse` and `TryParse` take what `ToString()` gave, and `Parse("")` gives the
default, so a loop on `Next.ToString()` checks `HasMore`. The text is safe in a URL and is encoded, not signed or encrypted: it carries the key
values of the last item, as `LastEvaluatedKey` does. `ToKey()` and `FromKey(key)` convert to and from a key of the request API.

## 7. Writes

```csharp
await shop.Put(new Item { ["pk"] = "USER#alice", ["sk"] = "PROFILE", ["fullName"] = "Alice" }).SendAsync(ct);
await shop.Put().With("pk", "USER#alice").With("sk", "PROFILE").IfNotExists().SendAsync(ct);    // Table<Item> only
WriteResult<Customer> replaced = await customers.Put(customer).ReturnOld().SendAsync(ct);

await shop.Update("USER#alice", "PROFILE")
    .Set("city", "Bern")
    .Add("orderCount", 1)
    .Remove("legacyFlag")
    .If(Attr("version").Eq(3))
    .SendAsync(ct);

int count = await customers.Update(Customer.Keys.Of("alice"))
    .Add(Customer.Attrs.OrderCount, 1)
    .MaxAttempts(1)
    .Return(Customer.Attrs.OrderCount)
    .SendAsync(ct);

await shop.Delete("USER#alice", "PROFILE").If(Attr("orderCount").Eq(0)).SendAsync(ct);
await customers.Delete(customer).SendAsync(ct);    // key from the entity
```

`Put` replaces the whole item. `Update` is an upsert unless `IfExists()`. `Put()` without an item and `With` exist on `Table<Item>` only, so a typed put
always goes through the entity. Conditions accumulate with `AND`. `IfNotExists()` and `IfExists()` are `attribute_not_exists` and `attribute_exists` on
the partition key: both on Put and Update, `IfExists()` on Delete.

| Clause | Result |
|---|---|
| `ReturnOld()` | Put, Update, Delete: `ALL_OLD` in `WriteResult<T>.Attributes` |
| `ReturnNew()` | Update: `ALL_NEW` in `Attributes` |
| `Return(attr)` | Update: `UPDATED_NEW` of one attribute. Returns `ReturnCommand<,>`, which only sends, and `SendAsync` gives the value. A missing attribute throws `DynamoDbMappingException`, never a default |
| `ReturnOldOnFailure()` | Put, Update, Delete and transaction steps: the item on `ConditionalCheckFailedException.Item`, or through `ItemFor` in a transaction |

`WriteResult<T>` has `Attributes`, `HasAttributes`, `ConsumedCapacity`, `ItemCollectionMetrics` and `Version`.

A retry repeats a write. `Add`, `list_append` and arithmetic in `SET` are applied twice if a response was lost, so `MaxAttempts(1)` or a condition makes
them exactly-once. After a lost response a conditional write judges what its first attempt wrote, which the `SendAsync` docs of each command spell out.

`Set` by handle takes a nullable value, and null is a `Remove`, for every attribute: `string?` for a reference type, and `Guid?` through an overload for a value type, which is a `Set` when it has a
value and costs what that costs. On a key input the rules of section 8 are those of the `Set` or `Remove` it turns into. `SetIfNotExists`, `Add` and `Delete` by handle reject null: a value type
does not compile, and a null of a reference type is a nullable warning and an `ArgumentNullException`. `Set`, `Add` and `Delete` by raw path and by handle both exist; a raw path is the escape
hatch for attributes outside the entity, and the checks of section 8 do not apply to it.

## 8. What a write of an entity enforces

**`[Version]`.** The property holds the version the caller last read, and 0 means new. Every enforced write is visible in `ToRequestJson()`.

| Write | Rule |
|---|---|
| `Put(entity)` | Version 0 adds `attribute_not_exists(<version>)`, any other `<version> = :old`, and the item is stored with `old + 1`. The entity is not mutated: `WriteResult<T>.Version` returns the stored version |
| `Update(key)` | `.IfVersion(n)` adds the condition and `SET <version> = :new` with `n + 1`. Without it the terminal throws `InvalidOperationException` naming the entity, unless `.IgnoreVersion()` was called. There is no `Update(entity)` |
| `Delete(key)` | The same rule, with the condition alone |
| `Delete(entity)` | Takes the version from the entity, as `IfVersion(entity.Version)`. `IfVersion(n)` replaces it, and `IgnoreVersion()` opts out |
| Transaction steps | The same rules: `IfVersion` on `UpdateStep` and `ConditionStep`, `IgnoreVersion` on all three steps |
| `BatchWrite` | A batch cannot carry a condition, so a put or delete of a versioned entity throws at the terminal unless `.IgnoreVersion()` was called on the batch |
| `Put(entity).IgnoreVersion()` | Stores the entity as it is, with no condition and no bump, for imports and migrations |

`IgnoreVersion()` after `IfVersion(n)` throws, because the condition cannot be taken out of the expressions; `IfVersion(n)` after `IgnoreVersion()` wins.
A failed condition is the ordinary `ConditionalCheckFailedException`. No retry and no read happens implicitly. The check runs in the terminal and costs an
entity without a version nothing. `WriteAttributes` does not bump, so `Item.From` stays a plain copy.

**Template inputs.** A typed `Set` or `Remove` by handle of a property that feeds a key template must leave that key consistent. The check runs at
the terminal, and raw paths are not checked.

| The update | Result |
|---|---|
| Sets all inputs of an index template | Writes the derived attribute in the same `SET` |
| Sets some inputs of a template | `InvalidOperationException` naming the template and what is missing |
| Touches an input of a template key of the table | `InvalidOperationException`: DynamoDB cannot update a key |
| Changes an input by `Add` or `SetIfNotExists` | `InvalidOperationException`: the value is not known here |
| Removes an input of a sparse index | Removes the derived attributes, unless another index still holds the item |
| Removes an input of an index that is not sparse | `InvalidOperationException` |
| Sets one key of a sparse index and does not produce the other | `InvalidOperationException` naming the other key, because the item would silently stay out of the index |

An input of the table key counts as not produced: such an item enters the index by `Put`.

**Literal keys.** A key that holds only literals counts as produced, on every index. When an update writes one key of an index, a sibling made only of literals is written in the same `SET`, so
`PartitionKey = "ALL"` can be entered by `Update`. A `Remove` that takes the item out of a sparse index removes it too. An update that does not touch such an index adds nothing to it, unless all its
keys are literal: then no property can bring an item in, so every typed update writes those keys with `if_not_exists`, as it does the discriminator, and an update at a key held by
another type does not overwrite them. An action of the update on the attribute itself takes precedence. An index with the partition key of the table is not one of these, even with a sort key of
literals: such an item enters it by `Put`.

**Discriminator.** `UpdateItem` can create the item, and such an item must not be invisible to the typed API. An `Update` on an entity with a discriminator
ends its `SET` with `#_type = if_not_exists(#_type, :t)`, also in a transaction, and visible in `ToRequestJson()`, unless that attribute is a key of the table. An action of the update on the
attribute itself takes precedence. A new item gets the type, and an existing one keeps it, so an update at a key held by another type does not rewrite it.

## 9. Conditions and expressions

```csharp
using static DynaSharp.Expr;     // Attr("x"), Attr<int>("n")

shop.Update(pk, sk).If(Attr("status").Eq("open"));                                         // struct condition, untyped
orders.Query(pk).Filter(Order.Attrs.Total.Gt(100m).And(Order.Attrs.Lines.Lt(10)));         // typed, composed
shop.Update(pk, sk).If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) < {10}"); // the grammar, for functions
shop.Update(pk, sk).Set($"{Attr("n")} = {Attr("n")} + {1}");                               // a raw action
orders.Update(key).Set(Order.Attrs.ShipTo.At(Address.Attrs.City), "Bern").IgnoreVersion();  // a nested path

private static readonly Condition Open = Condition.Of(Attr("status").Eq("open"));          // on the heap, to keep
shop.Update(pk, sk).If(Open & Attr("version").Eq(3));
```

Struct conditions cover comparisons, existence, `BeginsWith` and `Between`, compose with `And`, `Or`, `Negate` and allocate nothing. Interpolation covers the
rest of the grammar (`size`, `contains`, `attribute_type`, `list_append`, arithmetic in `SET`). In a hole, an `Attr` stands for its name, and a value,
an `AttributeValue`, a number or `handle.Value(x)`, for a value. A string in a hole is a value, so a name needs `Attr` (`DYNA210`), and an expression that
names no attribute throws `ArgumentException`.

Every name becomes a placeholder and equal values share one, so reserved words need no care. A path is names joined by dots with list indexes in
brackets; a dot always separates. `Attr` converts to its path as a `string`. `Attr<T, TConverter>` converts to `Attr` and not to `string`, so a wrong value
type does not compile through a raw overload. `Expr.Attr<T>(path)` is a number handle, for `Return(...)` and a typed `Add`. A custom condition implements
`ICondition`.

## 10. Batches

```csharp
BatchGetResult<Customer> found = await customers.BatchGet()
    .Key(Customer.Keys.Of("alice"))
    .Keys(ids.Select(Customer.Keys.Of))
    .SendAsync(ct);

BatchWriteResult written = await shop.BatchWrite()
    .Put(item)
    .Delete("USER#x", "PROFILE")
    .Put(orders, order)               // another type or table in the same batch
    .IgnoreVersion()
    .SendAsync(ct);
```

`BatchGet` reads one table, cut into requests of 100 keys. `BatchWrite` takes any tables, cut into requests of 25 writes that fit 16 MB. Requests run one
after the other, and what DynamoDB leaves unprocessed is sent again with the backoff of the retry policy on the client's clock. `MaxAttempts(n)` means what
it means everywhere, HTTP attempts per request. `MaxStalledRounds(n)` bounds the requests in a row without progress, default 8. `PutAll(IEnumerable<T>)`
and `DeleteAll<TKey>(IEnumerable<TKey>)` add a sequence; `Keys<TKey>` does on `BatchGet`.

A batch is not atomic. `BatchGetResult<T>` has `Items`, unordered, without missing keys, `ConsumedCapacity` and `Requests`. `BatchWriteResult` has `Written`,
`Requests`, `ConsumedCapacity` and `ItemCollectionMetrics`. A batch that gives up throws `BatchIncompleteException`, with the parsed `UnprocessedItems`
and `Written`, or `BatchGetIncompleteException<T>`, with `UnprocessedKeys` and the `Items` read so far. Both derive from `DynamoDbException` with the
code `BatchIncomplete`, and what they hold goes straight into the request API.

## 11. Transactions

```csharp
TransactMarker emailTaken = default;
try
{
    await client.TransactWrite()
        .Unique(markers, new EmailMarker(email, id), out emailTaken).ReturnOldOnFailure()
        .Put(customers, new Customer { Id = id }).IfNotExists()
        .Update(customers, Customer.Keys.Of("admin")).Add(Customer.Attrs.OrderCount, 1).If(Customer.Attrs.OrderCount.Lt(100))
        .Check(shop, "TENANT#1", "QUOTA").If(Attr("remaining").Gt(0))
        .Delete(shop, "CART#x", "ITEM#1")
        .SendAsync(ct);
}
catch (TransactionCanceledException e) when (e.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
{
    string? holder = e.ItemFor(emailTaken)?.GetString("customerId");
}

TransactGetResult snapshot = await client.TransactGet()
    .Get(customers, Customer.Keys.Of("alice"), out TransactSlot<Customer> slot)
    .Get(shop, "TENANT#1", "QUOTA", out TransactSlot<Item> quota)
    .SendAsync(ct);
Customer? read = snapshot.Read(slot).Item;
```

`Put` returns a `PutStep`, `Update` an `UpdateStep`, `Delete` and `Check` a `ConditionStep`. Each exposes its own clauses and the verbs of the next action,
so `.Add(...)` after `.Put(...)` does not compile. The verbs are extension methods of `TransactWriteExtensions` over `ITransactWriteChain<T>` and
`ITransactWriteStep<T>`. Each verb that starts an action has a form with `out TransactMarker`. A transaction takes 100 actions; the next verb throws
`InvalidOperationException`, and so does sending one with none.

Each step converts implicitly to `TransactWriteCommand`, so a loop reads
`TransactWriteCommand tx = client.TransactWrite(); foreach (...) { tx = tx.Put(table, item).IfNotExists(); }`. `Token(string)` sets the `ClientRequestToken`.
Without it each send generates one, and every retry of that send carries it.

`Unique(table, item, out marker)` is a put with `attribute_not_exists` on the partition key, and `Unique(table, key, out marker)` writes a marker item of
the key only. `TransactionCanceledException` has `ReasonFor(marker)`, returning `TransactionFailure`, `ItemFor(marker)`, returning `Item?`, `Failures`, a list of
`TransactionActionFailure(Index, Reason)`, and `CancellationReasons`, the raw list. `TransactWriteResult` has `ConsumedCapacity` and `ItemCollectionMetrics`, and
no stored versions.

`TransactGetCommand.Get` takes a table and a generated key, a partition key and a sort key, or a partition key alone, each with `out TransactSlot<T>`.
`TransactGetResult.Read(slot)` parses on demand and returns `GetResult<T>`.

## 12. Mixed-type reads

```csharp
await foreach (Mixed<Customer, Order> row in shop.Query(pk).Of<Customer, Order>().ItemsAsync(ct))
{
    if (row.Is(out Customer? customer)) { }
    else if (row.Is(out Order? order)) { }
}
```

`Of<...>()` is an extension of `QueryCommand<Item>` and `ScanCommand<Item>`, on a table or an `Index<Item>`, with 2 to 4 entity types, and returns a
`MixedQueryCommand<...>` or `MixedScanCommand<...>` with the same read clauses and the terminals of section 5. `MixedScanCommand` also has `Segment` and
`Parallel`. Its type arguments are constrained to `IDynamoDbDiscriminated<T>`, so a type that opted out does not compile. On a hand-written type that
claims it, `Of<...>()` throws `ArgumentException`.

The filter gains `#_type IN (:a, :b)`, so other types never reach the client and `ScannedCount` can exceed `Count`. A query of a table or index keyed by the
discriminator skips that filter, as a typed read does. Dispatch reads straight into the entity with no `Item` in between. `Mixed<...>` is a readonly struct
with `Is(out T)` per type and `Match(...)`. A read of `Table<T>` for an entity stays as it is. `Of<...>()` hangs off `Table<Item>`, so a mixed read of an index takes its key names
from an entity through `shop.Index<Membership>(Membership.Indexes.Gsi1)`.

## 13. Fan-out query

```csharp
await foreach (Order order in orders.Query(shardKeys, SortKey.Between(from, to)).Descending().ItemsAsync(ct)) { }
Page<Order> page = await orders.Query(shardKeys).PageAsync(pageSize: 50, after, ct);
```

`Query(IEnumerable<AttributeValue>, SortKey)`, `Query(IEnumerable<string>, SortKey)`, `Query(ReadOnlySpan<string>, SortKey)` and, by extension method, `Query(IEnumerable<PartitionKey<T>>, SortKey)` on
`Table<T>` and `Index<T>` return a `FanOutQueryCommand<T>`: one query per partition, merged. `Table<Item>` and `Index<Item>` take the keys of any entity. The `IEnumerable<string>` form has
`[OverloadResolutionPriority(-1)]`, so it never makes a call ambiguous that binds elsewhere; `Query(default)` and `Query([])` do not compile. Clauses are `Filter`, `Project`, `Limit` (items per partition page), `Descending`, `Consistent`,
`MaxAttempts`, `ReturnCapacity` and `MaxConcurrency(n)`, default 8.

Items merge in DynamoDB's sort key order: bytes for S and B, numeric for N, ties by the position of the partition key in the input. A partition is read
only when the merge needs it, so an early `break` does not read the rest, and at most one page per partition is buffered. A repeated or empty
partition key throws `ArgumentException`. An empty list reads nothing.

`PageAsync(pageSize, after, ct)` returns a `Page<T>` whose `Next` is a composite token with one entry per partition: its start key and the key of its next
item as a hint, or done. The hint spares a read of a partition the merge does not need yet, and never reaches a request. Tokens parse like plain ones,
and a token of a different partition list fails a count check; nothing is signed. If the table changes between pages, an item written before a hinted key
can come later than its place. `Page.ConsumedCapacity` adds every response of that `PageAsync`.

It is not a snapshot, and there is no `PagesAsync` and no `Parallel`.

## 14. Item and AttributeValue

`Item` is the flat item, a sealed mutable `IReadOnlyDictionary<string, AttributeValue>` that compares by content, and implements `IDynamoDbDocument<Item>`, which
is what makes `Table<Item>` the same engine. Build it with the indexer. `item["x"]` throws `KeyNotFoundException` naming the attribute. `GetString`, `GetNumber<T>`,
`GetBool`, `GetBinary`, `GetMap`, `GetList`, `GetStringSet`, `GetNumberSet` and `GetBinarySet` return null for a missing attribute and for DynamoDB NULL, and throw
`InvalidOperationException` for another type; a number that does not fit throws `FormatException` or `OverflowException`. These messages name the attribute, never the value. `ToString()`
returns DynamoDB JSON, which does contain values. `Item.From(value)` and `item.To<T>()` convert to and from a mapped type. An `Item` inside an `AttributeValue` is
read-only, and `new Item(other)` makes an editable copy. Read items have no attribute order, hand-built ones keep insertion order, and `Equals` is the only
sanctioned comparison. Its hash follows its content, so it does not belong in a `HashSet`.

`AttributeValue` is a struct with implicit conversions from `string`, `bool`, `int`, `long`, `double`, `decimal`, `byte[]` and `char`, which is a one-character
string. `Null` and the `FromXxx` factories cover the rest, `AsXxx` reads a value, and numbers stay text until read. `ToString()` is DynamoDB JSON.

## 15. Errors and escape hatches

`DynamoDbException` carries `ErrorCode`, the wire code without namespace, `StatusCode`, `RequestId`, `OperationName` and `TableName`. A code the library
knows has its class, in `DynaSharp`, and any other is a plain `DynamoDbException`. `DynamoDbMappingException` and `CredentialsUnavailableException` are there too, so a catch clause needs no other import.

| Failure | Where |
|---|---|
| A bad key of `Table<Item>`: empty string, empty binary, or a sort key on a table opened without one | `ArgumentException` at the call |
| A typed key built from bad input | `KeyFormatException`, an `ArgumentException` naming the placeholder and never the value |
| A rejected request of a command | `ValidationException`, with `Expressions`, `ExpressionNames` (`#n0 = status`) and `ExpressionValueTypes` (`:v0` is `S`), and one line in `Message`: `Expression: #n0 = :v0, where #n0 = status, :v0 (S)`. Values are not attached. Inside a transaction or a batch get the entries have a prefix such as `TransactItems[2].` |
| An item that does not fit the type | `DynamoDbMappingException` with `EntityType` and `AttributeName`, also for an item of another type; the discriminator value is named, because it is a type tag |
| A transaction | Section 11. `ConditionalCheckFailed` and `ValidationError` do not pass on a retry, `TransactionConflict` and `Throttled` may |

Compile errors stand in for what would otherwise be a `ValidationException` or a mapping failure: a key of another entity does not fit `Query`, `Index<T>` has no writes, `Put()` and `With` exist on
`Table<Item>` only, steps have their own clauses, `Return(attr)` ends the chain, `SortKey` has only legal operators, `Of<T>()` takes entities, and `Parallel()`
has no `PageAsync`.

Escape hatches: `client.SendRawAsync("ListTables", """{"Limit":1}"""u8.ToArray(), ct)` for what nothing models, with the pipeline's signing, retries and
telemetry; raw paths and interpolated expressions on typed commands; `Item.From` and `To<T>`.

`DynamoDbStreamsClient` takes the same options and has `ListStreamsAsync`, `DescribeStreamAsync`, `GetShardIteratorAsync`, `GetRecordsAsync` and `SendRawAsync`. There is
no consumer, by scope.

Credentials: `DefaultCredentialsProvider` is the chain of environment, shared files, container endpoint and instance metadata. A source that applies but is broken
fails instead of falling through. `EnvironmentCredentialsProvider`, `ProfileCredentialsProvider`, `ContainerCredentialsProvider`,
`InstanceMetadataCredentialsProvider` and `StaticCredentialsProvider` are the single sources.

Out of the API on purpose: an interface on `Table<T>` or `DynamoDbClient`, `Save`, identity maps, lazy loading, any read before a write, `ToRequest()` on an item command, `==` on
attributes, views and streaming callbacks over a response, compile-time expression templates. A write through `Table<Item>` is what it says: no `_type` and no
key check.

## 16. DynaSharp.Testing

The wire is the seam, and it serves every API. A test hands the client a `StubHandler` and `MaxAttempts = 1`, queues responses and asserts on `stub.Requests`.

```csharp
var stub = new StubHandler();
stub.Enqueue(StubResponse.Item(customer));
using var client = new DynamoDbClient(options with { HttpHandler = stub, MaxAttempts = 1 });
```

| Type | Members |
|---|---|
| `StubHandler` | `Enqueue`, `DefaultResponse`, `RecordRequests`, `Requests`; a constructor taking a responder |
| `StubResponse` | Results: `Item`, `Items`, `Attributes`, `BatchItems`, `Unprocessed`. Failures: `ConditionalCheckFailed`, `TransactionCanceled`, `DynamoDbError`. Parts: `Keys`, `KeyOf`, `Put`, `Delete`, `Reason`, `ConditionalCheckFailedReason`. Anything else: `Json` |
| `RecordedRequest` | `Operation`, `Headers`, `Method`, `RequestUri`, `Body`, `BodyText`, `BodyJson`, `ReadItem()`, `ReadItem<T>()`, `ReadKey()`, `ReadKey<T>()` |
| `RecordingHandler` | A `DelegatingHandler` that records `Exchanges` of a real client: `Request`, `Response`, `Error` |

The helpers go through `WriteAttributes`, so they cannot drift from the codec. `ToRequestJson()` is the second seam: what a chain would send, without a client.

## Table administration and conflicts

`orders.CreateTable().Including<Customer>().SendAsync(ct)` creates a table from what the entities declare: one entity cannot describe a single-table schema, so the table's indexes are the union over the entities named. Two entities that give an attribute two types throw at `Including`. `Provisioned(read, write)`, `WaitUntilActive()` and `IfNotExists()` shape it, and `ToRequest()` hands over a `CreateTableRequest` for what it does not cover, such as streams. A custom converter on a plain key counts as a string unless it implements `IAddableConverter`. `IfNotExists()` does not compare schemas, and takes a `DELETING` table for success. `DeleteTableAsync` deletes all items and does not wait.

`client.RetryOnConflictAsync(ct => ..., ct)` runs a delegate again on `ConditionalCheckFailedException`, `TransactionConflictException`, and a cancelled transaction with those reasons, up to 5 runs by default, with a small jitter through the client's `TimeProvider`. Nothing else retries a conflict. The delegate must read again, or the retry changes nothing. A conditional write whose response is lost fails its own pipeline retry, and the helper then applies the change again: `.MaxAttempts(1)` on the write avoids that.
