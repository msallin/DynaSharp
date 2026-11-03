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

`{Number:d6}` pads to six digits, so that keys sort by number. A timestamp in a template is in UTC and keeps milliseconds unless it says otherwise: `{PlacedAt:ms}` and a bare `{PlacedAt}` both store `2026-10-31T12:34:56.789Z`. `{PlacedAt:ticks}` keeps seven digits, and only that format keeps the whole value. A date format needs a `DateTimeOffset` or a `DateTime`. A `DateOnly` takes none, and a bare `{Day}` stores `2026-10-31`. Such an order and a customer of the same shop share a table and a partition:

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

[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, OmitEmpty = true, NoDiscriminator = true)]
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
