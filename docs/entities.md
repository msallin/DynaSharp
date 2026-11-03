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

`{Number:d6}` pads to six digits, so that keys sort by number. A timestamp in a template is in UTC, and a bare `{PlacedAt}` is `ticks`: the seven fraction digits of the attribute, so a key built from a value that was read back finds its item, and a read can restore the property from the key. Every other format cuts the value off, and only `ticks` keeps all of it. For `PlacedAt` at 14:34:56.7890123 +02:00 on 2026-10-31:

| Where | Text |
|---|---|
| The attribute `PlacedAt`, round-trip form `O` in UTC | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt}` or `{PlacedAt:ticks}` | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt:us}` | `2026-10-31T12:34:56.789012Z` |
| A key with `{PlacedAt:ms}`, the form of keys the AWS SDK wrote | `2026-10-31T12:34:56.789Z` |
| A key with `{PlacedAt:s}` | `2026-10-31T12:34:56Z` |
| A key with `{PlacedAt:yyyy-MM-ddTHH:mm}`, `yyyy-MM-ddTHH`, `yyyy-MM-dd`, `yyyy-MM` or `yyyy` | `2026-10-31T12:34` to `2026` |

No format keeps the offset, and the attribute does not either. A date format needs a `DateTimeOffset` or a `DateTime`. A `DateTime` names an instant only with `DateTimeKind.Utc` or `Local`, so one of `DateTimeKind.Unspecified`, which is what `default(DateTime)` has, throws on a write, in a key and in any other attribute: an `ArgumentException` that names the property and its attribute. An unset `DateTime` is the likely cause, so set it to a UTC time first. `SdkDateTimeConverter` takes such a time for local time instead, as the AWS SDK does, which fits only times that the SDK wrote. A `DateOnly` takes none, and a bare `{Day}` stores `2026-10-31`. Such an order and a customer of the same shop share a table and a partition:

| `pk` | `sk` | `bystatuspk` | `bystatussk` | `_type` | Other attributes |
|---|---|---|---|---|---|
| `CUSTOMER#alice` | `PROFILE` | | | `CUSTOMER` | `Id`, `Name`, `Email` |
| `CUSTOMER#alice` | `ORDER#000001` | `STATUS#Placed` | `2026-10-31T12:34:56.7890123Z` | `ORDER` | `CustomerId`, `Number`, `Status`, `PlacedAt`, `Total` |

Those are the default names. A `Put` stores the inputs of a key as attributes of their own too, unless `[KeyOnly]` says that the key is enough: no attribute holds such a property, so it has no handle in `Attrs`. An `Update` writes no input of the key of the table unless `StoreKeyInputs()` asks, so an item it created holds the key and not those attributes, and a read of the entity restores them from the key.

The rules a template follows, and what the generator reports when one breaks them, are under [Key templates](diagnostics.md#key-templates) on the diagnostics page.

## Keys and Attrs

`Keys` builds a key from values and checks them at the call: `Order.Keys.Of("alice", 1)` is the key of the table, and `Order.Keys.Pk.Of("alice")` the partition key alone, and `Order.Keys.Sk` holds the conditions on the sort key of a query, such as `BeginsWith`, `Between` and `Ge`. `Order.Keys.Of` throws `KeyFormatException` for a value that holds a delimiter of its template, and `Order.Keys.TryOf` returns false instead, which suits a value from outside. `Parse` reads the values back out of a stored key.

`Attrs` holds a handle per stored property, such as `Order.Attrs.Total`. A handle knows the name of the attribute and its converter, so `Order.Attrs.Total.Gt(100m)` is a typed condition and comparing a total with a string does not compile. [Expressions](expressions.md) says what a handle can do.

## Documents

A `[DynamoDbDocument]` type is stored as a map inside an item: it has no keys and no discriminator, and it is `partial` as an entity is. It is the way to nest a type without a converter, alone, in a list or as the values of a dictionary. The shop keeps an `Address` in `Customer.ShipTo` and a list of `OrderLine` in `Order.Lines`, and a handle reaches inside through `.At(...)`: `Customer.Attrs.ShipTo.At(Address.Attrs.City)`.

```csharp
[DynamoDbDocument]
public partial record Dimensions(decimal Width, decimal Height, decimal Depth)
{
    public decimal Volume => Width * Height * Depth;
}
```

A document stores what an entity stores: every public property that can be read and that has a setter or a matching constructor parameter, named and converted by the same rules, `[OmitEmpty]` and `[DynamoDbIgnore]` included, and a null is left out of the map. `new Dimensions(10, 2, 3)` is `{"M":{"Width":{"N":"10"},"Height":{"N":"2"},"Depth":{"N":"3"}}}`.

- **Computed properties.** A get-only property without a constructor parameter, such as `Volume`, is not stored, and the getter computes it again after a read. `[DynamoDbAttribute]` on it stores it, written and never read.
- **Constructors.** A read builds a positional record through its constructor, whose parameters are named after its properties, and `[DynamoDbConstructor]` picks the constructor where more than one could be used (`DYNA005`).
- **Nesting.** A document can hold documents, and lists and dictionaries of them. An entity in a property is stored as a map too, with its keys and its discriminator, which `DYNA031` reports on a property of an entity.
- **What only an entity has.** A key, an index, a discriminator, `[KeyOnly]`, `[Version]` and `[ComputedFrom]` on a document are `DYNA012`.

## Stored types

The built-in converters store these types, and anything else takes a converter of its own (`DYNA003`):

| Type | Stored as |
|---|---|
| `string` | `S` |
| `int`, `long`, `decimal`, `double` and every other number type, `Int128`, `UInt128`, `Half` and `BigInteger` too | `N`, as invariant text that reads back to the same value, so a `decimal` keeps its digits and its scale |
| `bool` | `BOOL` |
| `Guid` | `S`, lowercase with hyphens: `0f8fad5b-d9cb-469f-a165-70867728950e` |
| `DateTimeOffset` | `S`, the instant in UTC with seven fraction digits: `2026-10-31T12:00:00.0000000Z`. The offset is dropped, so `14:00+02:00` reads back as `12:00` with offset zero, and the text sorts chronologically |
| `DateTime` | `S`, in the form of `DateTimeOffset`. `DateTimeKind.Local` is converted to UTC, and `DateTimeKind.Unspecified` throws on a write |
| `DateOnly` | `S`: `2026-10-31` |
| `TimeOnly` | `S`, with seven fraction digits: `12:00:00.0000000` |
| `TimeSpan` | `S`: `1.02:03:04.5000000`, which does not sort like the value |
| `byte[]`, `ReadOnlyMemory<byte>` | `B` |
| An enum | `S`, the name of the member, and `N` for a `[Flags]` enum |
| `AttributeValue` | What it holds, of any type |
| `List<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]` | `L`, each element in the form of `T` |
| `HashSet<T>`, `IReadOnlySet<T>` of strings, numbers, `byte[]`, `Guid` or an enum | `SS`, `NS` or `BS`. An empty set is left out, because DynamoDB refuses one, and reads as empty |
| `Dictionary<string, T>`, `IReadOnlyDictionary<string, T>` | `M`, an attribute per key |
| A `[DynamoDbDocument]` type | `M` |
| `T?` of any of these | The form of `T`, and a null property is left out of the item |

## Project defaults

What is the same on every type can be said once for the project. This stores `CustomerId` as `customerId`, and leaves empty strings and collections out of an item:

```csharp
using DynaSharp.Mapping;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, OmitEmpty = true)]
```

`DiscriminatorOptOut = true` lets an entity without `[Discriminator]` go without one, which suits a project whose tables other code wrote, see [Items without a discriminator](adopting.md#items-without-a-discriminator). `[assembly: DynamoDbDefaultConverter]` gives a type a converter for the whole project, see [Converters](#converters).

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

## Local indexes

A local index sorts the items of a partition another way: it has the partition key of the table and a sort key of its own. `[LocalIndexKey]` takes the name of the index and the template of that sort key, and needs an entity with a sort key.

```csharp
[DynamoDbEntity]
[Discriminator("TICKET")]
[PartitionKey("PROJECT#{Project}")]
[SortKey("TICKET#{Number:d6}")]
[LocalIndexKey("ByPriority", "{Priority:d2}#{Number:d6}")]
public partial record Ticket
{
    public required string Project { get; init; }
    public required int Number { get; init; }
    public required int Priority { get; init; }
}
```

An index is read through `Index(...)`, as a global one is, with `Keys.Pk` of the table for the partition and `Keys.<Index>Sk` for the sort key:

```csharp
var urgent = client.Table<Ticket>("Tickets").Index(Ticket.Indexes.ByPriority)
    .Query(Ticket.Keys.Pk.Of("apollo"), Ticket.Keys.ByPrioritySk.BeginsWith(1))
    .Consistent()
    .ItemsAsync(ct);
```

- **Created with the table.** DynamoDB creates a local index with its table or never, so `CreateTable()` creates it, and a table that exists cannot get one. It needs no wait.
- **Consistent reads.** `Consistent()` works on a local index, which a global index refuses.
- **Attributes.** Its sort key is the attribute `byprioritysk` by default, the name of the index in lower case with `sk`, and `SortKeyAttributeName` names another. `Sparse = true` leaves items with a null input out, as on a global index.
- **Size.** A table with a local index holds at most 10 GB per partition, and a write beyond that throws `ItemCollectionSizeLimitExceededException`.

## Converters

`[DynamoDbConverter]` puts an `IDynamoDbConverter<T>` on a property, and [the migration page](migrating.md#what-changes-stored-data) has examples. A converter that answers `CountsAsMissing` says that a stored value counts as if the attribute were not there, so the rest of the item reads.

An enum needs no converter for that. With `[DynamoDbEnum]`, a name it does not have, such as one that a newer version of a service stores during a rollout, keeps the initial value of the property, which is null for a nullable property without an initializer. Writes store the same names as without the attribute, so the next `Put` of the entity stores that initial value over the name it did not know. That suits only an enum whose initial value is a safe "unknown": with `Scheduled` as the initial value, an item that a newer version cancelled under a name this one lacks reads as `Scheduled`, and the next `Put` reopens it. A `required` property still throws, and so does an element of a list, a set or a map, which would otherwise drop out of the collection and be gone after the next `Put`. The attribute goes on the enum type, so an enum of another assembly that does not carry it needs a converter of its own, one that answers `CountsAsMissing`.

```csharp
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
public enum Carrier { Unknown, Post, Courier }

[DynamoDbDocument]
public partial record Delivery
{
    public Carrier Carrier { get; init; } = Carrier.Unknown;
}
```

`[assembly: DynamoDbDefaultConverter]` gives a type a converter for the whole project: `[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(EpochSecondsConverter))]` stores every `DateTimeOffset` as epoch seconds, also in a list.

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
