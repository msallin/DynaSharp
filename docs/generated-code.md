# Generated code

The source generator adds members to every `partial` type with `[DynamoDbEntity]` or `[DynamoDbDocument]`. They live in your assembly, so the API reference cannot show them. The examples use `Order` of the [sample](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/ShopModel.cs):

```text
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("ORDER#{Number:d6}")]
[GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
[GlobalIndexKey("ByTracking", PartitionKey = "TRACK#{TrackingNumber}", Sparse = true)]
```

## What a type gets

| Member | On | Holds |
|---|---|---|
| `Order.Attrs` | Every entity and document | A typed handle per stored attribute, see [Attrs](#attrs) |
| `Order.Keys` | Every entity | `Of` and `TryOf` for the table key, and a builder per key, see [Keys](#keys) |
| `Order.Indexes` | An entity that declares an index | Each index name as a `const string`, see [Indexes](#indexes) |
| `Order.TableName` | An entity that declares a table | The declared name as a `const string`, see [TableName](#tablename) |
| `DynamoDbEntities.IncludingAll()` | The assembly, once, when its entities share the keys of one table | An `Including<T>()` of every entity, see [DynamoDbEntities](#dynamodbentities) |

The code that reads and writes the item implements `IDynamoDbEntity<Order>` explicitly, so you cannot call it. An enum's converter is a `file` type, and the structs a handle names to reach it are hidden from IntelliSense. `Keys`, `Attrs` and `Indexes` are reserved member names on every mapped type, and `TableName` on an entity that declares a table (`DYNA014`); a type that inherits from an entity hides its base's members with its own.

```csharp
Order.Keys.PrimaryKey key = Order.Keys.Of("alice", 42);
PartitionKey<Order> partition = Order.Keys.Pk.Of("alice");
string text = Order.Keys.Pk.Format("alice");                           // CUSTOMER#alice
Order.Keys.SkParts parts = Order.Keys.Sk.Parse("ORDER#000042");        // parts.Number is 42
SortKeyCondition recent = Order.Keys.Sk.Ge(40);                         // ORDER#000040 and after
Attr<decimal, NumberConverter<decimal>> total = Order.Attrs.Total;
Index<Order> placed = orders.Index(Order.Indexes.ByStatus);             // the index "ByStatus"
```

## Keys

### Keys.Of and Keys.TryOf

`Order.Keys.Of(customerId, number)` takes the placeholders of the table's partition key, then its sort key, in template order, each typed as its property. A property both keys use is one parameter. It returns `Order.Keys.PrimaryKey`, a readonly struct implementing `IKey<Order>` that holds the values, not the text: no string is made until a command writes the key. Another entity's key does not compile where `Get`, `Delete` or `Query` take this one.

`Of` checks the values, as `Format` does, and throws `KeyFormatException` for one that cannot be part of the key: empty, holding a delimiter of its template, or outside its format, such as `-1` or `1000000` under `{Number:d6}`. The exception names the placeholder and template, never the value.

`TryOf(...)` is the same check without an exception, for a value from outside: it returns the key, or `null` for a value that `Of` throws for, so a read of a value from outside is one expression with `is { } key`. It exists only where the table key can refuse a value: `Guid`, `DateOnly` and `DateTimeOffset` fit every template, so a key of those alone has none. It checks the table key only; an index template can still refuse the value when a `Put` writes it. A key of literals alone, such as `[SortKey("PROFILE")]` beside `[PartitionKey("CONFIG")]`, is `Keys.Of()` without parameters.

```csharp
Order? placed = Order.Keys.TryOf(after, 1) is { } key ? await orders.Get(key).ItemAsync(ct) : null;
Order? latest = Order.Keys.Pk.TryOf(after) is { } partition ? await orders.Query(partition).Descending().ItemAsync(ct) : null;
```

A method that stops at a value that does not fit returns early:

```csharp
if (Order.Keys.TryOf(after, 1) is not { } first)
{
    Console.WriteLine("Not a customer id.");
    return;
}

Order? found = await orders.Get(first).ItemAsync(ct);
```

The key is a nullable struct, so the `null` costs nothing. `Keys` and every partition key builder that can refuse a value have a `TryOf`; a sort key builder makes conditions and has none.

### Builders

Each key has a static class under `Keys`:

| Key | Builder |
|---|---|
| Partition key of the table | `Keys.Pk` |
| Sort key of the table | `Keys.Sk` |
| Global index | `Keys.ByStatusPk`, and `Keys.ByStatusSk` if it has a sort key |
| Local index | `Keys.<Index>Sk`. Its partition key is the table's, so `Keys.Pk` builds it |

The index part is the index name in Pascal case: `orders-by_date` gives `OrdersByDate`, `GSI1` gives `Gsi1`.

### A partition key of a template

| Member | Exists | Gives |
|---|---|---|
| `Format(values)` | Always | The key as stored: `Pk.Format("alice")` is `CUSTOMER#alice` |
| `Of(values)` | Always | A `PartitionKey<Order>` for `Query`, bound to the attribute it was built for: a table key in a query of `ByStatus` throws `ArgumentException` naming the builder to use |
| `TryOf(values)` | Where the template can refuse a value | The key, or `null` instead of `KeyFormatException`, for `is { } key`, at the cost of `Of` |
| `Parse(text)`, `Parse(utf8)`, `TryParse(text, out parts)`, `TryParse(utf8, out parts)` | Always | `PkParts`, a record struct with a property per placeholder. `Parse` succeeds exactly when `Format` of the result gives the text back |

### A sort key of a template

A sort key has no `Of`: its builder makes conditions for `Query`; `Keys.Of` makes the key of one item.

| Member | Exists | Gives |
|---|---|---|
| `Format(values)` | Always | The key as stored: `Sk.Format(42)` is `ORDER#000042` |
| `Eq(all values)` | Always | `= Format(...)`, one item of the partition |
| `Prefix(leading...)`, `UpperBound(leading...)` | At every literal: before the first placeholder, and after each placeholder a literal follows. A template of one literal has none | The text through that delimiter, and the same plus U+10FFFF, which sorts above every key under it |
| `BeginsWith(leading...)` | Where `Prefix` is | `begins_with` on that prefix. `BeginsWith()` is every key of the template. `BeginsWith(value)` formats the fields as the template does, so a value keeps only what its format holds: under `{At:yyyy-MM}#{Id}` a day is its month |
| `PrefixPartial(values)` | When the last placeholder is a `string` | A prefix ending inside that value, for autocomplete. It matches longer values, which `BeginsWith` does not |
| `Between(leading..., from, to)` | On a field whose stored text sorts like its value | Every key from `from` to `to`, both included, with the fields before it equal |
| `Range(leading..., from, to)` | Where `Between` is, and something follows the field | The same without the keys at `to`, for `[from, to)` |
| `Ge`, `Gt`, `Le`, `Lt(values...)` | When every field through the last argument sorts like its value | One end, not bounded by the template: `Ge` also reaches what sorts after it in the partition |
| `Parse`, `TryParse` | Always | `SkParts`, as for a partition key |

A field sorts like its value when its width is fixed (`d6`, a timestamp, a `Guid`, a date) or it is a string that ends the key or precedes a space. Plain digits sort `10` before `9` and an enum sorts by name, so those get no range. A string before another delimiter is `DYNA115`. What is not generated would be wrong, so it does not compile.

For the sample, `Order.Keys.Sk` of `ORDER#{Number:d6}` has `Format`, `Eq`, `Prefix()`, `UpperBound()`, `BeginsWith()`, `Between`, `Ge`, `Gt`, `Le`, `Lt` and the parsers; no `Range`, as nothing follows `Number`, and no `PrefixPartial`, as `Number` is no string. `Order.Keys.ByStatusSk` of `{PlacedAt}` has no literal, so no prefix, and its ranges compare whole keys. See [Queries and scans](queries.md#sort-key-conditions). The bounds they write:

| Condition | Key condition | A key that ends in the field |
|---|---|---|
| `Eq(values)` | `= Format(...)` | The same |
| `Between(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND UpperBound(leading..., to)` | `BETWEEN Format(leading..., from) AND Format(leading..., to)` |
| `Range(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND Prefix(leading..., to)` | Not generated |
| `Ge(x...)` | `>= Prefix(x...)` | `>= Format(x...)` |
| `Gt(x...)` | `> UpperBound(x...)` | `> Format(x...)` |
| `Le(x...)` | `<= UpperBound(x...)` | `<= Format(x...)` |
| `Lt(x...)` | `< Prefix(x...)` | `< Format(x...)` |

A field a bucket format truncates, such as a day under `{At:yyyy-MM-dd}`, compares as its bucket, so `Gt` of a day leaves out that whole day. A value that cannot be part of a key throws `KeyFormatException`, as `Format` does.

### A plain key

A key on a property, `[PartitionKey] public required string Sku`, has no template, nor has a template of one bare placeholder over its own property's attribute, such as `[SortKey("{At}", AttributeName = "At")]`. The attribute is the key, as its converter writes it, so a number stays a number:

| Member | Exists | Gives |
|---|---|---|
| `Of(value)` | Always | A `PartitionKey<T>` for a partition key, and the `AttributeValue` of a sort key for `SortKeyCondition` |
| `Format(value)` | When the key is stored as a string | The text |
| `Eq(value)` | On a sort key | `=` on the stored value |
| `Between`, `Ge`, `Gt`, `Le`, `Lt` | On a sort key that stores a number, binary, `string`, `Guid`, `DateTimeOffset`, `DateTime`, `DateOnly` or `TimeOnly` | The comparison of DynamoDB on the stored value |
| `BeginsWith(prefix)` | On a sort key of `string` | `begins_with` |

There is nothing to parse, no prefix and no `TryOf`: no template refuses a value, and an empty string is DynamoDB's to reject.

## Attrs

`Order.Attrs.Total` is an `Attr<decimal, NumberConverter<decimal>>`: the attribute name and the property's converter, which type conditions, updates and projections. There is one per stored attribute, on entities and documents, named after the property: `[DynamoDbAttribute("ttl")] public DateTimeOffset? ExpiresAt` gives `Attrs.ExpiresAt` for the attribute `ttl`.

- The value type is the type without its `?`, which `Set` and the conditions take. The converter says the rest: a nullable property reads through `NullableHandleConverter` or `NullableReferenceHandleConverter`, `[OmitEmpty]` through `OmitEmptyConverter` or a collection kin, and a `[Version]` with `VersionMissing.Allow` through one that reads a missing attribute as 0.
- A handle is `internal` unless the property, its type and its converters are public.
- A `[KeyOnly]` property has no attribute and no handle; neither has an attribute whose name holds `.` or `[`, which a path would split, and a raw path cannot address it either.
- A document has its own handles, and `At` walks into one: `Customer.Attrs.ShipTo.At(Address.Attrs.City)`.

See [Expressions](expressions.md) for what a handle can do.

## Indexes

`Order.Indexes.ByStatus` is the `const string` `"ByStatus"`, for `orders.Index(...)`, `shop.Index<Order>(...)` and the request API. The constant is named like the builders, so `GSI1` is `Indexes.Gsi1`. An entity without an index has no `Indexes`. Two indexes of one type that would share a name are reported at compile time.

## TableName

`Order.TableName` is the `const string` `"Shop"`: the table the entity declares, with `[DynamoDbEntity(Table = "...")]` or through `[assembly: DynamoDbDefaults(Table = "...")]`, as written. An entity without a declared table has none. It is the name before [`TableNaming`](tables.md#a-table-per-environment): the name the client opens is `orders.Name`, of a table from `client.Table<Order>()`.

```csharp
Console.WriteLine($"{Order.TableName} opens as {client.Table<Order>().Name}");  // "Shop", then the name after TableNaming
```

It is a constant, so it fits where only one does, such as an attribute argument or a `case`, and a tool, a test or infrastructure code that needs the name reads it from the entity instead of repeating it.

## DynamoDbEntities

Beside the members of each type, an assembly whose entities all have the keys of one table gets one class, `internal` and in the namespace `DynaSharp`, so that a file with `using DynaSharp;` finds it. For the sample:

```text
namespace DynaSharp;

internal static class DynamoDbEntities
{
    public static CreateTableCommand IncludingAll(this CreateTableCommand command)
        => command.Including<Cart>().Including<Customer>().Including<EmailClaim>().Including<Order>().Including<Product>();
}
```

`orders.CreateTable().IncludingAll()` names every entity of the assembly; which assemblies get it, and what another assembly writes instead, is under [Every entity of an assembly](tables.md#every-entity-of-an-assembly). The file is `DynaSharp-DynamoDbEntities.g.cs`.
