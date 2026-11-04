# Migrating

DynaSharp has no context that loads and saves objects: a table takes commands named after DynamoDB's operations, each sent where its chain ends. For a table other code wrote, see [Adopting an existing table](adopting.md).

`orders`, `customers`, `products` and `carts` are the tables of [the shop](getting-started.md#the-shop).

## Verbs

| AWS SDK `DynamoDBContext` | EfficientDynamoDb | DynaSharp |
|---|---|---|
| `LoadAsync<Order>(id, number)` | `GetItemAsync<Order>(pk, sk)` | `orders.Get(Order.Keys.Of(id, number)).ItemAsync(ct)` |
| `SaveAsync(order)` | `PutItemAsync(order)` | `orders.Put(order).SendAsync(ct)`, which replaces the whole item. `SaveAsync` sends an `UpdateItem`; `orders.Update(key).Set(...)` keeps attributes the class does not map |
| `QueryAsync<Order>(id)` | `Query<Order>().WithKeyExpression(...).ToListAsync()` | `orders.Query(Order.Keys.Pk.Of(id)).ItemsAsync(ct)` |
| `DeleteAsync<Order>(id, number)` | `DeleteItemAsync<Order>(pk, sk)` | `orders.Delete(Order.Keys.Of(id, number)).SendAsync(ct)` |
| `ScanAsync<Customer>(conditions)` | `Scan<Customer>().WithFilterExpression(...)` | `customers.Scan().Filter(Customer.Attrs.Email.Eq(email)).ItemsAsync(ct)` |
| `CreateBatchWrite<Product>()`, `AddPutItems`, `ExecuteAsync` | `BatchWrite().WithItems(Batch.PutItem(...))` | `products.BatchWrite().PutAll(catalog).SendAsync(ct)` |
| `Add(entity)` and `SaveChanges()` of a unit of work, as in EF Core | No such pattern | None: every command is sent at its terminal. `client.TransactWrite()` writes several items all or nothing, see [Transactions and batches](transactions.md) |

`orders` is `client.Table<Order>("Shop")`, opened once, see [Tables](tables.md#opening-a-table). `ItemsAsync` streams the items across pages; the SDK's `QueryAsync` needs `GetRemainingAsync` or `GetNextSetAsync`.

## From the AWS SDK

The tables compare with `DynamoDBContext` of `AWSSDK.DynamoDBv2` 4.0 in its default conversion, V1. Under `Conversion = DynamoDBEntryConversion.V2`, a `bool` is a `BOOL` and a list an `L`, as here, so those rows fall away.

### What changes stored data

Compare an item the old code wrote with the new code's `ToRequestJson()`, attribute by attribute, before you rely on an unchanged format. Notes follow the table.

| What you wrote | What to write |
|---|---|
| A string that can be empty, whose attribute `SaveAsync` removes for `""`. | `OmitEmpty = true` in `[assembly: DynamoDbDefaults]`, which reaches nested documents, or `[OmitEmpty]` on a type or property. Without it DynaSharp stores `{"S":""}`. |
| An empty list of documents, dictionary or `byte[]`, which the SDK keeps. | `OmitEmpty` also covers lists, sets, dictionaries and arrays: an empty one is not written and reads back as null or the initial value. Where null and `[]` differ, `[OmitEmpty(false)]` on the property keeps the empty one. |
| `SaveAsync`, which sends an `UpdateItem`: it sets what the class maps and leaves every other attribute. | A `Put` replaces the whole item, dropping attributes the entity does not map. Map them, or send `Update(key)` with a `Set` per attribute. |
| A timestamp in a key that the AWS SDK wrote with three fraction digits: `2026-10-09T10:00:00.123Z`. | `{At:ms}`, which writes and reads that form. |
| An upsert that names only part of an index key. | An update that sets every input of the index, or ends in `.IfExists()`, or a `Put`. |
| A null property. `SaveAsync` removes the attribute, or with `IgnoreNullValues = true` leaves it alone. | `Set(Attrs.X, null)` in an `Update` removes it; `SetIfNotNull(Attrs.X, value)` adds nothing for null. A `Put` leaves a null out of the item. |
| A `List<string>` or `string[]`, an `SS` in V1. `List<int>` is an `NS`; an empty one is left out. | `HashSet<string>`, or a set converter: `SetArrayConverter<T, TElement>` on an array, `StringListAsSetConverter` on a list. |
| A `bool`, an `N` of `1` or `0` in V1. | `BoolAsNumberConverter`, below. |
| A `DateTime`, UTC text with three fraction digits: `2026-10-31T12:00:00.123Z`. | `SdkDateTimeConverter`, below. |
| A `DateTimeOffset` your own converter stored with its offset: `2026-11-20T11:00:00.0000000+01:00`. | `[DynamoDbConverter(typeof(OffsetTimestampConverter))]`, below. |
| `""` that other code wrote for no value, such as a blank timestamp or JSON document. | `EmptyStringAsMissingConverter<T, TConverter>`, below: it reads `""` as a missing attribute, so the property keeps its initial value or reads null. |
| `StoreAsEpoch = true` on a `DateTime`, an `N`. | `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset`. |
| An enum, an `N` in V1 and V2. | `StatusAsNumberConverter`, below. DynaSharp stores the name as text; only a `[Flags]` enum as a number. |
| `[DynamoDBVersion] public long? Revision`, whose first `SaveAsync` stores 0. | `[Version(Missing = VersionMissing.Allow)] public long Revision`, not nullable, see [Adopting an existing table](adopting.md). Under plain `[Version]`, 0 means no item, so a write at 0 fails on such an item. |
| `[DynamoDBTable("Shop", LowerCamelCaseProperties = true)]` | `Naming = AttributeNaming.CamelCase` in `[assembly: DynamoDbDefaults]`, which applies to a plain key too, as in the SDK. |
| Items without `_type` or a version, attributes under their own names. | [Adopting an existing table](adopting.md). |

- **Empty strings.** `SaveAsync` removes the attribute for `""` also in a nested document and with `IgnoreNullValues`, unless `IsEmptyStringValueEnabled` is on. Under `OmitEmpty` a non-nullable `required` property is still written.
- **Timestamps in keys.** A bare `{At}` is `ticks`, seven digits: `2026-10-09T10:00:00.1230000Z`. Over rows the SDK wrote, a table key built from a value misses the stored row, and a `Put` writes a second item. In an index key nothing misses, but new items hold another format than old ones, and a range bound built from a value sorts apart from them. Under `{At:ms}` a read cannot restore `At` from the key, so an item an `Update` created needs the attribute.
- **Upserts.** A typed update writes the index keys it can compute. On a non-sparse index, `.Set(Order.Attrs.PlacedAt, at)` writes `bystatussk` but no `bystatuspk`, so an item it creates is silently missing from the index; with `Sparse = true` the call throws. A `[KeyOnly]` input has no handle to set, so such an index needs a plain attribute holding the whole key: a stored property under `PartitionKey = "{GroupKey}"`, set by its handle.
- **Lists as sets.** On an array, `[DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))] public string[] Tags { get; init; } = [];` (`NumberSetElement<int>` for numbers); on a list, `[DynamoDbConverter(typeof(StringListAsSetConverter))] public List<string> Tags { get; init; } = [];`, below. An empty one is left out here too. A duplicate writes `"SS":["x","x"]`, which DynamoDB rejects. A plain list or array is an `L` here; reading an `SS` into it throws `DynamoDbMappingException`. A set has no order, and a list keeps the order it was written in: code that moves a set to a list reads its elements in that order, no longer sorted.
- **Booleans.** `BoolAsNumberConverter` writes `1` and `0` and reads any nonzero number as true, as the SDK does. Without it a `bool` is a `BOOL`, and reading an `N` into it throws.
- **`DateTime`.** `SdkDateTimeConverter` writes the SDK's text, drops what is below the millisecond, takes `DateTimeKind.Unspecified` as local time and reads `DateTimeKind.Utc`, as SDK 4 does under its default `RetrieveDateTimeInUtc = true`. Without it DynaSharp reads that text but writes seven digits, `2026-10-31T12:00:00.1230000Z`, so a condition no longer matches an old value, old and new sort apart in a key, and `Unspecified` is rejected. `default(DateTime)` is `Unspecified` too, so test data that leaves a `DateTime` unset throws on its first write, where the SDK stored it. An SDK `TimeOnly` has three digits too and needs your own converter.
- **Offsets.** Without `OffsetTimestampConverter` DynaSharp stores the instant in UTC, `2026-11-20T10:00:00.0000000Z`, and reads the old text as that instant, so items move to UTC as they are written. Text with offsets does not sort chronologically, so ranges over it break across offsets.
- **Blank strings.** Without `EmptyStringAsMissingConverter<T, TConverter>` reading the item throws `DynamoDbMappingException` for that attribute.

Everything else stores what the SDK stores: numbers, strings, `Guid`, `DateOnly`, `byte[]`, a `HashSet` of strings or numbers, a `Dictionary<string, string>` and a nested document.

Register converters once for the project; `[DynamoDbConverter]` does it for one property. `BoolAsNumberConverter` and `SdkDateTimeConverter` ship in `DynaSharp.Serialization`; the other two here are your own.

```csharp
using DynaSharp;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[assembly: DynamoDbDefaultConverter(typeof(bool), typeof(BoolAsNumberConverter))]
[assembly: DynamoDbDefaultConverter(typeof(DateTime), typeof(SdkDateTimeConverter))]
[assembly: DynamoDbDefaultConverter(typeof(OrderStatus), typeof(StatusAsNumberConverter))]

public readonly record struct StatusAsNumberConverter : IDynamoDbConverter<OrderStatus>
{
    public static void Write(DynamoDbWriter writer, OrderStatus value) => writer.WriteNumber((int)value);

    public static OrderStatus Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);
        return (OrderStatus)reader.GetNumber<int>();
    }
}

public readonly record struct StringListAsSetConverter : IDynamoDbConverter<List<string>>
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

`IsOmitted` is there because DynamoDB refuses an empty set, `TryReadMissing` so that a `Select` reads a missing one as empty. `IDynamoDbConverter<T>` is static abstract, so a public converter generic over its value fails `CA1000` where that is an error: write one struct per type, over a helper. A `readonly record struct` also passes `CA1815`, which a plain `readonly struct` fails under a strict analysis mode.

Your converter may call the single-value converters, such as `DateTimeConverter` and `StringConverter`, and `SetArrayConverter<T, TElement>`: they are API. The list, map, set and document converters and the null wrappers, such as `ReadOnlyListConverter<T, TConverter>`, `HashSetConverter<T, TElement>` and `NullableConverter<T, TConverter>`, are public only for generated code: a call compiles but follows the package, so rebuild with every new version.

The other two shipped converters go on a property. `OffsetTimestampConverter` keeps the offset your own converter stored. `EmptyStringAsMissingConverter<T, TConverter>` reads a stored `""` as a missing attribute and anything else through the converter it names, here the built-in one of `DateTimeOffset`:

```csharp
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[DynamoDbEntity]
[Discriminator("SHIPMENT")]
[PartitionKey("SHIPMENT#{Id}")]
public partial record Shipment
{
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(OffsetTimestampConverter))]
    public DateTimeOffset PromisedFor { get; init; }

    [DynamoDbConverter(typeof(EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>))]
    public DateTimeOffset? DeliveredAt { get; init; }
}
```

`PromisedFor` stores `2026-11-20T11:00:00.0000000+01:00` and reads that offset back. `DeliveredAt` reads `{"S":""}` as null; a non-nullable property would keep its initial value. Only the property is covered: in a list each `""` still throws.

### The rest of the code

| What you wrote | What to write |
|---|---|
| `[DynamoDBHashKey]`, `[DynamoDBRangeKey]` | `[PartitionKey]`, `[SortKey]` on the property. `[DynamoDBHashKey("id")]` is `[PartitionKey(AttributeName = "id")]`. |
| `[DynamoDBProperty("total")]`, `[DynamoDBIgnore]` | `[DynamoDbAttribute("total")]`, `[DynamoDbIgnore]`. |
| `[DynamoDBProperty(typeof(MyConverter))]` with an `IPropertyConverter` | `[DynamoDbConverter(typeof(MyConverter))]` with an `IDynamoDbConverter<T>`. |
| A stored value that no longer fits the property, such as a removed enum name. | `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` on the enum, see [Converters](entities.md#converters). For another type, a converter that answers `CountsAsMissing`; for a blank string, `EmptyStringAsMissingConverter<T, TConverter>`, above. |
| A property computed from others that feeds an index. | `[ComputedFrom]`, see [Computed index keys](entities.md#computed-index-keys). |
| A date bucket in a key: `$"DAY#{arrival:yyyy-MM-dd}"`. | `{Arrival:yyyy-MM-dd}` in a template takes the property's type, so a `DateOnly day` goes in as `new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero)`. A day that is a value of its own is a `DateOnly` property. |
| A key put together by hand: `$"CUSTOMER#{id}"`. | `Order.Keys.Of(id, number)` and `Order.Keys.Pk.Of(id)` check the values and throw `KeyFormatException` for one that holds a delimiter. For a value from outside, `Order.Keys.TryOf(id, number, out var key)` returns false instead. |
| A key whose value holds the delimiter: `$"appointment#{key}"` over `a#b`. | `[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]` with `[KeyOnly] public required string Key { get; init; }`, unless the stored items hold a `Key` attribute. See [Adopting an existing table](adopting.md#keys-that-hold-your-delimiter). |
| `QueryOperator.Between` over two key strings. | `Keys.Sk.Between(from, to)`, which includes `to`, or `Keys.Sk.Range(from, to)`, which excludes it. |
| An `Expression` with `#names` and `:values`, or a `ScanCondition`. | `Order.Attrs.Total.Gt(100m)`, which is typed. |
| A `SaveAsync` that may create the item, storing every property. | `Update` with `.StoreKeyInputs()`, see [PutItem](commands.md#putitem). |
| A loop that reads, writes and catches `ConditionalCheckFailedException`. | `RetryOnConflictAsync`, see [Retry on a conflict](commands.md#retry-on-a-conflict). |
| Working out in a `catch` which item failed. | `ConditionalCheckFailedException.Key`, see [Conditions](commands.md#conditions). |
| `DescribeTable`, `CreateTable`, a catch of `ResourceInUseException`, a wait. | `carts.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct)`. |
| `UpdateTimeToLive` after the create, a `StreamSpecification` in it. | `.TimeToLive(Cart.Attrs.ExpiresAt)` and `.Stream(StreamViewType.NewAndOldImages)` on the same chain, see [Creating a table](tables.md#creating-a-table). |
| `ServiceURL` on `AmazonDynamoDBConfig` for an emulator, with made-up `BasicAWSCredentials`. | `ServiceUrl` in the options with any region and credentials, or `AWS_ENDPOINT_URL_DYNAMODB`, `AWS_REGION`, `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` and no code, see [Run a local emulator](getting-started.md#run-a-local-emulator). |
| `AmazonDynamoDBStreamsClient` and a loop of `GetShardIterator` and `GetRecords` that renews an expired iterator. | `new DynamoDbStreamsClient(client.Options)` and `ReadShardAsync`, which pages, renews the iterator and waits on an idle shard. Which shards to read, and children after their parent, stays yours, see [Streams](streams.md). |
| Credentials from `AWSSDK.Core`, with IRSA on EKS. | `new DynamoDbClient()`, which reads the same [chain](getting-started.md#credentials). `AWS_ROLE_SESSION_NAME` names the session; the default is `dynasharp-` and the Unix time. |
| A region from EC2 instance metadata. | `AWS_REGION`, or `Region` in the options. DynaSharp does not ask the metadata service, see [Region](getting-started.md#region). |
| A mock of `IDynamoDBContext` or `IAmazonDynamoDB`. | `StubClient.Create(stub)` over a `StubHandler`, which fakes the wire, and `StubSts` for STS. See [Testing](testing.md). |
| Tests against DynamoDB Local in a container. | `FakeDynamoDb`, in memory and one per test, which needs no Docker. See [Testing](testing.md). |

- **Key ranges.** `Between` and `Range` take values, not strings, see [Sort key conditions](queries.md#sort-key-conditions). Under `{At:ms}`, the value one step before `to` is `to.AddMilliseconds(-1)`.
- **Creating the table.** See [Creating a table](tables.md#creating-a-table), and [Adding an index](tables.md#adding-an-index) for a table that lacks one. `IfNotExists()` compares no schemas: over a table of the old layout it changes nothing, and the wait fails on the first index the entities declare and the table lacks, see [A table that exists](tables.md#a-table-that-exists). A table whose keys changed is dropped and created again.
- **Retries that run out.** When every run of `RetryOnConflictAsync` loses, it rethrows the exception of the last run, such as a `ConditionalCheckFailedException`. Code that caught an exception of its own after its retry loop catches that one instead.
- **Lost races in tests.** `FakeDynamoDb` answers one request at a time and never reports `TransactionConflict`, so two writers started together do not race. A test of a lost race makes the other writer's change itself, between the read and the write of the code under test, and so runs the same way every time.

## From EfficientDynamoDb

DynaSharp shares EfficientDynamoDb's aim: UTF-8 on the wire, little allocation, no AWS SDK. The difference: mapping is generated at build time instead of found by reflection, so Native AOT works, and keys are templates the generator fills instead of strings you assemble.

| What you wrote | What to write |
|---|---|
| `[DynamoDbTable("Shop")]` on the class | The name where the table is opened, `client.Table<Order>("Shop")`. An entity is not bound to one table. |
| `[DynamoDbProperty("pk", DynamoDbAttributeType.PartitionKey)]` on a string the code fills | `[PartitionKey("CUSTOMER#{CustomerId}")]` on the type, a template filled from properties, or `[PartitionKey(AttributeName = "pk")]` on a property, a plain key. The same for `[SortKey]`. |
| `[DynamoDbProperty("total")]` | `[DynamoDbAttribute("total")]`, or nothing when the name is the property's. |
| A `DdbConverter<T>` under `[DynamoDbConverter(typeof(...))]` | An `IDynamoDbConverter<T>`, with static members, under `[DynamoDbConverter(typeof(...))]` of `DynaSharp.Mapping`. See [Converters](entities.md#converters). |
| `[DynamoDbVersion]` | `[Version]`, see [Optimistic locking](commands.md#optimistic-locking). |
| `Condition<Order>.On(x => x.Status).EqualTo("Placed")` | `Order.Attrs.Status.Eq(OrderStatus.Placed)`, see [Expressions](expressions.md). |
| `UpdateItem<Order>().WithPrimaryKey(pk, sk).On(x => x.Total).Assign(10m).ExecuteAsync()` | `orders.Update(Order.Keys.Of(id, number)).Set(Order.Attrs.Total, 10m).SendAsync(ct)`. |
| `TransactWrite().WithItems(Transact.PutItem(order), ...)` | `client.TransactWrite().Put(orders, order)`, one step per action, see [Transactions and batches](transactions.md). |
| `context.LowLevel` with a `Document` | The request API of `DynaSharp.Requests`, or a `Table<Item>`, see [API levels](api-levels.md). |
| `DynamoDbContextConfig` with a `RegionEndpoint` and `AwsCredentials` | `DynamoDbClientOptions` with `Region` and `Credentials`, or nothing and the environment, see [Getting started](getting-started.md#configure-the-client). |

The stored formats differ in places: compare items as for the AWS SDK above. A converter covers each difference, and the [rules for a table that exists](adopting.md) cover items without `_type` or a version.
