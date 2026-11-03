# Entities and keys

An entity is a C# type that maps to an item. The source generator reads its attributes and writes the code that reads and writes the item, plus `Keys`, which builds and parses its keys, and `Attrs`, a typed handle per attribute for conditions, filters and updates.

## Defining an entity

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

`{Number:d6}` pads to six digits, so that keys sort by number. A timestamp in a template is in UTC, and a bare `{PlacedAt}` is `ticks`: the seven fraction digits of the attribute, so a key built from a value that was read back finds its item, and a read can restore the property from the key. Every other format cuts the value off, and only `ticks` keeps all of it. For `PlacedAt` at 14:34:56.7890123 +02:00 on 2026-10-31:

| Where | Text |
|---|---|
| The attribute `PlacedAt`, round-trip form `O` in UTC | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt}` or `{PlacedAt:ticks}` | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt:us}` | `2026-10-31T12:34:56.789012Z` |
| A key with `{PlacedAt:ms}`, the form of keys the AWS SDK wrote | `2026-10-31T12:34:56.789Z` |
| A key with `{PlacedAt:s}` | `2026-10-31T12:34:56Z` |
| A key with `{PlacedAt:yyyy-MM-ddTHH:mm}`, `yyyy-MM-ddTHH`, `yyyy-MM-dd`, `yyyy-MM` or `yyyy` | `2026-10-31T12:34` to `2026` |

No format keeps the offset, and the attribute does not either. A date format needs a `DateTimeOffset` or a `DateTime`. A `DateTime` names an instant only with `DateTimeKind.Utc` or `Local`, so one of `DateTimeKind.Unspecified`, which is what `default(DateTime)` has, throws on a write, in a key and in any other attribute: an `ArgumentException` that names the property and its attribute. `SdkDateTimeConverter` takes such a time for local time instead, as the AWS SDK does. A `DateOnly` takes none, and a bare `{Day}` stores `2026-10-31`. Such an order and a customer of the same shop share a table and a partition:

| `pk` | `sk` | `bystatuspk` | `bystatussk` | `_type` | Other attributes |
|---|---|---|---|---|---|
| `CUSTOMER#alice` | `PROFILE` | | | `CUSTOMER` | `Id`, `Name`, `Email` |
| `CUSTOMER#alice` | `ORDER#000001` | `STATUS#Placed` | `2026-10-31T12:34:56.789Z` | `ORDER` | `CustomerId`, `Number`, `Status`, `PlacedAt`, `Total` |

Those are the default names. The inputs of a key are stored as attributes of their own too, unless `[KeyOnly]` says that the key is enough. No attribute holds such a property, so it has no handle in `Attrs`.

The rules a template follows, and what the generator reports when one breaks them, are under [Key templates](diagnostics.md#key-templates) on the diagnostics page.

## Keys and Attrs

`Keys` builds a key from values and checks them at the call: `Order.Keys.Of("alice", 1)` is the key of the table, and `Order.Keys.Pk.Of("alice")` the partition key alone, and `Order.Keys.Sk` holds the conditions on the sort key of a query, such as `BeginsWith`, `Between` and `Ge`. `Order.Keys.Of` throws `KeyFormatException` for a value that holds a delimiter of its template, and `Order.Keys.TryOf` returns false instead, which suits a value from outside. `Parse` reads the values back out of a stored key.

`Attrs` holds a handle per stored property, such as `Order.Attrs.Total`. A handle knows the name of the attribute and its converter, so `Order.Attrs.Total.Gt(100m)` is a typed condition and comparing a total with a string does not compile. [Expressions](expressions.md) says what a handle can do.

## Project defaults

What is the same on every type can be said once for the project. This stores `CustomerId` as `customerId`, leaves empty strings and collections out of an item, lets an entity without `[Discriminator]` go without one, and stores every `DateTimeOffset` as epoch seconds, also in a list:

```csharp
using DynaSharp.Mapping;
using DynaSharp.Serialization;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, OmitEmpty = true, DiscriminatorOptOut = true)]
[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(EpochSecondsConverter))]
```

An attribute on a type or a property still wins. A stored attribute takes its name from its property, so changing `Naming` renames stored attributes, and so does renaming a property: a rename refactoring compiles, items written before keep the old name, and a read takes the new one for missing. A property whose name may change takes `[DynamoDbAttribute]`. `pk`, `sk` and `_type` stay as they are.

## Computed index keys

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

## Sparse indexes

An index can leave items out. With `Sparse = true`, an item with a null input writes no key of that index, so the index holds only the others. A nullable input needs it.

```csharp
[GlobalIndexKey("ByTracking", PartitionKey = "TRACK#{TrackingNumber}", Sparse = true)]
public partial record Parcel(string Id, string? TrackingNumber);
```

## Converters

`[DynamoDbConverter]` puts an `IDynamoDbConverter<T>` on a property, and [the migration page](migrating.md#what-changes-stored-data) has examples. A converter that answers `CountsAsMissing` says that a stored value counts as if the attribute were not there, so the rest of the item reads.

An enum needs no converter for that. With `[DynamoDbEnum]`, a name it does not have, such as one that a newer version of a service stores during a rollout, keeps the initial value of the property, which is null for a nullable property without an initializer. Writes store the same names as without the attribute. A `required` property still throws, and so does an element of a list, a set or a map, which would otherwise drop out of the collection and be gone after the next `Put`.

```csharp
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
public enum Carrier { Unknown, Post, Courier }

[DynamoDbDocument]
public partial record Delivery
{
    public Carrier Carrier { get; init; } = Carrier.Unknown;
}
```

`[assembly: DynamoDbDefaultConverter]` gives a type a converter for the whole project, as under [Project defaults](#project-defaults).

Some converters ship in `DynaSharp.Serialization` and are opt-in, through either attribute, because each stores another form than the default:

| Converter | Type | Stored as |
|---|---|---|
| `EpochSecondsConverter` | `DateTimeOffset` | A number of seconds since 1970, `{"N":"1793448000"}`, the form time to live needs |
| `SetArrayConverter<T, TElement>` | `T[]` | A set, `{"SS":["a","b"]}`, where an array is a list by default |
| `BoolAsNumberConverter` | `bool` | `{"N":"1"}` and `{"N":"0"}`, as the AWS SDK stores it in its conversion V1 |
| `SdkDateTimeConverter` | `DateTime` | Three fraction digits in UTC, `{"S":"2026-10-31T12:00:00.123Z"}`, with the kinds as the AWS SDK takes them |
| `OffsetTimestampConverter` | `DateTimeOffset` | The time with its offset, `{"S":"2026-11-20T11:00:00.0000000+01:00"}`, which does not sort chronologically across offsets |
| `EmptyStringAsMissingConverter<T, TConverter>` | any | What `TConverter` stores, and a stored `{"S":""}` reads as a missing attribute |

[The migration page](migrating.md#what-changes-stored-data) says when each fits, and which built-in converters a converter of your own may call.
