# Migrating

DynaSharp has no context that loads and saves objects. A table takes commands named after DynamoDB's operations, and each is sent where its chain ends. This page maps the verbs first, then what changes for code and for stored data coming from the AWS SDK's `DynamoDBContext` or from EfficientDynamoDb. A table that other code wrote keeps working under the rules of [Adopting an existing table](adopting.md).

`orders`, `customers`, `products` and `carts` are the tables of [the shop](getting-started.md#the-shop).

## Verbs

| AWS SDK `DynamoDBContext` | EfficientDynamoDb | DynaSharp |
|---|---|---|
| `LoadAsync<Order>(id, number)` | `GetItemAsync<Order>(pk, sk)` | `orders.Get(Order.Keys.Of(id, number)).ItemAsync(ct)` |
| `SaveAsync(order)` | `PutItemAsync(order)` | `orders.Put(order).SendAsync(ct)`, which replaces the whole item. `SaveAsync` sends an `UpdateItem`, so `orders.Update(key).Set(...)` is the write that keeps attributes the class does not map |
| `QueryAsync<Order>(id)` | `Query<Order>().WithKeyExpression(...).ToListAsync()` | `orders.Query(Order.Keys.Pk.Of(id)).ItemsAsync(ct)` |
| `DeleteAsync<Order>(id, number)` | `DeleteItemAsync<Order>(pk, sk)` | `orders.Delete(Order.Keys.Of(id, number)).SendAsync(ct)` |
| `ScanAsync<Customer>(conditions)` | `Scan<Customer>().WithFilterExpression(...)` | `customers.Scan().Filter(Customer.Attrs.Email.Eq(email)).ItemsAsync(ct)` |
| `CreateBatchWrite<Product>()`, `AddPutItems`, `ExecuteAsync` | `BatchWrite().WithItems(Batch.PutItem(...))` | `products.BatchWrite().PutAll(catalog).SendAsync(ct)` |
| `Add(entity)` and `SaveChanges()` of a unit of work, as in EF Core | No such pattern | No unit of work: every command is sent at its terminal. `client.TransactWrite()` writes several items all or nothing, see [Transactions and batches](transactions.md) |

`orders` is `client.Table<Order>("Shop")`, opened once, see [Tables](tables.md#opening-a-table). `ItemsAsync` streams the items and follows pages, where `QueryAsync` of the SDK needs `GetRemainingAsync` or `GetNextSetAsync`.

## From the AWS SDK

The tables compare with `DynamoDBContext` of `AWSSDK.DynamoDBv2` 4.0 in its default conversion, V1. With `Conversion = DynamoDBEntryConversion.V2` a `bool` is a `BOOL` and a list is an `L`, as here, so those rows fall away.

### What changes stored data

| What you wrote | What to write |
|---|---|
| A string that can be empty. `SaveAsync` removes the attribute for `""`, also in a nested document and with `IgnoreNullValues`, unless `IsEmptyStringValueEnabled` is on. | `OmitEmpty = true` in `[assembly: DynamoDbDefaults]`, which reaches nested documents, or `[OmitEmpty]` on a type or a property. Without it DynaSharp stores `{"S":""}`. A `required` property that is not nullable stays written even so. Compare captured items before you claim the format is unchanged: an item the old code wrote against the `ToRequestJson()` of the new code, attribute by attribute. |
| An empty list of documents, dictionary or `byte[]`. The SDK leaves out empty strings and keeps these. | `OmitEmpty` also covers lists, sets, dictionaries and arrays, so an empty list is not written, and reads back as null or as the initial value. Null and `[]` can mean different things, and `[OmitEmpty(false)]` on the property keeps the empty one. |
| `SaveAsync`, which sends an `UpdateItem`: it sets what the class maps and leaves every other attribute. | A `Put` replaces the whole item, so attributes the entity does not map are gone. Map them, or send `Update(key)` with a `Set` per attribute. |
| A timestamp in a key that the AWS SDK wrote, with three fraction digits: `2026-10-09T10:00:00.123Z`. | `{At:ms}`, which writes and reads that form. A bare `{At}` is `ticks` and writes seven digits, `2026-10-09T10:00:00.1230000Z`, so over such rows a key of the table built from a value misses the stored row, and a `Put` writes a second item. In a key of an index nothing misses, but new items hold another format than old rows, and a range bound built from a value sorts apart from them. Under `{At:ms}` a read cannot restore `At` from the key, so an item an `Update` created needs the attribute. |
| An upsert that names only part of an index key. | A typed update writes the index keys it can compute. On an index that is not sparse, `.Set(Order.Attrs.PlacedAt, at)` writes `bystatussk` and no `bystatuspk`, so an item it creates is not in the index, and nothing throws. With `Sparse = true` the same call throws. An update that can create an item sets every input of the index, ends in `.IfExists()`, or is a `Put`. A `[KeyOnly]` input has no handle to set, so such an index takes a plain attribute that holds the whole key: a stored property under `PartitionKey = "{GroupKey}"`, set by its handle. |
| A null property. `SaveAsync` removes the attribute, and with `IgnoreNullValues = true` leaves it alone. | `Set(Attrs.X, null)` in an `Update` removes it, and `SetIfNotNull(Attrs.X, value)` adds nothing for null. A `Put` leaves a null out of the item it replaces. |
| A `List<string>` or `string[]`, an `SS` in V1. `List<int>` is an `NS`, and an empty one is left out. | `HashSet<string>`, or `[DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))] public string[] Tags { get; init; } = [];`, with `NumberSetElement<int>` for numbers, or `[DynamoDbConverter(typeof(StringListAsSetConverter))] public List<string> Tags { get; init; } = [];`, below. An empty one is left out here too. An array or a list does not stop a duplicate, which writes `"SS":["x","x"]`, and DynamoDB rejects that. A plain list or array is an `L` here, and reading an `SS` into it throws `DynamoDbMappingException`. |
| A `bool`, an `N` of `1` or `0` in V1. | `BoolAsNumberConverter`, below, which writes `1` and `0` and reads any number but zero as true, as the SDK does. Here a `bool` is a `BOOL`, and reading an `N` into it throws. |
| A `DateTime`, text in UTC with three fraction digits: `2026-10-31T12:00:00.123Z`. | `SdkDateTimeConverter`, below, which writes that text, cuts off what is below the millisecond, takes `DateTimeKind.Unspecified` for local time and reads `DateTimeKind.Utc`, as SDK 4 does under its default `RetrieveDateTimeInUtc = true`. Without it DynaSharp reads that text and writes seven digits, `2026-10-31T12:00:00.1230000Z`, so a condition no longer finds an old value, and old and new sort apart in a key, and it rejects `Unspecified`. A `TimeOnly` has three digits as well, and takes a converter of your own. |
| A `DateTimeOffset` that your own converter stored with its offset: `2026-11-20T11:00:00.0000000+01:00`. | `[DynamoDbConverter(typeof(OffsetTimestampConverter))]`, below, which keeps the offset. DynaSharp stores the instant in UTC, `2026-11-20T10:00:00.0000000Z`, and reads the old text as that instant, so items move to UTC as they are written. Text with offsets does not sort chronologically, so a range over it is wrong across offsets. |
| `""` where a value belongs, which other code wrote for none: a timestamp or a JSON document that is a blank string. | `EmptyStringAsMissingConverter<T, TConverter>`, below, which reads `""` as if the attribute were not there, so the property keeps its initial value or reads null. Without it the read of the item throws `DynamoDbMappingException` for that attribute. |
| `StoreAsEpoch = true` on a `DateTime`, an `N`. | `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset`. |
| An enum, an `N` in V1 and V2. | `StatusAsNumberConverter`, below. DynaSharp stores the name as text, and only a `[Flags]` enum as a number. |
| `[DynamoDBVersion] public long? Revision`, whose first `SaveAsync` stores 0. | `[Version(Missing = VersionMissing.Allow)] public long Revision`, which is not nullable, see [Adopting an existing table](adopting.md). Under the default, `[Version]`, 0 means no item, so a write at 0 fails on such an item. |
| `[DynamoDBTable("Shop", LowerCamelCaseProperties = true)]` | `Naming = AttributeNaming.CamelCase` in `[assembly: DynamoDbDefaults]`, which takes a plain key along, as the SDK does. |
| Items without `_type`, items without a version, attributes under names of their own. | [Adopting an existing table](adopting.md). |

The rest stores what the SDK stores: numbers, strings, `Guid`, `DateOnly`, `byte[]`, a `HashSet` of strings or numbers, a `Dictionary<string, string>` and a nested document.

The converters are said once for the project, and `[DynamoDbConverter]` does the same for one property. `BoolAsNumberConverter` and `SdkDateTimeConverter` ship in `DynaSharp.Serialization`; the two others here are your own.

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

`IsOmitted` is there because DynamoDB refuses an empty set, and `TryReadMissing` so that a `Select` reads a missing one as empty. Converters are static, because `IDynamoDbConverter<T>` is static abstract, so a public one that is generic over its value fails `CA1000` in a project that treats it as an error. Write one struct per type, over a helper. A `readonly record struct` also passes `CA1815`, which a plain `readonly struct` fails under a strict analysis mode.

A converter of your own may call the converters of single values, such as `DateTimeConverter` and `StringConverter`, and `SetArrayConverter<T, TElement>`, as `StringListAsSetConverter` does: they are part of the API. The list, map, set and document converters and the wrappers of null, such as `ReadOnlyListConverter<T, TConverter>`, `HashSetConverter<T, TElement>` and `NullableConverter<T, TConverter>`, are public because generated code calls them, and they are infrastructure: a call compiles, and it follows the package, so build again with every new version of the package.

The other two that ship go on a property. `OffsetTimestampConverter` keeps the offset that a converter of your own stored, and `EmptyStringAsMissingConverter<T, TConverter>` reads a stored `""` as a missing attribute and everything else through the converter it names, here the built-in one of `DateTimeOffset`:

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

`PromisedFor` stores `2026-11-20T11:00:00.0000000+01:00` and reads that offset back. `DeliveredAt` reads `{"S":""}` as null, and a non-nullable property would keep its initial value. Only the property is asked: in a list each `""` still throws.

### The rest of the code

| What you wrote | What to write |
|---|---|
| `[DynamoDBHashKey]`, `[DynamoDBRangeKey]` | `[PartitionKey]`, `[SortKey]` on the property. `[DynamoDBHashKey("id")]` is `[PartitionKey(AttributeName = "id")]`. |
| `[DynamoDBProperty("total")]`, `[DynamoDBIgnore]` | `[DynamoDbAttribute("total")]`, `[DynamoDbIgnore]`. |
| `[DynamoDBProperty(typeof(MyConverter))]` with an `IPropertyConverter` | `[DynamoDbConverter(typeof(MyConverter))]` with an `IDynamoDbConverter<T>`. |
| A stored value that no longer fits the property, such as an enum name that is gone. | A converter that answers `CountsAsMissing`, see [Converters](entities.md#converters). For a blank string that is `EmptyStringAsMissingConverter<T, TConverter>`, above. |
| A property computed from others that feeds an index. | `[ComputedFrom]`, see [Computed index keys](entities.md#computed-index-keys). |
| A date bucket in a key: `$"DAY#{arrival:yyyy-MM-dd}"`. | `{Arrival:yyyy-MM-dd}` in a template takes the type of the property, so a `DateOnly day` goes in as `new DateTimeOffset(day, TimeOnly.MinValue, TimeSpan.Zero)`. A day that is a value of its own is a `DateOnly` property. |
| A key put together by hand: `$"CUSTOMER#{id}"`. | `Order.Keys.Of(id, number)` and `Order.Keys.Pk.Of(id)` check the values at the call, and throw `KeyFormatException` for a value that holds a delimiter. `TryOf` is the tool for a value from outside: `Order.Keys.TryOf(id, number, out var key)` returns false instead. |
| A key whose value holds the delimiter: `$"appointment#{key}"` over `a#b`. | `[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]` with `[KeyOnly] public required string Key { get; init; }`, unless the stored items hold a `Key` attribute. See [Adopting an existing table](adopting.md#keys-that-hold-your-delimiter). |
| `QueryOperator.Between` over two key strings. | `Keys.Sk.Between(from, to)` includes `to`, and `Keys.Sk.Range(from, to)` leaves it out, both from values and not from strings. `Range` exists only when something follows the field in the template. A key that ends in the field takes `Between` with `to` one step lower, such as `to.AddMilliseconds(-1)` under `{At:ms}`. |
| An `Expression` with `#names` and `:values`, or a `ScanCondition`. | `Order.Attrs.Total.Gt(100m)`, which is typed. |
| A `SaveAsync` that may create the item, which stores every property. | `Update` with `.StoreKeyInputs()`, see [PutItem](commands.md#putitem). |
| A loop that reads, writes and catches `ConditionalCheckFailedException`. | `RetryOnConflictAsync`, see [Retry on a conflict](commands.md#retry-on-a-conflict). |
| Working out in a `catch` which item failed. | `ConditionalCheckFailedException.Key`, see [Conditions](commands.md#conditions). |
| `DescribeTable`, `CreateTable`, a catch of `ResourceInUseException`, a wait. | `carts.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct)`. `.Stream(StreamViewType.NewImage)` for a table it creates and `.TimeToLive(Cart.Attrs.ExpiresAt)` go on the same chain. For a table that lacks an index, see [Adding an index](tables.md#adding-an-index). |
| Credentials from `AWSSDK.Core`, with IRSA on EKS. | `new DynamoDbClient()`, which reads the same [chain](getting-started.md#credentials). `AWS_ROLE_SESSION_NAME` names the session, which is otherwise `dynasharp-` and the Unix time. |
| A region found in EC2 instance metadata. | `AWS_REGION`, or `Region` in the options. DynaSharp does not ask the metadata service, see [Region](getting-started.md#region). |
| A mock of `IDynamoDBContext` or `IAmazonDynamoDB`. | `StubClient.Create(stub)` over a `StubHandler`, which fakes the wire, and `StubSts` for the way through STS. See [Testing](testing.md). |

## From EfficientDynamoDb

DynaSharp is the successor of EfficientDynamoDb, and keeps its aim: UTF-8 on the wire, little allocation, no AWS SDK. What changes is that the mapping is generated at build time instead of found by reflection, so Native AOT works, and that keys are templates the generator fills instead of strings you put together.

| What you wrote | What to write |
|---|---|
| `[DynamoDbTable("Shop")]` on the class | The name where the table is opened, `client.Table<Order>("Shop")`. An entity is not bound to one table. |
| `[DynamoDbProperty("pk", DynamoDbAttributeType.PartitionKey)]` on a string that the code fills | `[PartitionKey("CUSTOMER#{CustomerId}")]` on the type, a template that the generator fills from properties, or `[PartitionKey(AttributeName = "pk")]` on a property, a plain key. The same for `[SortKey]`. |
| `[DynamoDbProperty("total")]` | `[DynamoDbAttribute("total")]`, or nothing when the name is that of the property. |
| A `DdbConverter<T>` under `[DynamoDbConverter(typeof(...))]` | An `IDynamoDbConverter<T>`, whose members are static, under `[DynamoDbConverter(typeof(...))]` of `DynaSharp.Mapping`. See [Converters](entities.md#converters). |
| `[DynamoDbVersion]` | `[Version]`, see [Optimistic locking](commands.md#optimistic-locking). |
| `Condition<Order>.On(x => x.Status).EqualTo("Placed")` | `Order.Attrs.Status.Eq(OrderStatus.Placed)`, see [Expressions](expressions.md). |
| `UpdateItem<Order>().WithPrimaryKey(pk, sk).On(x => x.Total).Assign(10m).ExecuteAsync()` | `orders.Update(Order.Keys.Of(id, number)).Set(Order.Attrs.Total, 10m).SendAsync(ct)`. |
| `TransactWrite().WithItems(Transact.PutItem(order), ...)` | `client.TransactWrite().Put(orders, order)`, with one step per action, see [Transactions and batches](transactions.md). |
| `context.LowLevel` with a `Document` | The request API of `DynaSharp.Requests`, or a `Table<Item>`, see [API levels](api-levels.md). |
| `DynamoDbContextConfig` with a `RegionEndpoint` and `AwsCredentials` | `DynamoDbClientOptions` with `Region` and `Credentials`, or nothing, and the environment, see [Getting started](getting-started.md#creating-the-client). |

The stored formats of the two clients differ in places. Capture items that EfficientDynamoDb wrote and compare them, attribute by attribute, with what `ToRequestJson()` of DynaSharp sends, as for the AWS SDK above. A converter covers each difference, and the [rules for a table that exists](adopting.md) cover items without `_type` or a version.
