# Entities and keys

An entity is a C# type that maps to an item. From its attributes the source generator writes the code that reads and writes the item, plus `Keys`, which builds and parses its keys, and `Attrs`, a typed handle per attribute for conditions, filters and updates.

## Defining an entity

Declare a `partial` type and say how it maps.

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

`{Number:d6}` pads to six digits, so keys sort by number. A timestamp in a template is in UTC. A bare `{PlacedAt}` means `ticks`, seven fraction digits like the attribute: the only format that keeps the whole value, so a key built from a value read back finds its item and a read can restore the property from the key. Others truncate. For `PlacedAt` at 14:34:56.7890123 +02:00 on 2026-10-31:

| Where | Text |
|---|---|
| The attribute `PlacedAt`, round-trip form `O` in UTC | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt}` or `{PlacedAt:ticks}` | `2026-10-31T12:34:56.7890123Z` |
| A key with `{PlacedAt:us}` | `2026-10-31T12:34:56.789012Z` |
| A key with `{PlacedAt:ms}`, the form of keys the AWS SDK wrote | `2026-10-31T12:34:56.789Z` |
| A key with `{PlacedAt:s}` | `2026-10-31T12:34:56Z` |
| A key with `{PlacedAt:yyyy-MM-ddTHH:mm}`, `yyyy-MM-ddTHH`, `yyyy-MM-dd`, `yyyy-MM` or `yyyy` | `2026-10-31T12:34` to `2026` |

Neither a key format nor the attribute keeps the offset. A date format needs a `DateTimeOffset` or a `DateTime`; a `DateOnly` takes none, and a bare `{Day}` stores `2026-10-31`.

> [!WARNING]
> A `DateTime` names an instant only with `DateTimeKind.Utc` or `Local`. `DateTimeKind.Unspecified`, which `default(DateTime)` has, throws an `ArgumentException` on a write, in a key or any other attribute, naming the property and its attribute. The likely cause is an unset `DateTime`: set it to a UTC time. `SdkDateTimeConverter` instead takes such a time as local time, as the AWS SDK does, which fits only times the SDK wrote.

Such an order and a customer share a table and a partition:

| `pk` | `sk` | `bystatuspk` | `bystatussk` | `_type` | Other attributes |
|---|---|---|---|---|---|
| `CUSTOMER#alice` | `PROFILE` | | | `CUSTOMER` | `Id`, `Name`, `Email` |
| `CUSTOMER#alice` | `ORDER#000001` | `STATUS#Placed` | `2026-10-31T12:34:56.7890123Z` | `ORDER` | `CustomerId`, `Number`, `Status`, `PlacedAt`, `Total` |

Those are the default names. Key inputs are stored as follows:

- **Put** also stores them as attributes, unless `[KeyOnly]` says the key is enough. Such a property has no attribute, so no handle in `Attrs`.
- **Update** writes no input of the table key unless `StoreKeyInputs()` asks. An item it created holds the key without those attributes, and a read restores them from the key.

The rules of a template, and what the generator reports when one is broken, are under [Key templates](diagnostics.md#key-templates).

## Keys and Attrs

`Keys` builds keys from values and checks them at the call: `Order.Keys.Of("alice", 1)` is the table key, `Order.Keys.Pk.Of("alice")` the partition key alone, and `Order.Keys.Sk` holds the sort key conditions of a query, such as `BeginsWith`, `Between` and `Ge`. `Attrs` holds a typed handle per stored property: `Order.Attrs.Total.Gt(100m)` compiles, a comparison with a string does not. [Generated code](generated-code.md) lists every member, including `TryOf` and `Parse`; [Expressions](expressions.md) says what a handle can do.

## Documents

A `[DynamoDbDocument]` type is a map inside an item, without keys or discriminator, and `partial` like an entity. It nests a type without a converter, alone, in a list or as dictionary values. A handle reaches inside with `.At(...)`: `Customer.Attrs.ShipTo.At(Address.Attrs.City)`.

```csharp
[DynamoDbDocument]
public partial record Dimensions(decimal Width, decimal Height, decimal Depth)
{
    public decimal Volume => Width * Height * Depth;
}
```

A document stores what an entity stores: every public readable property with a setter or a matching constructor parameter, named and converted by the same rules, `[OmitEmpty]` and `[DynamoDbIgnore]` included. A null is left out of the map. `new Dimensions(10, 2, 3)` is `{"M":{"Width":{"N":"10"},"Height":{"N":"2"},"Depth":{"N":"3"}}}`.

- **Computed properties.** A get-only property without a constructor parameter, such as `Volume`, is not stored; the getter computes it after a read. `[DynamoDbAttribute]` on it stores it, written and never read.
- **Constructors.** A read builds a positional record through its constructor, whose parameters are named after its properties. `[DynamoDbConstructor]` picks one where several could be used (`DYNA005`).
- **Nesting.** A document can hold documents, and lists and dictionaries of them. An entity in a property is stored as a map too, with its keys and discriminator; `DYNA031` reports that on a property of an entity.
- **What only an entity has.** A key, an index, a discriminator, `[KeyOnly]`, `[Version]` and `[ComputedFrom]` on a document are `DYNA012`.

## Stored types

The built-in converters store these types; anything else needs its own converter (`DYNA003`):

| Type | Stored as |
|---|---|
| `string` | `S` |
| `int`, `long`, `decimal`, `double` and every other number type, `Int128`, `UInt128`, `Half` and `BigInteger` too | `N`, as invariant text that reads back to the same value, so a `decimal` keeps its digits and scale |
| `bool` | `BOOL` |
| `Guid` | `S`, lowercase with hyphens: `0f8fad5b-d9cb-469f-a165-70867728950e` |
| `DateTimeOffset` | `S`, the instant in UTC with seven fraction digits: `2026-10-31T12:00:00.0000000Z`. The offset is dropped (`14:00+02:00` reads back as `12:00` with offset zero), and the text sorts chronologically |
| `DateTime` | `S`, as `DateTimeOffset`. `Local` is converted to UTC; `Unspecified` throws on a write, see above |
| `DateOnly` | `S`: `2026-10-31` |
| `TimeOnly` | `S`, with seven fraction digits: `12:00:00.0000000` |
| `TimeSpan` | `S`: `1.02:03:04.5000000`, which does not sort like the value |
| `byte[]`, `ReadOnlyMemory<byte>` | `B` |
| An enum | `S`, the member name; `N` for a `[Flags]` enum |
| `AttributeValue` | What it holds, of any type |
| `List<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]` | `L`, each element in the form of `T` |
| `HashSet<T>`, `IReadOnlySet<T>` of strings, numbers, `byte[]`, `Guid` or an enum | `SS`, `NS` or `BS`. An empty set is left out, as DynamoDB refuses one, and reads as empty. One in a list or a dictionary cannot be left out, so a write throws an `ArgumentException` that names the property |
| `Dictionary<string, T>`, `IReadOnlyDictionary<string, T>` | `M`, an attribute per key |
| A `[DynamoDbDocument]` type | `M` |
| `T?` of any of these | The form of `T`; a null property is left out of the item, and an item without the attribute reads as the initial value, null unless an initializer gives one (`DYNA035`) |

## Project defaults

Set once for the project what is the same on every type. This stores `CustomerId` as `customerId` and leaves empty strings and collections out of an item:

```csharp
using DynaSharp.Mapping;

[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, OmitEmpty = true)]
```

`DiscriminatorOptOut = true` lets an entity without `[Discriminator]` go without one, for tables other code wrote or a [table of one entity](tables.md#one-table-per-entity), see [Items without a discriminator](adopting.md#items-without-a-discriminator). `[assembly: DynamoDbDefaultConverter]` gives a type a converter for the whole project, see [Converters](#converters).

An attribute on a type or property still wins. A stored attribute is named after its property, so changing `Naming` or renaming a property renames stored attributes: the rename compiles, items written before keep the old name, and a read finds the new one missing. Give a property whose name may change a `[DynamoDbAttribute]`. `pk`, `sk` and `_type` stay as they are.

## Computed index keys

For a computed index key, say what the getter reads, so an update that changes it throws instead of leaving the index behind. A `Put` of the changed entity moves the key.

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

With `Sparse = true`, an item with a null input writes no key of that index, so the index holds only the others. A nullable input requires it.

```csharp
[GlobalIndexKey("ByTracking", PartitionKey = "TRACK#{TrackingNumber}", Sparse = true)]
public partial record Parcel(string Id, string? TrackingNumber);
```

An update that sets one key of a sparse index writes the other too, building it from the values it has, or throws naming the `Set` to add. A key that is one bare placeholder over a string, `Guid`, `DateOnly`, `ticks` timestamp or enum by name, such as `PartitionKey = "{Owner}"`, is the text of its attribute, so the update copies it from the item (`SET byownerpk = Owner`) and needs no value. An item without the attribute fails that update with `ValidationException`.

## Local indexes

A local index sorts a partition another way: the table's partition key with a sort key of its own. `[LocalIndexKey]` takes the index name and the sort key template, and needs an entity with a sort key.

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

Read it through `Index(...)` like a global one, with the table's `Keys.Pk` for the partition and `Keys.<Index>Sk` for the sort key:

```csharp
var urgent = client.Table<Ticket>("Tickets").Index(Ticket.Indexes.ByPriority)
    .Query(Ticket.Keys.Pk.Of("apollo"), Ticket.Keys.ByPrioritySk.BeginsWith(1))
    .Consistent()
    .ItemsAsync(ct);
```

- **Created with the table.** DynamoDB creates a local index with its table or never: `CreateTable()` creates it, an existing table cannot get one, and it needs no wait.
- **Consistent reads.** `Consistent()` works on a local index; a global index refuses it.
- **Attributes.** Its sort key is the attribute `byprioritysk` by default (index name in lower case plus `sk`); `SortKeyAttributeName` names another. `Sparse = true` works as on a global index.
- **Size.** A table with a local index holds at most 10 GB per partition; a write beyond throws `ItemCollectionSizeLimitExceededException`.

## Converters

`[DynamoDbConverter]` puts an `IDynamoDbConverter<T>` on a property; [the migration page](migrating.md#what-changes-stored-data) has examples. A converter that answers `CountsAsMissing` makes a stored value count as a missing attribute, so the rest of the item reads.

An enum needs no converter for that. With `[DynamoDbEnum]`, an unknown name, such as one a newer version of a service stores during a rollout, keeps the property's initial value (null for a nullable property without an initializer). Writes store names as without the attribute, so the next `Put` stores that initial value over the unknown name.

> [!WARNING]
> Use it only for an enum whose initial value is a safe "unknown". With `Scheduled` as the initial value, an item a newer version cancelled under a name this one lacks reads as `Scheduled`, and the next `Put` reopens it.

A `required` property still throws, and so does an element of a list, set or map, which would otherwise be lost on the next `Put`. The attribute goes on the enum type; an enum from another assembly needs a converter that answers `CountsAsMissing`.

```csharp
[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
public enum Carrier { Unknown, Post, Courier }

[DynamoDbDocument]
public partial record Delivery
{
    public Carrier Carrier { get; init; } = Carrier.Unknown;
}
```

`[assembly: DynamoDbDefaultConverter]` sets a type's converter for the whole project: `[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(EpochSecondsConverter))]` stores every `DateTimeOffset` as epoch seconds, also in a list.

These ship in `DynaSharp.Serialization` and are opt-in through either attribute, since each stores another form than the default:

| Converter | Type | Stored as |
|---|---|---|
| `EpochSecondsConverter` | `DateTimeOffset` | Seconds since 1970, `{"N":"1793448000"}`, the form time to live needs |
| `SetArrayConverter<T, TElement>` | `T[]` | A set, `{"SS":["a","b"]}`, where an array is a list by default |
| `BoolAsNumberConverter` | `bool` | `{"N":"1"}` and `{"N":"0"}`, as the AWS SDK stores it in conversion V1 |
| `SdkDateTimeConverter` | `DateTime` | Three fraction digits in UTC, `{"S":"2026-10-31T12:00:00.123Z"}`, taking the kinds as the AWS SDK does |
| `OffsetTimestampConverter` | `DateTimeOffset` | The time with its offset, `{"S":"2026-11-20T11:00:00.0000000+01:00"}`, which does not sort chronologically across offsets |
| `EmptyStringAsMissingConverter<T, TConverter>` | any | What `TConverter` stores; a stored `{"S":""}` reads as a missing attribute |

[The migration page](migrating.md#what-changes-stored-data) says when each fits, and which built-in converters your own may call.
