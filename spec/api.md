# DynaSharp - public API

The public API as shipped, and the contract for changing it. A change to the public surface changes this file in the same commit, and a
change of contract goes through the lead. The key grammar is in `requirements.md`, the compile-time rules in `../docs/diagnostics.md`, the internals
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
| `DynaSharp` | `DynamoDbClient`, `DynamoDbClientOptions`, `AwsRegion`, `Table<T>`, `Index<T>`, every command and result, the composition interfaces `IConditional<TSelf>`, `IFilterable<TSelf>`, `IUpdatable<TSelf>` and `IRetryable<TSelf>`, the extensions `AttrExtensions`, `ConditionExtensions`, `TableExtensions`, `MixedExtensions` and `TransactWriteExtensions`, `GuidExtensions`, which adds `Guid.FirstVersion7` and `Guid.LastVersion7`, `Item`, `AttributeValue`, `AttributeType`, `Attr`, `Attr<T, TConverter>`, `Expr`, `Condition`, `SortKeyCondition`, `PartitionKey<T>`, `ContinuationToken`, `ContinuationTokenSigner`, `Page<T>`, `Mixed<...>`, `ConsumedCapacity`, `CapacityDetail`, `ItemCollectionMetrics`, `CancellationReason`, `ReturnConsumedCapacity`, `ReturnItemCollectionMetrics`, `ReturnValue`, `ReturnValuesOnConditionCheckFailure`, `SelectKind`, `TableStatus`, `ConflictKind`, `DynamoDbException` and the exceptions of the service, `KeyFormatException`, `TableNotActiveException` |
| `DynaSharp.Expressions` | The structs that conditions compose from, which are return types, not written by hand and hidden: `Compare<,>`, `And<,>`, `Or<,>`, `Not<>`, `Between<,>`, `AttributeExists`, `BeginsWith`, `Contains<,>`, `Arg<,>`; `ICondition`, `ExpressionSink`, `ExpressionHandler`, `CommandHandle` |
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

Members that exist because generated code lives in the consumer's assembly are public, hidden from IntelliSense with `[EditorBrowsable(EditorBrowsableState.Never)]`,
which also keeps them out of the API reference, and infrastructure: they serve the generator and converter authors, and they change with the package. They are
`KeyWriter`, `KeyReader` and `KeyTimeFormat`, so `DynaSharp.Keys` has no visible type; the members of `ConverterChecks` that generated code calls; `DynamoDbEntityInfo`,
`DynamoDbIndexInfo`, `DynamoDbKeyTemplate`, `DynamoDbKeyInput` and `IKey`; and the structs of `DynaSharp.Expressions` with `ExpressionSink`, `ExpressionHandler` and
`CommandHandle`. What a call site meets stays visible: `IKey<T>`, `ICondition` and the `IDynamoDb...<T>` interfaces are constraints of the API, and `ConverterChecks.Expect`
is for a converter of your own.

There are three ways in, and the shape of a call tells them apart.

| | Request API | `Table<Item>` | `Table<Entity>` |
|---|---|---|---|
| Entry | `client.GetItemAsync(new GetItemRequest { ... })` | `client.Table("Shop", "pk", "sk")` | `shop.Of<Order>()`, `client.Table<Order>("Shop")` |
| Arguments | Request records | `AttributeValue` keys, `string` paths, `Item` | `Order.Keys.Of(...)`, `Order.Keys.Pk.Of(...)`, `Order.Attrs.X`, `Order` |
| Results | `XxxResponse` | `GetResult<Item>`, `WriteResult<Item>`, `Page<Item>` | The same over `Order` |
| Async type | `Task<T>` | `ValueTask<T>` | `ValueTask<T>` |
| A bad expression fails | At DynamoDB | At DynamoDB, with the rendered expression attached | At compile time for handles, at DynamoDB for raw paths |

`Table<T>` is the primary API. The request API is a sibling, for administration, unmodeled parameters and migration, not a layer beneath it:
both run on the same pipeline with the same telemetry. Its member names follow the service model in botocore, and so does what it requires: a response record marks `required` only what the model does, and a
member the model leaves optional is nullable or reads as its default, even one DynamoDB sends in practice, such as `TableDescription.TableName` or `CreateTableResponse.TableDescription`. An `Item` goes into any request, because it implements
`IReadOnlyDictionary<string, AttributeValue>`, and `ContinuationToken.ToKey()` and `FromKey(...)` bridge paging.

## 2. Client

```csharp
using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2") });
```

`DynamoDbClient` is sealed and `IDisposable`. Its members are `Options`, `Table(name, partitionKey, sortKey = null)`, `Table<T>(name)`, `TransactWrite()`,
`TransactGet()`, `DescribeTableAsync(tableName, ct)`, `RetryOnConflictAsync(...)`, `SendRawAsync(operation, requestJson, ct)` and `Dispose()`. `DescribeTableAsync` by name returns what the overload of the request API returns, a
`DescribeTableResponse`, and needs no request and no `using DynaSharp.Requests;`. `new DynamoDbClient()` takes everything from the environment.

`Options`, on `DynamoDbClient` and `DynamoDbStreamsClient`, is a `DynamoDbClientOptions` of what the client really uses, resolved once when it was created and not read again: a region and an endpoint from the environment are there and not
null, and `Credentials` is the provider in use. `client.Options with { MaxAttempts = 1 }` gives a second working client, which shares the provider and a handler that was given. A handler the client made stays its own, so `HttpHandler` is null then. `ServiceUrl`
holds an endpoint from the options or the environment. The public endpoint derived from the region stays null, although the client uses it: this narrows "what the client really uses" on purpose, so that `client.Options with { Region = other }` goes to the endpoint of
the new region, and a streams client built from the `Options` of a client gets its own endpoint. `ToString()` of the options names the type of the provider and shows no key, and an endpoint without user info.
There is no interface: the test seam is the `HttpMessageHandler`.

| Option | Default | Note |
|---|---|---|
| `Region` | `AWS_REGION`, `AWS_DEFAULT_REGION`, the selected profile | Required, also with `ServiceUrl`: the signature contains it |
| `ServiceUrl` | `AWS_ENDPOINT_URL_DYNAMODB`, `AWS_ENDPOINT_URL`, the public endpoint | Absolute http or https, no user info, path, query or fragment |
| `Credentials` | The default chain | The client does not dispose a provider it is given |
| `HttpHandler` | An owned pooled `SocketsHttpHandler` | The client does not dispose a handler it is given. It carries the requests to DynamoDB and no others: a provider of credentials has its own HTTP client, so a stubbed client still calls the real STS or metadata endpoint when the environment selects that provider |
| `AttemptTimeout` | 30 s | Per attempt, credentials lookup included; surfaces as `TimeoutException` |
| `MaxAttempts` | 4 | Including the first; 1 means no retries. The waits are random, up to 100, 200 and 400 ms for a transient failure, so a cold dependency has 700 ms at most and 350 ms on average, and up to 50, 100 and 200 ms for throttling. The remarks of this property are the public description of the retry rules |
| `LoggerFactory` | None | |
| `TimeProvider` | `TimeProvider.System` | Timeouts, retry delays, batch backoff, signing time, `WaitUntilActiveAsync` |
| `TokenSigner` | None: plain tokens | A `ContinuationTokenSigner` that signs and checks the continuation tokens of paged reads, see section 6. `ToString()` names its type only |

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
`WaitUntilActiveAsync(timeout, ct)` cover the two administrative calls a table user makes; the timeout defaults to 5 minutes. A response without a description gives one with the name of the table and `TableStatus.Unknown`, which a wait does not take for active.
A wait returns when the table and all its global secondary indexes are `ACTIVE`, because a table reads `ACTIVE` while an index backfills, on `Table<T>` and `CreateTableCommand`. DynamoDB allocates an index that `UpdateTable` adds before it backfills it, which takes minutes on any table and often more than five, and a backfill takes as long as the table is large, so such a wait needs a longer timeout than the default.
It also needs every global index that the entities declare to be listed, because an index `UpdateTable` just added may not be listed yet: the entity of a `Table<T>`, and on `CreateTableCommand` what `Including<T>` added too. Until then it keeps polling, and at the timeout the exception names the index and its message says that it was not listed. On `CreateTableCommand` the wait ends with that exception before the timeout, at the fifth look in a row that finds the table without the index, 1.5 seconds in: a create lists all its indexes, and a table that exists gets one only from an `UpdateTable` sent before the command, so the lag of `DescribeTable` is all there is to bear with. A look that lists every declared index starts the count over, and one that does not find the table does not count. A wait of `Table<T>` cannot know whether an `UpdateTable` is still to come and keeps polling. A local index needs no wait, and a table opened without an entity declares nothing. So `IfNotExists().WaitUntilActive()` over an old table without a declared index fails within seconds instead of returning.
There is no wait for one index. A wait that runs out throws `TableNotActiveException`, which is section 15. `CreateTableCommand.SendAsync` and its awaiter return `CreateTableResult`, a sealed class in `DynaSharp` with `Table` (the `TableDescription` it returned before), `Created` (this call's `CreateTable` created the table; false when `IfNotExists()` found it, also after a lost response whose retry met the table) and `TimeToLiveEnabled` (this call's `UpdateTimeToLive` was accepted, on a new table or a found one without it; false when it was on already, not asked for, or another instance got there first). The timeout bounds the wait and nothing else, so on `CreateTableCommand` the calls before it and time to live after it take their own time. A `CancellationToken` on the same timeout races it, and when the token wins the caller gets a bare `TaskCanceledException` or `OperationCanceledException`, which names no table and no index, so a wait gets less time than the token.

`Index<T>` has `Query` and `Scan`, so a write through an index does not compile. `orders.Index(name)` and `shop.Index<Order>(name)` throw `ArgumentException` for
an index the entity does not declare, which `DYNA207` reports for a constant name.

## 4. Generated surface

For `[DynamoDbEntity]` and `[DynamoDbDocument]` types, which must be `partial`:

| Member | Content |
|---|---|
| `T.Keys.Of(...)` | A `readonly struct PrimaryKey : IKey<T>` holding the placeholder values, which costs no string until it is written. It checks the values at the call, as `Format` and `T.Keys.Pk.Of(...)` do, so `Keys.Of("a#b")` throws `KeyFormatException`, and `T.Keys.TryOf(..., out key)` returns `false` for the same values. A key of another entity does not compile |
| `T.Keys.Pk`, `Sk`, `Gsi1Pk`, ... | One static class per key. For a template: `Format`, `Parse`, `TryParse`; on a partition key, of the table or of an index, `Of`, with the parameters of `Format`, and `TryOf` with those and `out` the key; on a sort key `Prefix`, `UpperBound`, `BeginsWith`, each at every depth `Prefix` has, when the last placeholder is a string `PrefixPartial`, and the conditions `Eq`, `Between`, `Range`, `Ge`, `Gt`, `Le` and `Lt` of section 6. For a plain key, or a template of one bare placeholder over the attribute of its property: `Of(value)`, with the type and the converter of the property, which gives a `PartitionKey<T>` on a partition key and the `AttributeValue` for a `SortKeyCondition` on a sort key, and `Format(value)` when the key is stored as a string. There is nothing to parse and no prefix. A sort key also has the conditions of section 6 that fit what it stores |
| `T.Attrs.X` | A typed handle `Attr<TValue, TConverter>` per stored attribute, also on documents. `TValue` is the type without its `?`, which is what `Set` and the conditions take, and the handle of a nullable property says so in its converter: `Attr<int, NullableHandleConverter<int, NumberConverter<int>>>`, and `Attr<string, NullableReferenceHandleConverter<string, StringConverter>>` for a reference type. `internal` unless the property, its type and its converters are public. An attribute name holding `.` or `[` and a `[KeyOnly]` property have none |
| `T.Indexes.X` | A `const string` per index, on an entity with an index: `orders-by_date` gives `OrdersByDate`, `GSI1` gives `Gsi1` |

`Order.Keys.Pk.Of(userId)` returns a `PartitionKey<Order>`, a readonly struct over the key as it is stored, one type for every key builder of an entity. A key of a template, and a plain string, cost nothing beyond what `Format` costs; a plain number or binary key goes through its converter once. It has no implicit conversion to `AttributeValue`,
because that would bring the mistake back that it rules out; `.Value` is the explicit way out, and the key as text, which is the digits of a number and the base64 of binary. `Order.Keys.Sk.BeginsWith()` and `BeginsWith(leading...)` return a `SortKeyCondition`.

`TryOf` is there for a value from outside, which would otherwise take a catch. `false` is what `Of` throws `KeyFormatException` for, no more and no less, because both run one piece of generated code over one `KeyWriter`, which is told not to throw. Nothing is thrown and caught on the way: `Keys.Pk.TryOf` costs what `Keys.Pk.Of` costs, the string of the key when it fits and nothing when it does not, and `Keys.TryOf` costs no string. On `false` the key is `default`. It exists where a value can be turned away. A key of literals has nothing to check. A plain key has no template, so nothing throws `KeyFormatException`, and an empty string there is DynamoDB's to reject. `Guid`, `DateOnly` and `DateTimeOffset` fit every template, so a key of those alone has none, and `Keys.TryOf` of an entity with a key of each kind checks the keys that can fail. The rule is by encoding and not by template: a plain number counts, although only a delimiter next to it can reject it, as does `d3` over a `byte`. When a placeholder is called `Key`, the `out` parameter is `key_`.

The check of `Keys.Of` renders the key into a stack buffer and drops it, so it allocates nothing, a value longer than the buffer borrows from the pool as a write does, and a command renders the key again when it writes it. A key that cannot fail skips the check. `default(PrimaryKey)` has seen no `Of`, so it throws when it is written.

A timestamp in a template is in UTC, and a bare `{At}` is `{At:ticks}`: it stores `2026-10-09T10:00:00.1234567Z`, the seven digits a stored attribute holds, and only that format is lossless, so a read can restore the property from the key. `{At:ms}` stores `2026-10-09T10:00:00.123Z`, which is what keys the AWS SDK wrote hold. The other formats are in `requirements.md`.

A value that holds the delimiter of its template throws `KeyFormatException`, unless the placeholder has the format `raw`: with `[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]` over `[KeyOnly] public required string Key`, `Keys.Pk.Format("a#b")` is `appointment#a#b`, and `Parse` gives `a#b` back. `[KeyOnly]` belongs there because a `Put` stores an input of a template as an attribute of its own as well, so without it every item a `Put` writes gets an extra `Key`, and nothing says so, while an `Update` writes none. It is for items that do not hold the attribute: on a table whose items hold it, a `Put` drops it. A raw value ends only where the key ends, so `raw` goes on a `string` placeholder that is the last thing in the template, and anything else does not compile (`DYNA118`, `DYNA104`). Only the delimiter check goes. What else a string may not be it may not be here, so a value that is empty, not well-formed, or holds a control character or U+10FFFF throws, on a write and through `Keys.Of` on a read, and `Parse` does not accept such text, so a stored key that holds one gives `DynamoDbMappingException` on a read that restores an input from it, and a read of many items ends at the first such key, with nothing of its page, unless `OnMappingFailure` skips it, section 6. `TryOf` is the tool for a value from outside, and `Keys.TryOf` answers for the key of the table only: with a strict index it returns `true`, and the `Put` throws. A plain key is the better choice for an entity with keys of several shapes, and for values that must miss and not throw. `Format`, `Parse`, `TryParse`, `PrefixPartial`, `Eq`, the ranges and a read that restores a key input work as they do for a string that ends the key, and the prefixes before it end on their delimiters as ever. A value without a delimiter gives the bytes it gives without `raw`, so taking `raw` off or putting it on moves no stored key. The format is per template, so a `{Key}` of the same property in an index still rejects what `{Key:raw}` of the table key takes, and a `Put` of such a value throws.

`Keys`, `Attrs` and `Indexes` are reserved member names on every mapped type (`DYNA014`). A type with the name of a class generated into it is
`DYNA025`. A string before a delimiter in a sort key is `DYNA115` at severity `Info`: the ordering caveat is real but rare, and a warning would fail a
warnings-as-errors build on a hierarchical key. Attributes:

| Attribute | On | Meaning |
|---|---|---|
| `[DynamoDbEntity]`, `[DynamoDbDocument]` | type | A type with keys, a nested type without |
| `[PartitionKey]`, `[SortKey]` | type with a template, property for a plain key | `AttributeName` overrides the name. Defaults are `pk` and `sk`; a plain key is the attribute of its property, so `Naming` of `[assembly: DynamoDbDefaults]` converts it |
| `[GlobalIndexKey(name)]` | type | `PartitionKey`, `SortKey`, `Sparse`, `PartitionKeyAttributeName`, `SortKeyAttributeName` |
| `[LocalIndexKey(name, sortKey)]` | type | `Sparse`, `SortKeyAttributeName` |
| `[Discriminator("X")]` | type | Required on an entity, unless `DiscriminatorOptOut` of `[assembly: DynamoDbDefaults]` opts out for it. Without it `DYNA020` is an error that suggests the name in snake case and upper case, `TRANSPORT_RECORD` for `TransportRecord`, and the type is generated as with `OptOut`, so that this error alone fails the build. `AttributeName` overrides `_type`; `OptOut = true` is for tables written by other tools, and keeps the type out of mixed reads |
| `[Version]` | property | Section 8 |
| `[KeyOnly]` | property | Stored only in a key, restored by parsing it, so a key must hold it without loss |
| `[DynamoDbAttribute(name)]` | property | Renames, whatever `Naming` says, and stores a computed property, which is then written and never read |
| `[ComputedFrom(names)]` | computed property | Names the stored properties the getter reads, which lets it feed an index template, section 8 |
| `[DynamoDbIgnore]` | property | Not mapped |
| `[OmitEmpty]` | property, entity or document type | An empty string, list, set, dictionary or array counts as null, section 7, nullable or not. On a type it covers those properties of that type that feed no key, and a nested type has its own. `[OmitEmpty(false)]` takes it back. On a property of another type, that feeds a key, or that a read cannot do without, it is `DYNA030` |
| `[DynamoDbConverter(type)]` | property, type | First in converter selection, the property before the type |
| `[DynamoDbConstructor]` | constructor | Chooses the constructor reads use |
| `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` | enum | The generated converter answers `CountsAsMissing` for a stored `S` that is no name of a member, an empty one included, so a property of the enum reads it as a missing attribute, as below. `UnknownEnumName.Throw` is the default. Writes and stored bytes do not change. An element of a list, set or map and a plain key still throw. A `[Flags]` enum, stored as a number, and an enum with its own `[DynamoDbConverter]` have nothing for it to do, which is `DYNA033` on the attribute. So has a property of the enum where an unknown name throws anyway, a `required` one that is not nullable, one set by a constructor parameter without a default and a plain key, which is `DYNA033` on the property |
| `[assembly: DynamoDbDefaults(...)]` | assembly | What a project says once: `Naming`, `DiscriminatorOptOut`, `OmitEmpty`, below |
| `[assembly: DynamoDbDefaultConverter(type, converter)]` | assembly | Once per type. Third in converter selection, after `[DynamoDbConverter]` on the property and on the type, before the built-ins, below |

The default names are frozen: `pk`, `sk`, `<index>pk` and `<index>sk`, with the name of the index in lower case, and `_type`. They are in the data of every table a default wrote, so a new default would leave those items behind with no error, and an `AttributeName` is the only way to another name.

Real services repeat attributes that are the same on every type, so an assembly says them once. They hold for the types the assembly declares, nested ones too, and an analyzer that meets a type from metadata reads the defaults of the assembly of that type:

```csharp
[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, DiscriminatorOptOut = true, OmitEmpty = true)]
[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(StoredTimestampConverter))]
```

| Default | Meaning |
|---|---|
| `Naming` | `AttributeNaming.AsDeclared`, the default, `CamelCase` or `SnakeCase`, by the rules of `JsonNamingPolicy.CamelCase` and `JsonNamingPolicy.SnakeCaseLower`, for every attribute that derives from a property, on entities and on documents. `[DynamoDbAttribute]` and `AttributeName` win. Two properties that collide after the conversion are `DYNA004` |
| `DiscriminatorOptOut` | An entity without `[Discriminator]` behaves as `[Discriminator(OptOut = true)]`, and `DYNA020` does not fire. `[Discriminator("X")]` still wins |
| `OmitEmpty` | Every property `[OmitEmpty]` is valid on behaves as marked, except what feeds a key and what a read cannot do without, which stay written and report nothing. `[OmitEmpty(false)]` takes it back for a property or a type |

The frozen names derive from no property, so `Naming` does not touch them. A plain key is not on that list: its attribute derives from its property, so `[PartitionKey] public string UserId` is `user_id` under `SnakeCase`, and a table that has another key takes `AttributeName`, because DynamoDB cannot rename a key. A template of one bare placeholder is the attribute of its property only if its `AttributeName` is the converted name. Changing `Naming` renames stored attributes: items written before keep the old names, and a read takes the new ones for missing, which is no error unless the property is `required`. Renaming a property does the same under every naming, `AsDeclared` too, because the attribute follows the name of the property, and a rename refactoring compiles. A property whose name may change takes `[DynamoDbAttribute]`.

A default converter is first found after what the property and the type of the value say, for the type itself, its nullable form and elements of lists, dictionaries and arrays. An element of a set needs an `IDynamoDbSetElementConverter<T>`, or the set is `DYNA003`. In a key it does what `[DynamoDbConverter]` on the property does: a plain key goes through it and takes any type, so `DYNA017` does not apply, and a placeholder of a template keeps its format, so a template of a type without one is `DYNA103`. A `[Version]` keeps the built-in converter, because an update adds to it. A converter that does not fit is `DYNA008`, and two defaults for one type, a nullable type and an open generic type are `DYNA029`.

TTL is `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset`. There is no `[TimeToLive]`.

The library ships opt-in converters beside the built-ins, for what other writers stored. None is a default, because defaults are frozen with the data they wrote; each goes into `[DynamoDbConverter]` or `[DynamoDbDefaultConverter]`:

| Converter | Of | Stores | Reads |
|---|---|---|---|
| `EpochSecondsConverter` | `DateTimeOffset` | `{"N":"1793448000"}`, whole seconds in UTC | Whole seconds in the range of `DateTimeOffset`, with offset zero |
| `SetArrayConverter<T, TElement>` | `T[]` | A set of the kind `TElement` says | That set, an empty one for none |
| `BoolAsNumberConverter` | `bool` | `{"N":"1"}`, `{"N":"0"}`, the AWS SDK in conversion V1 | Zero as `false`, any other number as `true`, as the SDK does; a `BOOL` throws |
| `SdkDateTimeConverter` | `DateTime` | `{"S":"2026-10-31T12:00:00.123Z"}`, UTC with three digits cut off, `Unspecified` taken as local, as SDK 4 in V1 and V2 | What `DateTimeConverter` reads, as `Utc`, which is SDK 4 under `RetrieveDateTimeInUtc = true` |
| `OffsetTimestampConverter` | `DateTimeOffset` | `{"S":"2026-11-20T11:00:00.0000000+01:00"}`, `"O"` with the offset, `+00:00` for UTC | The forms `DateTimeOffsetConverter` reads, keeping the offset, and `Z` as offset zero |
| `EmptyStringAsMissingConverter<T, TConverter>` | any | What `TConverter` writes | What `TConverter` reads, and answers `CountsAsMissing` for `{"S":""}` on top of what `TConverter` answers. Passes `IsOmitted` on, and not `TryReadMissing`, so that `DYNA209` still sees a `Select` that throws |

The generator knows two of them by name, because a plain key depends on what they store: `BoolAsNumberConverter` as a plain key is a number key, and `SdkDateTimeConverter` as a plain sort key has the ranges.

A built-in converter that refuses a value of its type on write, `DateTimeConverter` a `DateTime` of `DateTimeKind.Unspecified` and the number converters NaN or an infinity, throws `ArgumentException` and `ArgumentOutOfRangeException` as before. On the generated write of a property through one of them, alone or in a list, a dictionary or a set, a catch with a filter names the property, its type, its attribute, what was wrong and the fix, and no value: `The property LoggedAt of Logbook, the attribute "logged_at", cannot be written: its DateTime has DateTimeKind.Unspecified, so it names no instant. A DateTime that was never set, default(DateTime), has this kind: set it to a UTC time first. Only for times that the AWS SDK wrote, store it with [DynamoDbConverter(typeof(SdkDateTimeConverter))], ...`. The exception keeps its type and holds that of the converter inside; any other failure passes the filter as it is, and the success path pays nothing. A key template reports such a `DateTime` with `KeyFormatException`, which names the template. A write by handle, such as `Set(Attrs.At, value)`, throws what the converter throws.

A read leaves `Item` out: the generated code goes from JSON to the type. A typed read checks `_type` when it is present and throws
`DynamoDbMappingException` naming the type it found. An unknown attribute is skipped. A missing `required` member throws, except a nullable one,
which keeps its default because null is omitted on write. A missing non-nullable set reads as empty. A key input whose attribute is missing is
restored from the key it feeds when that format is lossless, which is what an item created by an `Update` looks like.

Custom converters implement `IDynamoDbConverter<T>` with static members on a struct or class. The members are static abstract, so a converter generic over its value is `CA1000` in a project that treats that as an error, and the way out is one struct per type over a helper. A converter of a number or set implements
`IAddableConverter` to allow a typed `Add`. A converter of a set answers `TryReadMissing` with an empty set, because an empty set is a missing attribute in DynamoDB and `Select` throws for it otherwise. That member is infrastructure, but it stays visible for this one case, unlike the members section 1 hides.

A converter that implements `CountsAsMissing(in DynamoDbReader reader, AttributeType type)` says that a stored value counts as if the attribute were not in the item: the mirror of `IsOmitted` for a read, for a lenient enum or a blank string as null. `Read` stays what it is, strict, because a list has positions, so such a converter looks at the value twice, first here and then in `Read`, and only on a property whose converter answers. The default answers false, and the generated read of any other property holds no question. `[DynamoDbConverter]` and `[DynamoDbDefaultConverter]` work alike here, and `[OmitEmpty]` passes the question on: it has no answer of its own, so a stored `""` still reads as `""`. The generated converter of an enum with `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` answers as well, true for a string that is no name of a member, and the forwarder its handle names passes the question on, so `Select` asks it too.

As if the attribute were not in the item means what a read does for that: a property keeps its initial value, a nullable one too, so an initializer on a `Payload?` stays and only one without gives null. A `required` member throws `DynamoDbMappingException`, and an input of a key that holds it is restored from the key. A `{"NULL":true}` on a nullable property reads as null without asking. An element of a list, a map or a set is not asked, and neither is a plain key, whose initial value would be another item, nor `Keys.X.Parse` or an `Item`.

A read asks first and then skips or reads, so a converter does not consume the value. The reader comes by `in`, on the value, and the getters work on it. A call that moves a reader moves a copy that the compiler makes for that call, and passing it on by `ref` or assigning to it does not compile, so nothing a converter does there moves what the read goes on with. To look inside a list or a map it copies the reader once, `var map = reader;`, and walks the copy, which is less than 200 bytes on the stack and no allocation.

## 5. Commands

Every method of a table that reads or writes items starts a command, a `readonly ref struct` over pooled state. `CreateTable()` starts an ordinary class, `CreateTableCommand`, because administration is no hot path, and it has no `MaxAttempts`. A command is built and sent in one expression: `diagnostics.md`
lists what the compiler refuses and what to write instead. Every fluent call uses up its handle and returns a new one, so using one twice
throws `InvalidOperationException` (`DYNA204` where a build can see it), and a call whose result is dropped sends nothing (`DYNA203`). The method that returns a command is the template of a request, and a helper takes and returns one:

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
| `IUpdatable<TSelf>` | `UpdateCommand<T>`, `UpdateStep` | `Set`, `SetIfNotExists`, `Add`, `Delete`, `Remove`, by raw path and by handle, and by handle only `Set` of a nullable value and `SetIfNotNull` |
| `IRetryable<TSelf>` | Every command and step, not `ParallelScanCommand<T>`, `ReturnCommand<,>` or what `Select` leaves | `MaxAttempts` |
| `ITransactWriteChain<TSelf>`, `ITransactWriteStep<TSelf>` | `TransactWriteCommand` and the steps | Carry the verbs of section 11. Nothing outside the library implements them |

Their type parameter has `allows ref struct`, so a generic helper costs no box. A condition worth keeping in a field is a `Condition`, section 9.

| Command | Terminals |
|---|---|
| `GetCommand<T>` | `SendAsync`, `ItemAsync`, `ToRequestJson` |
| `PutCommand<T>`, `UpdateCommand<T>`, `DeleteCommand<T>`, `ReturnCommand<,>` | `SendAsync`, `ToRequestJson` |
| `QueryCommand<T>` | `ItemsAsync`, `ItemAsync`, `PageAsync(after, ct)`, `PageAsync(ct)`, `PagesAsync`, `CountAsync`, `ToRequestJson` |
| `ScanCommand<T>` | `ItemsAsync`, `PageAsync(after, ct)`, `PageAsync(ct)`, `PagesAsync`, `CountAsync`, `ToRequestJson` |
| `GetSelectCommand<,>` | `SendAsync`, `ToRequestJson` |
| `QuerySelectCommand<,>` | `ValuesAsync`, `ValueAsync`, `PageAsync(after, ct)`, `PageAsync(ct)`, `PagesAsync`, `ToRequestJson` |
| `ScanSelectCommand<,>` | `ValuesAsync`, `PageAsync(after, ct)`, `PageAsync(ct)`, `PagesAsync`, `ToRequestJson` |
| `ParallelScanCommand<T>` | `ItemsAsync` |
| `FanOutQueryCommand<T>` | `ItemsAsync`, `PageAsync(pageSize, after, ct)`, `PageAsync(pageSize, ct)`, `CountAsync`, `ToRequestJson` |
| `MixedQueryCommand<...>`, `MixedScanCommand<...>` | `ItemsAsync`, `PageAsync(after, ct)`, `PageAsync(ct)`, `PagesAsync`, `ToRequestJson` |
| `BatchGetCommand<T>`, `BatchWriteCommand<T>`, `TransactGetCommand`, a transaction chain | `SendAsync`, `ToRequestJson` |

Every async terminal takes a `CancellationToken`. `await command` is `SendAsync()` for the commands that have `SendAsync`; the awaiter is hidden
from IntelliSense and takes no token. A read has no default terminal (`DYNA202`), so `await shop.Query(pk)` does not compile. `ToRequestJson()` ends the
command, sends nothing and renders the body with everything enforced, for tests and debugging; on a batch it renders the first request.

| Clause | On |
|---|---|
| `MaxAttempts(n)` | This call only |
| `ReturnCapacity(detail = Total)` | All commands; the result carries it, except on `ReturnCommand<,>`, where only the metric does |
| `ReturnMetrics()` | Put, Update, Delete, `BatchWrite`, a transaction |
| `Consistent()` | Get, Query, Scan, fan-out, mixed reads, `BatchGet`. `InvalidOperationException` on an index known to be global: one opened through its entity, or by hand with another partition key than the table. An index of the partition key of the table opened by hand is left to DynamoDB |
| `Project(paths)`, `Project(attrs)` | Get, Query, Scan, fan-out, mixed reads, `BatchGet`. A typed read adds the discriminator and the keys it needs, and throws `DynamoDbMappingException` for what else the entity needs, which `DYNA208` reports |
| `Select(handle)` | Get, Query, Scan. One attribute without the entity, section 6. It ends the chain, as `Return(attr)` does, so every other clause comes before it |
| `Filter`, `Limit(n)` | Query, Scan, fan-out, mixed reads. `Limit` is per page and works before the filter, as DynamoDB means it. A cap on items is `.ItemsAsync(ct).Take(n)`, which reads until it has them and stops |
| `Descending()` | Query, fan-out, mixed query |
| `OnMappingFailure(callback)` | Query, Scan, fan-out, mixed reads. Skips an item that the type cannot read and hands its `DynamoDbMappingException` to the callback, section 6 |

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

Page<Order> page = await orders.Query(pk).Limit(25).PageAsync(ContinuationToken.Parse(fromClient), ct);    // null on the first page
string? next = page.HasMore ? page.Next.ToString() : null;
int total = await orders.Query(pk).CountAsync(ct);
await foreach (Page<Order> p in orders.Query(pk).PagesAsync(ct)) { }

Order? open = await orders.Query(pk).Filter(Order.Attrs.Status.Eq(OrderStatus.Open)).ItemAsync(ct);    // the first match, or null
Order? newest = await orders.Query(pk).Descending().Limit(1).ItemAsync(ct);                            // a read without a filter says that one is enough
Order? held = await orders.Query(Order.Keys.Of("alice", "o1")).ItemAsync(ct);                          // takes what Get takes

await foreach (Order order in byStatus.Query(Order.Keys.StatusIndexPk.Of(OrderStatus.Open), Order.Keys.StatusIndexSk.Ge(from)).ItemsAsync(ct)) { }
Page<Item> segment = await shop.Scan().Segment(2, of: 8).PageAsync(after, ct);
await foreach (Item item in shop.Scan().Parallel(segments: 8).ItemsAsync(ct)) { }
```

`Table<T>` and `Index<T>` take a `PartitionKey<T>` in `Query`, so `orders.Query(Customer.Keys.Pk.Of("a"))` does not compile; the partition key of a table and of its index have one type, so the
compiler does not tell those two apart. A key remembers the attribute it was built for, and `Query` throws `ArgumentException` naming the key builder to use when that is not the attribute it reads, because
such a query would come back empty without a word from DynamoDB. An index with the attribute of the table, a local one among them, takes the key of the table, and so does a fan-out by what each key says. An
overloaded index takes the key of every entity on it. `Table<Item>` and `Index<Item>` take a `PartitionKey<TEntity>` of any entity, so `shop.Query(Order.Keys.Pk.Of(id)).Of<Customer, Order>()` reads
naturally. These are extension methods of `TableExtensions`, so a call that binds to a member keeps binding to it, and the `AttributeValue` overload stays as the escape hatch.

`Query<TKey>(key)` on `Table<T>` takes what `Get` takes, so the key of another entity does not compile, and queries the partition key with `Eq` on a sort key if there is one. A member that
fails its constraint is no candidate, so a string or a `PartitionKey<T>` binds as it did. `ItemAsync` gives the first matching item in query order or null, on a table and on an index. It follows empty filtered
pages and stops at the first item. It adds no `Limit`, because `Limit` works before the filter and what goes out stays what `ToRequestJson()` shows, so a read of a partition without a filter asks for `.Limit(1)` itself.
Fan-out, mixed reads and scans have no `ItemAsync`.

### Sort key conditions

```csharp
invoices.Query(pk, Invoice.Keys.Sk.Between(from, to));        // [SortKey("INVOICE#{IssuedAt}#{Id}")]: from the first key at from to the last key at to
invoices.Query(pk, Invoice.Keys.Sk.Range(from, to));          // [from, to): the same, without the keys at to
invoices.Query(pk, Invoice.Keys.Sk.Ge(from));                 // from then on, and what else sorts after that
invoices.Query(pk, Invoice.Keys.Sk.Eq(issuedAt, id));         // that one key
stock.Query(pk, Stock.Keys.Sk.Between("books", 10, 99));      // [SortKey("{Shelf}#{Row:d3}#{Id}")]: the rows of one shelf
```

The builder of a sort key writes a condition from placeholder values, so nobody puts `Prefix` and `UpperBound` together by hand. `SortKeyCondition` stays, for a value that is at hand already. The arguments are the leading placeholders, as `Prefix` takes them, at every depth that has a value, and a key that ends in a placeholder has that depth as well.

| Generated | Key condition | A key that ends in a placeholder, at its full depth |
|---|---|---|
| `Eq(all placeholders)` | `= Format(...)` | The same |
| `Between(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND UpperBound(leading..., to)` | `BETWEEN Format(leading..., from) AND Format(leading..., to)` |
| `Range(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND Prefix(leading..., to)` | None |
| `Ge(x...)` | `>= Prefix(x...)` | `>= Format(x...)` |
| `Gt(x...)` | `> UpperBound(x...)` | `> Format(x...)` |
| `Le(x...)` | `<= UpperBound(x...)` | `<= Format(x...)` |
| `Lt(x...)` | `< Prefix(x...)` | `< Format(x...)` |

Bounds are whole fields, and a field that a bucket format truncates compares as its bucket: `Gt` of a day leaves out that whole day. A key that ends in a placeholder has no prefix at that depth, because no delimiter ends the last field, so the complete key is both bounds. An upper bound would be wrong there for a string: `Gt("a")` has to keep `ab`, which sorts above `a` and below `a` plus U+10FFFF. `Between("a", "m")` takes whole values, so `ma` stays out, which is the difference to `PrefixPartial`. A value that cannot be part of a key throws `KeyFormatException`, as `Format` does, and DynamoDB rejects a `from` above `to` with a `ValidationException`.

`Between` varies the last placeholder alone and keeps the ones before it equal, because that is the range that holds for every template: all keys between two bounds with a common start have that start. Both ends are included, so on a key that goes on behind the field it includes all of `to`. `Range` is the same range without `to`, for a read of `[from, to)`, and it comes with the same leading fields. `BETWEEN` includes its bounds there too, and what leaves `to` out is what is stored: every key at `to` goes on behind the prefix, so it sorts above that bound, and no key is the prefix itself, since no value is empty. So a `Range` of a bucket format stops before that whole bucket, `Range(to, to)` reads nothing, and `from` above `to` is the `ValidationException` of `Between`. A range across two whole tuples can be written by hand from `Prefix` and `UpperBound`. `Ge`, `Gt`, `Le` and `Lt` take the tuple and compare in the order of the key. They have one end, which a key condition cannot change, so they go on past the template: `Ge(at)` also reaches `PROFILE` above `INVOICE#`, which a typed read filters away after reading it and `Table<Item>` returns, and `Ge(year, month)` goes on through the years after that one.

What cannot be correct is not generated, so it does not compile.

- No range without a value. `Ge()` of a prefix alone asks for what lies outside the template. `BeginsWith()` is how to read all of it, and a template of literals has `Eq()`.
- No range ends on a field whose bytes do not follow its value: plain digits, where `10` sorts before `9`, a plain decimal, an enum by name, a custom type.
- No `Range` where nothing goes on behind the field, because a bound would be a key that `BETWEEN` takes. A key that ends in the placeholder has no prefix there, and in `{Day:d3}#` the prefix of a day is the whole key. `{Day:d3}#end` goes on, and has it. It is not generated where `Between` is not, so a plain key has none.
- A string is in order at the end of the key and before a space. Before any other delimiter a value such as `a b` sorts below `a` (`DYNA115`), so no range ends on such a field and no open range passes one. `{Shelf}#{Row:d3}#{Id}` has `Between(shelf, from, to)` and no `Ge(shelf, row)`, and no range of shelves. A raw value always ends the key, so it is in order, delimiters and all, and `DYNA115` has nothing to say about it.

A plain sort key is compared as it is stored, so each condition is `SortKeyCondition` over `Of`. A number, binary, `string`, `Guid`, `DateTimeOffset`, `DateTime`, `DateOnly` and `TimeOnly`, whose stored form is in the order of the value, have all of `Eq`, `Between`, `Ge`, `Gt`, `Le` and `Lt`. So do `SdkDateTimeConverter`, whose text has a fixed width, and `BoolAsNumberConverter`, a number. A `TimeSpan`, an enum, `OffsetTimestampConverter`, whose text does not sort across offsets, and a custom converter have `Eq` alone, and a `string` also has `BeginsWith(prefix)`.

A version-7 `Guid` starts with its time in milliseconds, and the lowercase text a `Guid` is stored as, in a key and in an attribute, sorts by that time. `Guid.FirstVersion7(at)` and `Guid.LastVersion7(at)`, static members that `GuidExtensions` adds to `Guid`, are the lowest and the highest version-7 value of the millisecond of `at`: its time with every random bit 0, `…-7000-8000-000000000000`, and with every random bit 1, `…-7fff-bfff-ffffffffffff`. So every value `Guid.CreateVersion7(at)` makes lies between them, `LastVersion7` sorts below every value of the next millisecond, and they go into the conditions of a sort key with a `Guid` at the end: `Keys.Sk.Between(Guid.FirstVersion7(from), Guid.LastVersion7(to))` takes both milliseconds whole, `Ge(Guid.FirstVersion7(from))` starts at `from`, and `Lt(Guid.FirstVersion7(to))` stops before `to`. What is below a millisecond is dropped and the offset does not count, as in `Guid.CreateVersion7`, and an instant before the Unix epoch throws `ArgumentOutOfRangeException`, as there. They allocate nothing. There is no type of a time-ordered id: a ULID is a `string` and sorts the same way.

A condition of constant text or of a plain string allocates nothing. One that formats a value costs its strings, as `BeginsWith(value)` does, because a key condition holds text.

`ItemsAsync` and `PagesAsync` follow pages, filtered empty ones included, and can be enumerated once. `PageAsync(ct)` is the first page without a `default` in front of the token, and its token has no default, so `PageAsync()` stays the overload with the position. `SortKeyCondition` offers only what a key
condition allows: `Eq`, `Lt`, `Le`, `Gt`, `Ge`, `Between`, `BeginsWith`. `CountAsync` is `Select: COUNT` summed over pages, and throws
`InvalidOperationException` with a projection. `Parallel(segments)` returns `ParallelScanCommand<T>`, whose only terminal is `ItemsAsync`, because a merged
page has no single key; concurrency is the segment count, and paging across requests goes through `Segment(n, of: m)`.

A typed `Query` or `Scan` on an entity with a discriminator filters on it: `#_type = :t`, visible in `ToRequestJson()`. It costs no capacity, because a
filter runs after the read, and `ScannedCount` can then exceed `Count`. On a query of a table or index keyed by the discriminator the key condition
does that job, because DynamoDB refuses a filter on a key.

`Page<T>` has `Items`, `Count`, `ScannedCount`, `ConsumedCapacity`, `HasMore` and `Next`. `ItemsAsync` drops capacity; a caller who wants it pages.
### Items that do not fit

```csharp
var unreadable = new List<Item?>();
await foreach (Order order in orders.Query(pk).OnMappingFailure(failure => unreadable.Add(failure.Key)).ItemsAsync(ct)) { }
```

An item that the type cannot read ends the read with `DynamoDbMappingException`, and that stays the default: such an item is a bug of the model or of a writer, and a read that drops items would hide it. `OnMappingFailure(Action<DynamoDbMappingException>)` on `QueryCommand<T>`, `ScanCommand<T>`, `FanOutQueryCommand<T>` and the six mixed commands skips it and hands the exception to the callback, with `EntityType`, `AttributeName` and `Key`, the key attributes of the table as stored, also on an index. It is a callback and not a result type, because a result type would change the element type of `ItemsAsync` and every terminal behind it, and the exception already carries what identifies the item. Nothing logs it, so the key goes where the callback puts it. `OnMappingFailure(static _ => { })` skips and counts only. A second call replaces the first. It covers every terminal that reads items, `ItemsAsync`, `ItemAsync`, `PageAsync` and `PagesAsync`, and those of a `Select` after it, whose response holds no key, so `Key` is null there. A `Parallel` scan takes it from the scan before it.

Only `DynamoDbMappingException` is skipped: an item of another type where no filter kept it out, a value the converter cannot read, a missing `required` attribute, a stored key that does not fit its template, and on a fan-out an item without the keys the merge orders by. The response is read as before; on a failure the read goes on from the copy of the reader it keeps for `Key` anyway, past the item, so a page without a failure pays nothing, and a read without the clause runs the code it ran before, with one null check per page.

The callback runs once per skipped item, after the call of its page and before the items of that page are given out. It is outside the call, so what it throws neither looks like a response that did not parse nor is retried: it ends the read as it is. A parallel scan and a fan-out read pages on several threads at once, so there it can run concurrently. An item counts in `dynasharp.client.operation.skipped_items` (`{item}`, tags `db.operation.name`, `db.collection.name`, `server.address`) once the callback has returned for it.

What a skipped item does to the rest: it is not in `Page<T>.Items`, and `Page<T>.Count` stays the count of DynamoDB, so `Count - Items.Count` is what a page skipped; a page of a fan-out counts what it gives, so there it is in neither. `Limit` counts it, because DynamoDB read it, so a page can hold fewer items. `Take(n)` counts what it gets and reads on, `ItemAsync` gives the first item that can be read, and `CountAsync` reads no item and counts it. `Next` of a page is `LastEvaluatedKey`, past the item, so it is not read again. A fan-out continues after the last item it gave from each partition, so a skipped item behind that comes up again on the next page, and the callback has it again.

### One attribute

```csharp
SelectResult<decimal> total = await orders.Get(key).Consistent().Select(Order.Attrs.Total).SendAsync(ct);
decimal value = total.Found ? total.Value : 0m;                                                      // no item is no error
decimal orZero = total.GetValueOrDefault(0m);                                                       // the same, as Nullable<T> says it
SelectResult<string?> email = await customers.Get(Customer.Keys.Of("alice")).Select(Customer.Attrs.Email);    // null for an item without it

await foreach (decimal amount in orders.Query(pk).Filter(Order.Attrs.Status.Eq(OrderStatus.Open)).Limit(25).Select(Order.Attrs.Total).ValuesAsync(ct)) { }
SelectResult<decimal> newest = await orders.Query(pk).Descending().Limit(1).Select(Order.Attrs.Total).ValueAsync(ct);
Page<decimal> totals = await orders.Scan().Select(Order.Attrs.Total).PageAsync(after, ct);
```

`Select(handle)` on `GetCommand<T>`, `QueryCommand<T>` and `ScanCommand<T>` projects one attribute and reads its value without the entity, so a `required` member is not in the way, which is the difference to `Project`. It saves bandwidth, not capacity. Like `Return(attr)` it ends the
chain: the commands it returns have terminals and no clause, so a `Project` after it and a second `Select` do not compile, and `Filter`, `Limit`, `Descending`, `Consistent` and the others come before it. That is a smaller surface than repeating eight clauses on three types, and each of them means there what it means here.
A `Project` before it throws `InvalidOperationException`, because it would ask for what is not read. It is not on a fan-out, a mixed read or `BatchGet`, and more than one handle is a later step.

The value follows the handle. `TValue` is the type of the property, so `Order.Attrs.Total` gives a `decimal`, and `Customer.Attrs.Email` of a `string?` a `string?`. A missing attribute gives null for a nullable handle and an empty set for a set, because DynamoDB has none and a read of the entity gives that too, and throws
`DynamoDbMappingException` for any other, never a default: a read of the entity leaves a property that is not `required` at its initial value, so an `int` without its attribute is 0 there and throws here. A `[Version]` does the same, with a message that says that the item was written without one and points at `VersionMissing.Allow`, and `DYNA209` does not report it: every write of the library stores the version but an `Update` with `IgnoreVersion()` that creates the item. A version with `VersionMissing.Allow` gives 0, because that is what such an item means, see section 8. That is about an item that exists: no item is `Found == false`, also there. A stored `{"NULL":true}` reads as null on a nullable handle and throws on another, and a value the converter cannot read throws, naming the attribute. A value the converter counts as missing, section 4, reads as missing: null for a nullable handle, also where a read of the entity keeps an initializer, what the converter answers in `TryReadMissing`, and otherwise a throw that `DYNA209` reports. `Return(attr)` throws for it, as it does for a missing attribute, because it never gives a default.

`SelectResult<TValue>` is what `SendAsync` on a get and `ValueAsync` on a query give. `Found` says that there is an item and `Value` holds the attribute, and it has the shape of `GetResult<T>` but for that one word: `GetResult<T>.Found` says that `Item` is not null, which an attribute cannot promise, and `Value` throws `InvalidOperationException` for no item, because a 0 would read like one. `GetValueOrDefault()` and `GetValueOrDefault(defaultValue)` are the names `Nullable<T>` has for what does not throw: the value when found, else the default of the type or the fallback. An item whose nullable attribute is null gives that null, not the fallback, because the item exists. `DYNA227`, at Info, reports `Value` in a member that reads no `Found` of any `SelectResult`, a property pattern included: by member and not by flow, so it reports no code that checks somewhere, and it stays Info because code that knows the item exists is correct.
So a nullable handle tells no item, an item without the attribute and a value apart, and a handle that cannot be null has no item and a value, also for an `int`. A scan has no `ValueAsync`, as it has no `ItemAsync` anywhere.

A read of a table with several entities keeps its behaviour, and `ToRequestJson()` shows how. A `Get` projects the discriminator beside the attribute, and an item of another type throws `DynamoDbMappingException` as `orders.Get(key).ItemAsync()` does, in the same words. A `Query` and a `Scan` keep their filter on the discriminator, which keeps other items away, so nothing but the
attribute comes back. A query keyed by the discriminator has no such filter, so it projects the discriminator and checks each item. Without a key in the response, `DynamoDbMappingException.Key` of a `Query` or `Scan` is null.

The handle decides what can be read, and it goes through the converter of the property, with `[DynamoDbConverter]` and `[OmitEmpty]` as they are: a stored `""` reads as `""`, and an attribute that a write left out as null. A nested path `Order.Attrs.ShipTo.At(Address.Attrs.City)` reads through the maps, and its last handle decides: a `City` that cannot be null
throws for an item without `ShipTo`, even if `ShipTo` is nullable. A path with a list index throws `ArgumentException`, because DynamoDB returns a shortened list, in which its position is not known. A `[KeyOnly]` property has no attribute and no handle, so it does not compile. A stored input of a key of the table has both, but an item an `Update` created holds only the key: a read of the entity restores the
input from the key, `Select` does not, and throws, which `DYNA209` reports. An update with `StoreKeyInputs()` stores it. Reading through the key would take the parser of the template at run time, which only the generated read has, for a value the caller usually holds: it is part of the key.

`ContinuationToken` is a position: `default` is the start, `Parse` and `TryParse` take what `ToString()` gave, and null or `""` give the
default, so the first request of a client needs no check, and a loop on `Next.ToString()` checks `HasMore`. The text is safe in a URL and is encoded, not signed or encrypted: it carries the key
values of the last item, as `LastEvaluatedKey` does. It starts with the version of its format, on a page and a fan-out alike, so `Parse` tells a token of another format, older or newer, from one it can read: that is a `FormatException` that says so, quotes nothing, and asks to start the read again. `TryParse` is false for it. There is no limit on the length: a token of a page is as long as a key, and one of a fan-out grows with its partitions. `ToKey()` and `FromKey(key)` convert to and from a key of the request API.

Signed tokens are opt-in, for a web API whose clients must not choose their position: `new DynamoDbClientOptions { TokenSigner = new ContinuationTokenSigner(current, previous) }`. A key has at least `ContinuationTokenSigner.MinKeyLength` (32) bytes and is copied; the first key signs, every key is tried in order to check, with `CryptographicOperations.FixedTimeEquals`, which is how a key rotates. The signer lives in the options, not in a call or a table, so all reads of a client sign, every instance of a service with the same keys checks what another one handed out, and a client without one keeps plain tokens as they were.

| | |
|---|---|
| Format | Version 2: `Base64Url([2][key JSON][HMAC-SHA256, 32 bytes])`. Version 1 stays `[1][key JSON]` |
| Signed message | `[2][key JSON][length of the key, 4 bytes big-endian]DynaSharp.ContinuationToken\0{"TableName":"…"[,"IndexName":"…"]\0<key condition>[\0<partitions>]` |
| Bound | Table, index, the key condition with names and values in place of the placeholders (`#"pk" = :{"S":"a"} AND begins_with(#"sk", :{"S":"x"})`), so another partition or sort key condition does not take the token, and for a fan-out the list of partition keys. A scan binds table and index |
| Not bound | Filter, projection, `Limit`, direction and consistency: a token goes on with another of these on the same query, as a plain one does. A fan-out still checks its direction itself. No expiry |
| Checked | In `PageAsync` of `Query`, `Scan`, their `Select` and mixed forms, and the fan-out, before a request goes out. `Parse` checks only the shape of version 2, because it has no keys |
| Rejected | With a signer: a signature that does not hold (altered, made up, another query, a key no longer held), and a plain token that came through `Parse`/`TryParse`. Without a signer: a signed token. All are `FormatException`, quote nothing of the token, and ask to start the read again |
| Accepted unsigned | A plain token this process made, from `FromKey` or kept from a client without a signer, because it is no input of a client: the bridge from the request API works on a client that signs |

A patch release reads and writes the token formats of the release before it; a release that changes one gives it a new version and says so in its notes, and `docs/compatibility.md` says what that means for a rolling deployment. A signed token shows its key values as a plain one does: it is signed, not encrypted. Inside the key condition a chosen position is only a place among items the caller may read anyway, DynamoDB rejects a start key outside it, and a filter applies from any position, so signing matters where the key condition is not what keeps callers apart. `ContinuationToken` stays a value: two tokens are equal if their text is, wherever they came from.

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
| `ReturnOld()` | Put, Update, Delete: `ALL_OLD` in `WriteResult<T>.Item` |
| `ReturnNew()` | Update: `ALL_NEW` in `Item` |
| `Return(attr)` | Update: `UPDATED_NEW` of one attribute. Returns `ReturnCommand<,>`, which only sends, and `SendAsync` gives the value. A missing attribute throws `DynamoDbMappingException`, never a default |
| `ReturnOldOnFailure()` | Put, Update, Delete and transaction steps: the item on `ConditionalCheckFailedException.Item`, or through `ItemFor` in a transaction |
| `StoreKeyInputs()` | Update of an entity and `UpdateStep`: stores the inputs of the key of the table as attributes, section 8. On a `Table<Item>` it does not compile, and on a step of one it throws `InvalidOperationException` |

`WriteResult<T>` has `Item`, `HasItem`, `ConsumedCapacity`, `ItemCollectionMetrics` and `Version`.

A retry repeats a write. `Add`, `list_append` and arithmetic in `SET` are applied twice if a response was lost. `MaxAttempts(1)` makes them at most once, because a lost response then
reports a failure after the write was applied, and a condition makes them exact. After a lost response a conditional write judges what its first attempt wrote, which the `SendAsync` docs of each command spell out.

`Set` by handle takes a nullable value, and null is a `Remove`, for every attribute: `string?` for a reference type, and `Guid?` through an overload for a value type, which is a `Set` when it has a
value and costs what that costs. On a key input the rules of section 8 are those of the `Set` or `Remove` it turns into. `SetIfNotNull(handle, value)`, for reference and value types and by handle only, is
that `Set` when there is a value, with the rules of section 8, and with null it adds nothing, so the attribute stays and a key input counts as untouched. `SetIfNotExists`, `Add` and `Delete` by handle reject null: a value type
does not compile, and a null of a reference type is a nullable warning and an `ArgumentNullException`. `Set`, `Add` and `Delete` by raw path and by handle both exist; a raw path is the escape
hatch for attributes outside the entity, and the checks of section 8 do not apply to it.

With `[OmitEmpty]` an empty string or collection is written like null: `Put`, a batch, a transaction, a `With` by handle and `Item.From` leave it out, `Set` by handle is a `Remove`, `SetIfNotNull` adds nothing, and `SetIfNotExists` by handle rejects it with an
`ArgumentException`, the base of what null gets. A stored `""` or `[]` still reads as it is, and a raw path writes what it is given. A missing attribute reads as a missing attribute always does: null for a nullable property, an empty set for a set, and otherwise the initial value, so a list that starts filled comes back filled. So `[]` does not read back as `[]`, null and `[]` can mean different things, and `[OmitEmpty(false)]` keeps the empty one. That is wider than the rule of the AWS SDK, which leaves out an empty string and keeps an empty list of documents, an empty dictionary and an empty `byte[]`. A `required` property that is neither nullable nor a set, and a constructor parameter of that kind without a default, could not be read back: `DYNA030` on the property, and a type or the assembly default passes over it, as over a key input. `Select` of a property that is not nullable throws for a missing attribute, `DYNA209`, unless it is a set.

The handle says so in its converter, `Attr<string, NullableReferenceHandleConverter<string, OmitEmptyConverter<StringConverter>>>` for a `string?`, `OmitEmptyCollectionConverter<T, TElement, TConverter>` for a collection, and `OmitEmptySetConverter<T, TElement, TConverter>` for one that `Add` takes, so a nested path keeps it, and a type without the attribute generates what it did.

`IDynamoDbConverter<T>.IsOmitted` is the same question, by value, for a converter of your own, such as one that stores a list as a set, which DynamoDB refuses to hold empty. All the writes above ask it, and a value it counts as null goes the way an empty string goes with `[OmitEmpty]`: `Put`, `Item.From`, a batch, a transaction and a nested document leave it out, and so do the paths by handle. The generated writer holds the call only for a `[DynamoDbConverter]`, on the property or on its type, or a `[DynamoDbDefaultConverter]`, that implements `IsOmitted`, so a built-in converter and any other pays nothing. A property that feeds a key is always written, and an element of a list or a map as well. Beside `[OmitEmpty]`, what either counts as null goes. A read does not ask: the attribute is missing, so the property keeps its initial value, and a `required` one throws.

## 8. What a write of an entity enforces

**`[Version]`.** The property holds the version the caller last read, and 0 means that no item exists. Every enforced write is visible in `ToRequestJson()`.

| Write | Rule |
|---|---|
| `Put(entity)` | Version 0 adds `attribute_not_exists(<partition key>)`, on the key and not on the version, so it fails on any item under the key, also on one without a version attribute. `[Version(Missing = VersionMissing.Allow)]` is the answer for a table that keeps such items, see below; for one item, `Put(entity with a version above 0).IgnoreVersion()` takes it over, and at 0 the item would read as 0 and fail the next write. Any other `<version> = :old`, and the item is stored with `old + 1`. The entity is not mutated: `WriteResult<T>.Version` returns the stored version |
| `Update(key).IfVersion(n).IfNotExists()` | Legal at version 0, in either order, and the clause appears once. Above 0 the terminal throws `InvalidOperationException`, also on `UpdateStep` and `ConditionStep`, for the reason of the next row |
| `Put(entity).IfNotExists()` | Legal at version 0, and the clause appears once. Above 0 the terminal throws `InvalidOperationException`, also on a transaction step and on `Unique`, because no item can lack its key and hold a version, unless `.IgnoreVersion()` was called. `DYNA206` reports that where a build can see the version |
| `Put(entity).IfExists()`, `Update(key).IfVersion(0).IfExists()`, `Delete(entity).IfExists()` | Legal above 0. At version 0 the terminal throws `InvalidOperationException` naming the entity, in either order, also on a transaction step and `ConditionStep`, because version 0 means that no item has the key, unless `.IgnoreVersion()` was called, which lifts this where it lifts the row above. Both conditions would otherwise go out and fail at DynamoDB every time |
| `Update(key)` | `.IfVersion(n)` adds the condition, which at 0 is `attribute_not_exists(<partition key>)`, and `SET <version> = :new` with `n + 1`. Without it the terminal throws `InvalidOperationException` naming the entity, unless `.IgnoreVersion()` was called. There is no `Update(entity)` |
| `Delete(key)` | The same rule, with the condition alone |
| `Delete(entity)` | Takes the version from the entity, as `IfVersion(entity.Version)`, so at 0 it fails on any item under the key. `IfVersion(n)` replaces it, and `IgnoreVersion()` opts out |
| Transaction steps | The same rules: `IfVersion` on `UpdateStep` and `ConditionStep`, `IgnoreVersion` on all three steps |
| `BatchWrite` | A batch cannot carry a condition, so a put or delete of a versioned entity throws at the terminal unless `.IgnoreVersion()` was called on the batch |
| `Put(entity).IgnoreVersion()` | Stores the entity as it is, with no condition and no bump, for imports and migrations |

**`VersionMissing`.** `[Version(Missing = VersionMissing.Allow)]` is for a table that keeps items without the attribute, which read as version 0, or items that hold 0, which the first `SaveAsync` of the AWS SDK stores and which read the same. `Reject`, the default, is what the table above says. `DynamoDbEntityInfo.VersionMissing` carries it.

| With `Allow` | Rule |
|---|---|
| Every version 0: `Put`, `IfVersion(0)`, `Delete(entity)`, transaction steps | `(attribute_not_exists(<version attribute>) OR <version attribute> = :v)` with 0 as the value, instead of the partition key, because both kinds of item read as 0. So the first write replaces such an item and stores version 1, a second write at 0 fails, and a write at 0 no longer proves that the key was free |
| `IfNotExists()` at 0 | `attribute_not_exists(<partition key>)` alone and once, in either order and on `Unique` too, because the caller asked for that |
| `IfExists()` at 0 | No longer throws, because an existing item that reads as 0 is possible, and renders `attribute_exists(<partition key>) AND (attribute_not_exists(<version attribute>) OR <version attribute> = :v)`, in either order |
| A read | Gives 0 for an item without the attribute, also for a `required` version and a parameter of a constructor without a default, because a read that threw could not reach the item |
| `Select(Attrs.Version)` | Gives 0 as well: the handle has a generated converter that answers `TryReadMissing`, and is still a number for `Add`. No item is `Found == false`, and `Value` throws |

The condition of `IfVersion(0)` waits for the terminal, because `IfNotExists` can still replace it, so it stands after the other conditions of the chain. A version above 0 behaves as without `Allow`.

`IgnoreVersion()` after `IfVersion(n)` throws, because the condition cannot be taken out of the expressions; `IfVersion(n)` after `IgnoreVersion()` wins.
A failed condition is the ordinary `ConditionalCheckFailedException`. No retry and no read happens implicitly. The check runs in the terminal and costs an
entity without a version nothing. `WriteAttributes` does not bump, so `Item.From` stays a plain copy.

**Template inputs.** A typed `Set` or `Remove` by handle of a property that feeds a key template must leave that key consistent. The check runs at
the terminal, and raw paths are not checked.

| The update | Result |
|---|---|
| Knows all inputs of an index template, by setting them or from the key it was given | Writes the derived attribute in the same `SET` |
| Lacks an input of a template that it could set | `InvalidOperationException` naming the template, what is missing and the `Set` to add, `.Set(WorkItem.Attrs.CreatedAt, value)` with the value the item holds, because an update expression can copy an attribute and cannot format a key of several values or with literals from what an item holds. `DYNA226` reports it in a build |
| Lacks an input that it cannot set | `InvalidOperationException` naming the template and what is missing, and pointing at a `Put` of the changed entity or a raw path |
| Touches an input of a key of the table | `InvalidOperationException`: DynamoDB cannot update a key |
| Changes an input by `Add` or `SetIfNotExists` | `InvalidOperationException`: the value is not known here |
| Changes by handle what a computed input is derived from: `Set`, `SetIfNotNull` with a value, `Remove`, `Add`, `Delete` or `SetIfNotExists`, also of a path inside it such as `Attrs.Message.At(Msg.Attrs.Arrival)` | `InvalidOperationException` naming the property, the computed input and the template, and pointing at a `Put` of the changed entity or a raw path, because no update can run a getter. In a key of the table it is the row above that |
| Writes by handle a computed input with `[ComputedFrom]` that has an attribute of its own | The same, because the key would follow the update and not the getter, and the next `Put` would move it |
| Removes an input of a sparse index | Removes the derived attributes, unless another index still holds the item |
| Removes an input of an index that is not sparse | `InvalidOperationException` |
| Sets one key of a sparse index and does not produce the other | `InvalidOperationException` naming the other key, because an update does not read the item and cannot tell whether it holds that key, and an item without it would stay out of the index without an error, and the `Set` that produces it when a handle can. `DYNA226` reports it in a build |
| Sets one key of an index that is not sparse and does not produce the other | Writes the key it can compute and throws nothing, so `.Set(Attrs.At, at)` renders `SET At = :v0, gsk = :v1` with no `gpk`. An item that exists keeps its other key, and an item the update creates stays out of the index. An update that can create an item sets every input of the index, ends in `.IfExists()`, or is a `Put` |
| `StoreKeyInputs()` | Ends the `SET` with `#attr = if_not_exists(#attr, :v)` per input of a key of the table with an attribute of its own, after the actions and the derived keys and before keys of literals and the discriminator |

An update does not read: an update expression can copy an attribute of the item, `SET a = b`, and cannot format a key from attributes, so every input of a key it changes comes from the caller, also one that stays as it is, and a key is never written from some of its inputs. The copy is used where the key is the stored text of the attribute byte for byte, for the other key of a sparse index, the one case that would throw otherwise: a template of one bare placeholder, no literal, over a property with an attribute, written by `StringConverter` (also `{X:raw}`, which is the same text alone), `GuidConverter` (lowercase `D` in both), `DateOnlyConverter` (`yyyy-MM-dd` in both), `DateTimeConverter` or `DateTimeOffsetConverter` under `{X}` or `{X:ticks}` (the round-trip form in UTC in both), or the generated converter of an enum by name. Nothing else qualifies: a number is stored as `N` and DynamoDB rejects it for a string key ("Type mismatch for Index Key"), `d<w>` and `n<i>.<f>` pad or sign it, a timestamp format other than `ticks` cuts it off, a literal or a second placeholder is other text, and a converter of a property or of the assembly, such as `SdkDateTimeConverter`, writes what it likes. An index that is not sparse is not copied into, because an update that creates an item would then fail where it writes the key it can compute today. There is no way to name such a value for the key alone, because it would be a second spelling of `Set` that writes the key and leaves the attribute as it was, so that the two could disagree; `Set` writes both. A design that keeps an input that never changes in the key of the table needs none of that: an input of the table key is known from the key the update was given, so an index key that mixes it with a property that changes follows that property, and an index key made only of such inputs counts as
produced: it is written in the same `SET` when the update writes the other key of its index. A generated key gives the value. A key given as strings goes through the generated parser, which gives it back
unless the format of the key loses what the index key wants, such as a timestamp a table key cuts off at the day. A sparse index that needs such a value then throws, an index that is not sparse keeps
the key a `Put` wrote, and an entity like that holds its generated key per update, for one box. The read happens only for an update that writes such a key, so any other costs nothing.
An index with only such keys is not written by an update that does not touch it: such an item enters it by `Put`.

Typed updates write no input of a key of the table unless `StoreKeyInputs()` asks, so an item an update created holds the key and not the attributes. With the clause it holds what a `Put` writes: `:v` is the value as the converter of the property writes it, never the text of the key, so `EpochSecondsConverter` gives a number and a binary converter a binary, and `IsOmitted` is not asked, because a key input is always written. The value comes from the key the update was given: a generated key holds it, and a key given as strings goes through the generated parser. A plain key, a template of one placeholder over the attribute of its property and a `[KeyOnly]` input store nothing, so an entity with nothing else gets a body that is the same with and without the clause. A value that no key of the table holds whole, such as a timestamp in `{At:yyyy-MM-dd}`, comes from a generated key alone: with strings the terminal throws `InvalidOperationException` naming the property and the template, also when an index repeats the format, which is enough for its key and not for the attribute. A key that does not fit its template throws too. An action of the update on the attribute by a raw path takes precedence, as for the discriminator, and by a handle it is the row above. An item that exists keeps what it has, so the clause goes with `IfExists()`, `IfNotExists()`, `IfVersion` and `IgnoreVersion`. It costs an `AttributeValue` per input, and a value of a generated key its box. An update without it pays nothing: the state keeps a generated key in a holder it fills again, for an entity that may need it.

A computed property is an input like any other to `Put`, `Item.From`, a batch and a transaction put, which run the getter, to the builders in `Keys`, and to a sparse index, which a nullable one gates. A read leaves it alone. Only an update differs, because it has no instance: an input with `[ComputedFrom]` has no value there, so a template with one is a template whose input it cannot set, unless it touches nothing of it. `SetIfNotNull` of null adds nothing and counts as untouched. A raw path is not checked, so `.Set("message", value)` goes out and the index keeps the key it had. The list has properties of the same type with an attribute of their own, one level only: no computed property, no `[DynamoDbIgnore]`, no `[KeyOnly]`, and no `[Version]`, which an update moves without a handle. One of them may feed another key.

**Literal keys.** A key that holds only literals counts as produced, on every index. When an update writes one key of an index, a sibling made only of literals is written in the same `SET`, so
`PartitionKey = "ALL"` can be entered by `Update`. A `Remove` that takes the item out of a sparse index removes it too. An update that does not touch such an index adds nothing to it, unless all its
keys are literal, or are keys of the table, which every item has. Then no property can bring an item in, so every typed update writes the literals with `if_not_exists`, as it does the discriminator,
and an update at a key held by another type does not overwrite them. An action of the update on the attribute itself takes precedence. A local index with a sort key of literals is one of these.

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

Struct conditions cover comparisons, existence, `BeginsWith`, `Contains` and `Between`, compose with `And`, `Or`, `Negate` and allocate nothing. A chain of one operator renders as one group, `(a OR b OR c)`, and conditions
added one by one as `(a) AND (b) AND (c)`. `BeginsWith` by handle is an extension method of `AttrExtensions`, because the type
of the attribute decides: a number, a boolean and a timestamp stored as a number do not compile (`CS0619`), as DynamoDB finds nothing there, an enum, a `Guid` or a date, which are stored as strings, do, and
a binary attribute takes a binary prefix, with a string as `CS0619`. A `[Flags]` enum is stored as a number, and its handle has a generated converter that no overload can name, so it compiles and `DYNA216` reports it.
`Contains` by handle is an extension method of `AttrExtensions` too, with one overload per handle of a set, a list and a string that the generator emits, also nullable and with `[OmitEmpty]`: a set takes an element of its type,
written by `SetElementValueConverter<T, TElement>` as the set stores it, a list an element through the converter of its elements, and a string a substring. Anything else has no overload (`CS1929`). `Attr` has
`Contains(AttributeValue)` and `Contains<TNumber>(TNumber)`. There is no `NotContains`: `Negate()` renders `NOT (contains(...))`, and on an attribute that does not exist `contains` is false, so the negation holds there.
In an interpolated expression `DYNA216` reports `begins_with` and `contains` on a number. Interpolation covers the
rest of the grammar (`size`, `attribute_type`, `list_append`, arithmetic in `SET`). In a hole, an `Attr` stands for its name, and a value,
an `AttributeValue`, a number or `handle.Value(x)`, for a value. A string in a hole is a value, so a name needs `Attr` (`DYNA210`), and an expression that
names no attribute throws `ArgumentException`. A bare name in the text goes to DynamoDB as it is, so a reserved word there is rejected (`DYNA220`), and one that
the type of the command does not have is `DYNA213`.

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

A batch gives up after `MaxStalledRounds`, on a throttle on the last attempt of a request, and on any other failure after at least one request went through; the last two have the failure as it came as `InnerException`. A failure of the first request, where nothing was applied, stays raw. Cancellation by the caller stays an `OperationCanceledException` and does not report progress.

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
await foreach (Order order in orders.Query(shardKeys, SortKeyCondition.Between(from, to)).Descending().ItemsAsync(ct)) { }
Page<Order> page = await orders.Query(shardKeys).PageAsync(pageSize: 50, after, ct);
```

`Query(IEnumerable<AttributeValue>, SortKeyCondition)`, `Query(IEnumerable<string>, SortKeyCondition)`, `Query(ReadOnlySpan<string>, SortKeyCondition)` and, by extension method, `Query(IEnumerable<PartitionKey<T>>, SortKeyCondition)` on
`Table<T>` and `Index<T>` return a `FanOutQueryCommand<T>`: one query per partition, merged. `Table<Item>` and `Index<Item>` take the keys of any entity. The `IEnumerable<string>` form has
`[OverloadResolutionPriority(-1)]`, so it never makes a call ambiguous that binds elsewhere; `Query(default)` and `Query([])` do not compile. Clauses are `Filter`, `Project`, `Limit` (items per partition page), `Descending`, `Consistent`,
`MaxAttempts`, `ReturnCapacity` and `MaxConcurrency(n)`, default 8.

Items merge in DynamoDB's sort key order: bytes for S and B, numeric for N, ties by the position of the partition key in the input. A partition is read
only when the merge needs it, so an early `break` does not read the rest, and at most one page per partition is buffered. A repeated or empty
partition key throws `ArgumentException`. An empty list reads nothing.

`PageAsync(pageSize, after, ct)` returns a `Page<T>` whose `Next` is a composite token with one entry per partition: its start key and the key of its next
item as a hint, or done, and the direction of the read. The hint spares a read of a partition the merge does not need yet, and never reaches a request. Tokens parse like plain ones.
A token of a different partition list fails a count check, one of the other direction is rejected, and all that is wrong with its shape throws `FormatException`, as `Parse` does, before a request goes out, so a web handler has one
thing to catch locally. `ValidationException` remains for a start key only DynamoDB can judge. A client with `TokenSigner` signs a fan-out token over its partition keys too, see section 6. If the table changes between pages, an item written before a hinted key
can come later than its place. `Page.ConsumedCapacity` adds every response of that `PageAsync`.

It is not a snapshot, and there is no `PagesAsync` and no `Parallel`.

## 14. Item and AttributeValue

`Item` is the flat item, a sealed mutable `IReadOnlyDictionary<string, AttributeValue>` that compares by content, and implements `IDynamoDbDocument<Item>`, which
is what makes `Table<Item>` the same engine. Build it with the indexer, and `Remove(name)` takes an attribute out and says whether it was there. `item["x"]` throws `KeyNotFoundException` naming the attribute. `GetString`, `GetNumber<T>`,
`GetBool`, `GetBinary`, `GetMap`, `GetList`, `GetStringSet`, `GetNumberSet` and `GetBinarySet` return null for a missing attribute and for DynamoDB NULL, and throw
`InvalidOperationException` for another type; a number that does not fit throws `FormatException` or `OverflowException`. These messages name the attribute, never the value. `ToString()`
returns DynamoDB JSON, which does contain values. `Item.From(value)` and `item.To<T>()` convert to and from a mapped type. `item.Is<T>()` says whether the item is of an entity: its discriminator attribute, `_type` or
the `AttributeName` of the type, holds a string equal to the type's discriminator, which is the rule of a mixed read, from the same `MixedSchema`. A missing attribute, NULL or another kind of value is false, the type argument is
constrained to `IDynamoDbDiscriminated<T>` as `Of<...>()` is, a hand-written type that claims it without a discriminator throws `ArgumentException`, and it allocates nothing. It reads no other attribute, so an item that is a `T` can
still fail `To<T>()`. There is no `TryTo<T>(out T)`: a `Try` method that throws a mapping failure of the right type breaks the pattern, and one that returns false hides it, so the caller writes `Is<T>()` and then `To<T>()`. An `Item` inside an `AttributeValue`, which is what `GetMap` returns, is
read-only, which only shows at run time: the type is the same, because a type of its own would repeat every getter. `new Item(other)` makes an editable copy. Read items have no attribute order, hand-built ones keep insertion order, and `Equals` is the only
sanctioned comparison. Its hash follows its content, so it does not belong in a `HashSet`.

`AttributeValue` is a struct with implicit conversions from `string`, `bool`, `int`, `long`, `double`, `decimal`, `byte[]` and `char`, which is a one-character
string. `Null` and the `FromXxx` factories cover the rest, `AsXxx` reads a value, and numbers stay text until read. `ToString()` is DynamoDB JSON.

DynamoDB holds 38 significant digits and a `decimal` 28 to 29, with at most 28 after the point. A read of a `decimal` returns the exact value or throws `OverflowException`, on every path: `NumberConverter<decimal>`, `DynamoDbReader.GetNumber<decimal>`, `Item.GetNumber<decimal>` and `AttributeValue.AsNumber<decimal>`. It never rounds, and `1E-29` is no 0. Trailing zeros and exponents that keep the value are fine, such as `1.10`, `1E+2` and `0E+5`. For a number that needs all 38 digits, `AsNumber()` and `DynamoDbReader.GetNumberText` give the text.

A `double`, `float` or `Half` rounds to the nearest value it holds, and a number below its range reads as 0: that is a binary float, on all those paths. A number that rounds to infinity throws `OverflowException` there, and so does the text `NaN`.

A string with a lone surrogate is no text, and `DynamoDbWriter` throws `ArgumentException` for it, in an attribute value, a string set and an attribute name, as expression text and keys do, rather than store U+FFFD. `ToString()` of a value that holds one shows U+FFFD instead, because it is the text someone reads while finding out what went wrong.

## 15. Errors and escape hatches

`DynamoDbException` carries `ErrorCode`, the wire code without namespace, `StatusCode`, `RequestId`, `OperationName` and `TableName`. A code the library
knows has its class, in `DynaSharp`, and any other is a plain `DynamoDbException`. `DynamoDbMappingException` and `CredentialsUnavailableException` are there too, so a catch clause needs no other import.

`IsTransient`, get-only on `DynamoDbException` and named after `DbException.IsTransient`, is true when the same call can succeed later without any change: everything `FailureClassifier.Classify` repeats (throttling and capacity, `TransactionInProgressException`, any 500, 502, 503 or 504, `RequestTimeoutException`, `ReplicatedWriteConflictException`), so the two cannot disagree, and `TransactionConflictException` and a cancelled transaction whose failed actions are all `TransactionConflict` or `Throttled`, which the pipeline leaves to the caller. A batch that gave up is true without a cause, which means its rounds ran out, and otherwise as its `InnerException`. Everything else is false: validation, a missing or busy table or index, access, signature and token codes, a failed condition, a cancelled transaction with any other reason or none, size limits, and a code below 500 that the library does not know. The client has retried before the exception surfaces, so true says that its attempts ran out. A `TimeoutException`, `HttpRequestException`, `OperationCanceledException` or `CredentialsUnavailableException` is no `DynamoDbException` and keeps its type, so it has no `IsTransient`; what STS answers surfaces as the last.

| Failure | Where |
|---|---|
| A bad key of `Table<Item>`: empty string, empty binary, or a sort key on a table opened without one | `ArgumentException` at the call |
| A typed key built from bad input | `KeyFormatException`, an `ArgumentException` naming the placeholder and never the value. From a generated builder the message names the template as written, and for a delimiter the delimiters of the template, not which one the value holds: `The value contains a delimiter of the key template 'USER#{Id}' ('#'). (Parameter 'Id')` |
| A rejected request of a command | `ValidationException`, with `Expressions`, `ExpressionNames` (`#n0 = status`) and `ExpressionValueTypes` (`:v0` is `S`), and one line in `Message`: `Expression: #n0 = :v0, where #n0 = status, :v0 (S)`. Values are not attached. Inside a transaction or a batch get the entries have a prefix such as `TransactItems[2].` |
| An item that does not fit the type | `DynamoDbMappingException` with `EntityType`, `AttributeName` and `Key`, also for an item of another type; the discriminator value is named, because it is a type tag. A stored key that does not fit its template says so, with no value. `Key` holds the key attributes of the table for the item that failed, on `Get`, `Query`, `Scan`, fan-out, `BatchGet` and mixed reads, and neither the message nor telemetry show it. A query or scan with `OnMappingFailure` skips the item and hands the exception to the callback, section 6. `Select` on a `Query` or `Scan` brings no key back, so `Key` is null there |
| A failed condition | `ConditionalCheckFailedException`, with `Item`, the item the condition saw, when `ReturnOldOnFailure()` asked for it, and `Key`, whatever was asked. `Message` gains a line that names the condition the write sent, never a value: `The condition of this Update on Users did not hold: the version check on Revision.`, or the check that no item or that an item has this key, or the caller's own condition, all of them with `and DynamoDB does not say which` when there were several. A version that marks a new item, as on a plain `Put` of a fresh entity, adds that the check asks that no item has the key, and that the version last read or `IgnoreVersion()` writes over the stored item. `Key` is the key of the item as the request named it, with stored values such as `USER#alice`, read from the body that was sent on the failure path alone: `Key` of `Update` and `Delete`, the key attributes of `Item` of `Put`, `request.Key` of `UpdateItemAsync` and `DeleteItemAsync`. It is null on `PutItemAsync`, which has no key names, on `SendRawAsync`, in a transaction, where `ItemFor` is that, and on an exception that did not come from a response. `T.Keys.Pk.Parse(key.GetString("pk"))` gives the inputs of a key of a template back. A caller that holds the key gains nothing from it: it is for code that does not, such as a wrapper around a delegate. Neither the message, `ToString()`, logs nor telemetry show it |
| A transaction | Section 11. `ConditionalCheckFailed` and `ValidationError` do not pass on a retry, `TransactionConflict` and `Throttled` may. `Message` of a command gains a line that names each failed action by position, verb and table, with its code: `Action 2 of 2, Put on Users: ConditionalCheckFailed` |
| A wait for a table that runs out, or on `CreateTableCommand` does not see a declared index listed | `TableNotActiveException`, sealed, in `DynaSharp`, a `TimeoutException` with `TableName`, `LastStatus` (`TableStatus?`, null when the last look did not find the table, and then `InnerException` is the `ResourceNotFoundException`) and `PendingIndex` (`string?`, the index that was not active or, of those declared, not listed, which the message names too). A `catch (TimeoutException)` keeps working, and `AttemptTimeout` on a poll still gives the plain type |

Compile errors stand in for what would otherwise be a `ValidationException`, a mapping failure or an empty result: a key of another entity does not fit `Query`, `Index<T>` has no writes, `Put()` and `With` exist on
`Table<Item>` only, steps have their own clauses, `Return(attr)` and `Select(handle)` end the chain, `SortKeyCondition` has only legal operators, `Of<T>()` takes entities, `Parallel()`
has no `PageAsync`, and `BeginsWith` takes no handle of a number.

Escape hatches: `client.SendRawAsync("ListTables", """{"Limit":1}"""u8.ToArray(), ct)` for what nothing models, with the pipeline's signing, retries and
telemetry; raw paths and interpolated expressions on typed commands; `Item.From` and `To<T>`.

`DynamoDbStreamsClient` takes the same options and has `ListStreamsAsync`, `DescribeStreamAsync`, `GetShardIteratorAsync`, `GetRecordsAsync`, `ReadShardAsync` and `SendRawAsync`.
`ReadShardAsync(ReadShardRequest, ct)` returns `IAsyncEnumerable<ChangeRecord>` for one shard: `StreamArn`, `ShardId`, a required `ShardIteratorType` with `SequenceNumber` exactly for the two sequence positions
(`ArgumentException` otherwise, `ArgumentOutOfRangeException` for `Unknown`), `Limit` per `GetRecords`, and `EmptyPageDelay` (default 1 s, at least zero and below the 15 minutes of an iterator), waited on the client's
`TimeProvider` after an empty page of an open shard. Arguments are checked at the call, requests start with the first `MoveNextAsync`. An expired iterator is renewed with `AfterSequenceNumber` of the last record returned, or at
the request's position before the first record; an iterator that is expired at once surfaces `ExpiredIteratorException`. The sequence ends on a page without `NextShardIterator` or a `GetShardIterator` without an iterator.
`TrimmedDataAccessException` surfaces, because data was lost. The checkpoint is `StreamRecord.SequenceNumber` of each record; there is no checkpoint store, no lease and no lineage scheduler. Records are returned, not pages: a
page boundary means nothing to a caller, and the position is per record. `StreamRecord.NewImage` and `OldImage` are `Item`, so `To<T>()` reads an image as an entity and `Is<T>()` tells the entities of one table apart. The members of
the responses are nullable as the service model has them: botocore's `dynamodbstreams` marks no member of an output shape required but those of `KeySchemaElement`, and the API reference says `Required: No` for each, so a
response without one reads as null, as everywhere else in the library, and nothing throws for it. There is no consumer, by scope.

Credentials: `DefaultCredentialsProvider` is the chain of environment, web identity token file, shared files, container endpoint and instance metadata, in the order of botocore. A
source that applies but is broken fails instead of falling through. `EnvironmentCredentialsProvider`, `WebIdentityCredentialsProvider`, `ProfileCredentialsProvider`,
`ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider`, `AssumeRoleCredentialsProvider`, `SsoCredentialsProvider` and `StaticCredentialsProvider` are the single sources. `WebIdentityCredentialsProvider` takes its settings from
`AWS_WEB_IDENTITY_TOKEN_FILE`, `AWS_ROLE_ARN` and `AWS_ROLE_SESSION_NAME`, or as `new WebIdentityCredentialsProvider(tokenFile, roleArn, region, roleSessionName)`, or `new WebIdentityCredentialsProvider(tokenFile, roleArn, stsEndpoint, roleSessionName)`. A profile with
`web_identity_token_file` and `role_arn` (and `role_session_name`) gets the same provider from `ProfileCredentialsProvider`. The token goes to the regional STS endpoint of the client's region, and `AWS_STS_REGIONAL_ENDPOINTS=legacy` selects the global endpoint for the regions botocore keeps there. `AWS_ENDPOINT_URL_STS`, then `AWS_ENDPOINT_URL`, go ahead of both, in the order of botocore and with the rules of the row of `ServiceUrl`, and then no region is needed. `stsEndpoint` goes ahead of them all, by the same rules, an `ArgumentException` otherwise: explicit settings replace the environment, so that constructor reads no variable, whereas the variables still win over the region of the other one. A loopback endpoint goes past the default proxy. Its requests do not go through `HttpHandler`.

`EnvironmentCredentialsProvider` reads `AWS_CREDENTIAL_EXPIRATION` (ISO 8601) into `Expiration`, as botocore's `EnvProvider` does. With it the provider reads the environment again through the refresh cache and `TryInvalidate` drops the credentials and returns `true`; without it the credentials never expire, are read once, and `TryInvalidate` returns `false`. A time that is not one, or has passed, throws and names the variable.

`ProfileCredentialsProvider` runs the `credential_process` of a profile, in botocore's place between the static keys of the credentials file and those of the config file. The command line is split as botocore's `compat_shell_split` does (the rules of the C runtime on Windows, `shlex.split` elsewhere), the program starts without a shell, with its standard input closed, 20 seconds and 64 KB of output, and its output is parsed as `Version` 1. Credentials with an `Expiration` refresh through the cache of the other providers, and `TryInvalidate` runs the program again; static keys of a profile answer `false`. Messages name the profile and the exit code, never the output or the standard error.

`new AssumeRoleCredentialsProvider(sourceCredentials, roleArn, roleSessionName = null, options = null)` assumes a role with the credentials of any provider, through `AssumeRole` of STS: a form of the Query protocol signed with SigV4 (`SigV4Signer.SignForm`, service `sts`), with the endpoint rules of web identity (`AssumeRoleOptions.StsEndpoint`, `AWS_ENDPOINT_URL_STS`, `AWS_ENDPOINT_URL`, `AWS_STS_REGIONAL_ENDPOINTS=legacy`, the regional endpoint) and its retries. `AssumeRoleOptions` has `ExternalId`, `Duration` (15 minutes to 12 hours, whole seconds), `Region` and `StsEndpoint`, all `init`. The global endpoint signs for `us-east-1`, a configured endpoint for the region that is known or else `us-east-1`. It is an `IRefreshableCredentialsProvider` and refreshes through the cache. No MFA. A profile with `role_arn` and `source_profile` or `credential_source` builds one, in botocore's order and with its checks, see `ProfileCredentialsProvider`; the region of STS is that of the client, else `AWS_REGION`, `AWS_DEFAULT_REGION` and the `region` of the profile being resolved.

`new SsoCredentialsProvider(startUrl, ssoRegion, accountId, roleName, sessionName = null)` reads the token that `aws sso login` cached in `~/.aws/sso/cache/{sha1 of the session name, or of the start URL}.json` at every fetch and calls `GetRoleCredentials` of the portal (`GET /federation/credentials?role_name=&account_id=` with `x-amz-sso_bearer_token`, `AWS_ENDPOINT_URL_SSO`, `AWS_ENDPOINT_URL`). With a session, a token in its last 15 minutes is renewed through `CreateToken` of the OIDC service (`AWS_ENDPOINT_URL_SSO_OIDC`) and written back to the file, keeping its permissions; a failed renewal is logged and the cached token used while it lasts. A missing or expired token and HTTP 401 fail with a message that says to run `aws sso login`. `startUrl` is a `string`, because the cache file is named by its SHA-1 as written. A profile is SSO when it has `sso_account_id` or `sso_role_name`, in botocore's place after web identity, with an `sso_session` from a `[sso-session name]` section or the older keys in the profile, and the checks of botocore's `_load_sso_config`.

Out of the API on purpose: an interface on `Table<T>` or `DynamoDbClient`, `Save`, identity maps, lazy loading, any read before a write, `ToRequest()` on an item command, `==` on
attributes, views and streaming callbacks over a response, compile-time expression templates. A write through `Table<Item>` is what it says: no `_type` and no
key check.

## 16. DynaSharp.Testing

The wire is the seam, and it serves every API. A test hands a client a `StubHandler`, queues responses and asserts on `stub.Requests`. `StubClient.Create` makes that client, and `StubClient.CreateStreams` the one of DynamoDB Streams: the fixed region `us-east-1`, static fake credentials, `MaxAttempts = 1` and no attempt timeout, with nothing read from the environment or the shared AWS files. The second argument adjusts the options the way a variation of them is written, and the client validates the result.

```csharp
using var stub = new StubHandler();
using var client = StubClient.Create(stub);
stub.Enqueue(StubResponse.Item(customer));

using var retrying = StubClient.Create(stub, options => options with { MaxAttempts = 4, TimeProvider = clock });
```

There is no attempt timeout because `AttemptTimeout` runs on `TimeProvider`, so a fake clock that a test moves would time out the request it is answering. A test of a timeout sets one. The timeout of a wait for a table runs on that clock too, so a loop that advances it with `Task.Yield` burns the whole timeout: such a wait takes a timeout the loop does not reach, or the clock moves once per poll the handler has seen.

| Type | Members |
|---|---|
| `StubHandler` | `Enqueue`, `DefaultResponse`, `RecordRequests`, `Requests`, `ThrowIfResponsesLeft`; a constructor taking a responder |
| `StubClient` | `Create`, `CreateStreams` |
| `StubResponse` | Results: `Item`, `Items`, `Count`, `Attributes`, `BatchItems`, `Unprocessed`. Tables: `Table`, `Table<T>`, `TableCreated`, `TableUpdated`, `TableDeleted`, `TablesListed`, `TimeToLive`, `TimeToLiveUpdated`. Streams: `StreamDescription`, `ShardIterator`, `Records`. Failures: `ConditionalCheckFailed`, `TransactionCanceled`, `ResourceNotFound`, `ResourceInUse`, `DynamoDbError`. Parts: `Keys`, `KeyOf`, `Put`, `Delete`, `Reason`, `ConditionalCheckFailedReason`, `GlobalIndex`, `Shard`, `Inserted`, `Modified`, `Removed`, `Change`. Anything else: `Json` |
| `RecordedRequest` | `Operation`, `Headers`, `Method`, `RequestUri`, `Body`, `BodyText`, `BodyJson`, `TableName`, `IndexName`, `ConsistentRead`, `ScanIndexForward`, `Limit`, `ReadItem()`, `ReadItem<T>()`, `ReadKey()`, `ReadKey<T>()`, `ConditionText`, `UpdateText`, `KeyConditionText`, `FilterText`, `ProjectionText`, `TransactItems` |
| `RecordedTransactStep` | `Kind` (a `TransactStepKind`: `Put`, `Update`, `Delete`, `ConditionCheck`, `Get`, `Unknown`), `TableName`, `ReadItem()`, `ReadItem<T>()`, `ReadKey()`, `ReadKey<T>()`, `ConditionText`, `UpdateText`, `ProjectionText` |
| `RecordingHandler` | A `DelegatingHandler` that records `Exchanges` of a real client: `Request`, `Response`, `Error` |
| `StubSts` | `IDisposable`. `Endpoint`, `Enqueue`, `DefaultResponse`, `Requests` |
| `StubStsResponse` | `Credentials`, `Error`. Anything else: `StatusCode` and `Body` |
| `RecordedStsRequest` | `Action`, `RoleArn`, `RoleSessionName`, `WebIdentityToken`, `Form`, `Headers` |
| `FakeDynamoDb` | A constructor taking a `TimeProvider`. Clients: `CreateHandler`, `CreateClient`, `CreateStreamsClient`. The wire: `Answer(RecordedRequest)`, `Answer(operation, requestJson)`. Tables: `CreateTable(request)`, `CreateTable<T>(name)`, `DescribeTable`, `DeleteTable`, `TableNames`, `Reset`. Items: `Put`, `Put<T>`, `Get`, `Get<T>`, `Delete`, `Items`, `Items<T>`, `ExpireItems` |

The helpers go through `WriteAttributes`, so they cannot drift from the codec, and `Items` takes entities as well as an `Item`.

**A response of the wrong operation throws.** A response of a factory carries the operations its body is written for, and `StubHandler` compares them with the `X-Amz-Target` of the request that takes it, whether the response came from the queue, the responder or `DefaultResponse`. A mismatch throws `InvalidOperationException` naming both operations and the position of the response in the queue, because the client reads most such bodies without complaint: an answer of `UpdateTimeToLive` taken by a `GetItem` reads as no item, and a test of a miss passes for the wrong reason. The errors go by what the factory is for: `ConditionalCheckFailed` answers `PutItem`, `UpdateItem` and `DeleteItem`, `TransactionCanceled` the two transactions. `ResourceNotFound` and `ResourceInUse` have the shape that many operations return, and fit any, as do `Json`, `DynamoDbError` and `new StubResponse()`. There is no switch to turn the check off: a test that wants a body for another operation writes it with `Json`. A response left in the queue is no error, because a test that fails it by default would break suites that queue one answer too many; `ThrowIfResponsesLeft()` is the opt-in check, and `Dispose` never checks.

**Members of a recorded request.** `TableName`, `IndexName`, `ConsistentRead`, `ScanIndexForward` and `Limit` read the member of that name at the top of the body as it was sent, `null` where the request has none. A batch and a transaction have no `TableName` at the top, `ConsistentRead` of a `BatchGetItem` is per table, and a command sends `ConsistentRead` only as `true` and `ScanIndexForward` only as `false`, so `null` stands for the default of DynamoDB. The name map stays with `BodyJson`: its keys are the placeholder numbering the texts below exist to hide. `ToRequestJson()` is the second seam: what a chain would send, without a client. The credentials are not behind this seam, see `HttpHandler` in section 2, so a test that does not use `StubClient` also sets `Credentials`.

**Answers of administration and streams.** A body holds what DynamoDB sends and stays within the service model. `Table(name, status, indexes)` is a table on demand with the string key `pk`, and `GlobalIndex(name, status)` an index with one key attribute named after it. `Table<T>(name, status)` has the keys, types and local indexes of the entity and every global index it declares as active, built as `CreateTable()` builds them, so a wait for the declared indexes ends on it. It lists one entity: a table of several takes the indexes by name. `TableCreated` defaults to `CREATING` and `TableUpdated` to `UPDATING`, with the indexes in the status each was given, `GlobalIndex(name, IndexStatus.Creating)` for one the update adds. `TableDeleted` holds what the API reference shows for `DeleteTable`, without keys and indexes. `TablesListed(names)` is the last page of a `ListTables`, and `TablesListed(names, lastEvaluatedTableName)` a page that more pages follow; the names go out in the order given. A `StubHandler` that hands another answer to an `UpdateTable` or a `ListTables` names these two in its failure. `Count(count, scannedCount)` has no `Items` and takes a last evaluated key as `Items` does. `Records(nextShardIterator, ...)` numbers the records of a page that carry no `EventId` or `SequenceNumber`, and `null` for the iterator ends a closed shard. Values a test does not state are fixed, so an answer does not depend on the clock: the region of `StubClient`, the account `123456789012`, the time 2026-01-01.

**Text of a recorded expression.** Each text is the expression with its placeholders resolved from the maps next to it, or `null` if the request has no such expression. One pass reads each placeholder to its end, so `:v1` is no prefix of `:v10`, and what was put in is not read again. Keywords, functions and spacing stay as sent, and a placeholder the maps lack stays as it is, which is the mistake a test should see. A name stands as it is, in a document path too, and in backticks if it holds a dot or a bracket: `` `a.b` `` is one attribute and `a.b` a path. A value reads in the notation of PartiQL with double quotes, from its wire form:

| Wire | Text |
|---|---|
| `{"S":"Placed"}` | `"Placed"`, with the escapes of JSON |
| `{"N":"3.10"}` | `3.10` |
| `{"BOOL":true}`, `{"NULL":true}` | `true`, `null` |
| `{"B":"aGk="}` | `b"aGk="` |
| `{"L":[{"S":"a"},{"N":"1"}]}` | `["a", 1]` |
| `{"M":{"k":{"N":"1"}}}` | `{"k": 1}` |
| `{"SS":["a","b"]}`, `{"NS":["1"]}`, `{"BS":["aA=="]}` | `<<"a", "b">>`, `<<1>>`, `<<b"aA==">>` |

`TransactItems` reads `TransactWriteItems` and `TransactGetItems` step by step, empty for any other request, and each step resolves with its own maps. `BatchWriteItem` stays with `BodyJson`.

**The fake.** `FakeDynamoDb` is an in-memory DynamoDB behind the same seam, for functional tests. `CreateHandler()` is a `StubHandler` whose responder is `Answer(RecordedRequest)`, and `CreateClient(configure)` is `StubClient.Create` over a new one, `CreateStreamsClient(configure)` `StubClient.CreateStreams`, so `Requests`, `Enqueue` and `ThrowIfResponsesLeft` keep their meaning: a queued response answers in place of the fake, which never sees it. `Answer(operation, requestJson)` takes the bare name or the whole target and serves a server or a raw client. Every handler of one fake shares its state; the fake holds one lock, under which every request and every direct call runs from start to end, and no static state. A table exists only once created, over the wire, by `CreateTable(request)` with the checks of the wire, or by `CreateTable<T>(name)`, which goes through `TableSchema` as `CreateTable()` of the entity does; it and its indexes are `ACTIVE` in that answer, and `DeleteTable` frees the name at once. `Put`, `Get`, `Delete` and `Items`, each also for entities through `Item.From` and `To<T>`, use the storage path of `PutItem`, `GetItem` and `DeleteItem` without a condition, and throw what a client throws for the answer of the wire: the error goes through `ErrorMapper`. `Get` and `Items` give copies; `Items` is in key order. The constructor takes the `TimeProvider` that dates tables and that `ExpireItems()` compares the attribute of time to live with, in epoch seconds, leaving an expiry more than five years before the clock as DynamoDB does; nothing expires otherwise. The clock also dates the stream records, expires a shard iterator after 15 minutes, trims records after 24 hours and removes a stream 24 hours after it was disabled; `ExpireItems()` records its deletes with the service as `userIdentity`. Operations the fake does not answer are HTTP 400 `UnknownOperationException`; an enum value DynamoDB does not have is its `ValidationException`; a member it does not implement inside one it answers is HTTP 400 with `__type` `DynaSharp.Testing#UnsupportedByFakeException`, so `ErrorCode` is `UnsupportedByFakeException`. No member is ignored. `docs/testing.md` lists the operations and the semantics.

A test of the way a pod gets its credentials has `StubSts`, which stands in for STS. Since a provider has its own HTTP client, it is a server and not a handler: HTTP/1.1 on `127.0.0.1` and a free port, listening from the constructor to `Dispose`, with one port per instance and no call to a network. It answers `AssumeRoleWithWebIdentity` with the next queued response, else `DefaultResponse`, else 400 with the code `StubStsNoResponse`, which the provider puts in its `CredentialsUnavailableException`. `StubStsResponse.Credentials` is the sample of the AWS documentation with the given values, with `AssumedRoleUser` derived from the role and session of the request and `2100-01-01T00:00:00Z` for credentials without an expiration, a fixed date beyond any clock a test runs on, so that neither the advisory refresh 15 minutes before it nor the mandatory one 10 minutes before it opens unless a test gives an expiration of its own, and `Error` has the status botocore's model gives the code, 400 for one it does not list. There are two ways to it. `new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint)` reads no variable, so the test runs in parallel with any other. Or a test sets `AWS_ENDPOINT_URL_STS` to `sts.Endpoint` itself, with `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, and goes through the default chain as a pod does; that test is the one that must not run in parallel. The stub never touches the process environment or the disk: the token file is the test's to write.

## Table administration and conflicts

`orders.CreateTable().Including<Customer>().SendAsync(ct)` creates a table from what the entities declare: one entity cannot describe a single-table schema, so the table's indexes are the union over the entities named. Two entities that give an attribute two types throw at `Including`. `Provisioned(read, write)`, `Stream(view)`, `TimeToLive(attribute)`, `WaitUntilActive()` and `IfNotExists()` shape it, and `ToRequest()` hands over a `CreateTableRequest` for what it does not cover, such as tags. A custom converter on a plain key counts as a string unless it implements `IAddableConverter`. `IfNotExists()` describes first: a table `DescribeTable` finds that is not `DELETING` ends the create there, with no `CreateTable`, so an existing table takes `dynamodb:DescribeTable` alone and leaves no failed call in CloudTrail, unless `TimeToLive` is on the chain, which goes on and takes `dynamodb:DescribeTimeToLive` and `dynamodb:UpdateTimeToLive`, and with `WaitUntilActive()` a first look that shows the table and its indexes active needs no other. A missing or `DELETING` table goes on to `CreateTable`, so creating takes both permissions, and an error of the first look other than a missing table surfaces. `IfNotExists()` does not compare schemas, and a table that is `DELETING` is no success: the `ResourceInUseException` surfaces, with and without a wait. When `CreateTable` says `ResourceInUseException` and `DescribeTable` then finds no table, which is a delete that just ended or a table another process just created, the command creates again, up to 5 times over 1.5 seconds, and the exception surfaces only if that settles nothing. `DeleteTableAsync` deletes all items and does not wait.

`Stream(view)` goes into the request, so `ToRequest()` shows it, and it applies to a table this command creates: the stream of an existing table stays untouched under `IfNotExists()`. A value that is no view throws at the clause. `TimeToLive(attributeName)`, and `TimeToLive(Attr)`, which a generated handle converts to and which refuses a nested path, ensures time to live, after the create or after finding the table, and it is not in `ToRequest()`, because `CreateTable` does not take it. It needs an active table, so it implies the wait, with the default timeout unless `WaitUntilActive(timeout)` gave another, and a declared index that is missing times it out as it does any wait. Then `DescribeTimeToLive`: `ENABLED` or `ENABLING` on that attribute ends it, and anything else sends `UpdateTimeToLive`. A `ValidationException` of that call, which is what a second instance gets when the first one won, looks again: time to live now on for that attribute is success, and otherwise it rethrows, which is how time to live on another attribute surfaces. It takes `dynamodb:DescribeTimeToLive`, `dynamodb:UpdateTimeToLive` when it is not on yet, and `dynamodb:DescribeTable` for the wait.

The indexes of the command apply, like `Stream(view)`, to a table it creates. An index for a table that exists goes in this order, which the README shows: `CreateTable().IfNotExists()` without a wait, then `WaitUntilActiveAsync` on `client.Table(name, "pk", "sk")`, which declares nothing, because `UpdateTable` needs an active table and a typed wait times out on an index that is not there, then `UpdateTableAsync`, one index per call, and last the typed wait, with a timeout that fits the backfill. `TimeToLive` implies that wait, so it goes on the last step.

`client.RetryOnConflictAsync(ct => ..., ct)`, and `orders.RetryOnConflictAsync(ct => ..., ct)` for code that holds a table and no client, with the same overloads and the same loop, runs a delegate again on `ConditionalCheckFailedException`, `TransactionConflictException`, and a cancelled transaction with those reasons, up to 5 runs by default, with a small jitter through the client's `TimeProvider`. Four more overloads on each take a `ConflictKind retryOn` after the delegate, with and without `maxAttempts`: `ConditionalCheckFailed`, `TransactionConflict` or `Any`, a `[Flags]` enum whose `Any` is what the overloads without it retry. A conflict it does not name surfaces at once like any other failure, with no wait and no telemetry of a conflict, a cancelled transaction runs again when any of its reasons is of a named kind, and a value that is none of the three throws `ArgumentOutOfRangeException` at the call. `TransactionConflict` is for a run whose failed condition is its answer, a uniqueness claim or a record that exists, where asking again changes nothing. Nothing else retries a conflict. The delegate must read again, or the retry changes nothing. A conditional write whose response is lost fails its own pipeline retry, and the helper then applies the change again: `.MaxAttempts(1)` on the write avoids that. Follow-up writes stay outside the delegate: a retry inside it multiplies the runs, and one that gave up counts as a conflict of the outer one. What surfaces after the last run is the exception of that run, so a `ConditionalCheckFailedException` holds the `Key` of the write of that run, see section 15.
