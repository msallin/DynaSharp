# Generated code

The source generator adds members to every `partial` type with `[DynamoDbEntity]` or `[DynamoDbDocument]`. They are generated into your assembly, so the API reference cannot show them: this page does. The examples use `Order` of the [sample](https://github.com/msallin/DynaSharp/blob/main/samples/DynaSharp.Sample/ShopModel.cs):

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
| `Order.Keys` | Every entity | `Of` and `TryOf` for the key of the table, and a builder per key, see [Keys](#keys) |
| `Order.Indexes` | An entity that declares an index | The name of each index as a `const string`, see [Indexes](#indexes) |

The code that reads and writes the item implements `IDynamoDbEntity<Order>` explicitly, so it is not a member you can call. The converter of an enum is a `file` type, and the structs a handle names to reach it are hidden from IntelliSense. None of these is written by hand. `Keys`, `Attrs` and `Indexes` are reserved member names on every mapped type (`DYNA014`), and a type that inherits from an entity hides the classes of its base with its own.

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

`Order.Keys.Of(customerId, number)` takes the placeholders of the partition key and then of the sort key of the table, in template order, each with the type of its property. A property that both keys use is one parameter. It returns `Order.Keys.PrimaryKey`, a readonly struct that implements `IKey<Order>` and holds the values, not the text: no string is made until a command writes the key. A key of another entity does not compile where `Get`, `Delete` or `Query` take this one.

`Of` checks the values at the call, as `Format` does, and throws `KeyFormatException` for one that cannot be part of the key: empty, holding a delimiter of its template, or out of its format, such as `-1` or `1000000` under `{Number:d6}`. The exception names the placeholder and the template, never the value.

`TryOf(..., out key)` is the same check without an exception, for a value from outside. It exists only where a key of the table can turn a value away. `Guid`, `DateOnly` and `DateTimeOffset` fit every template, so a key made of those alone has no `TryOf`. It answers for the key of the table only: a template of an index can still refuse the value when a `Put` writes it. The `out` parameter is `key`, or `key_` when a placeholder is called `Key`. A key of literals alone, such as `[SortKey("PROFILE")]` beside `[PartitionKey("CONFIG")]`, is `Keys.Of()` without parameters.

```csharp
if (!Order.Keys.TryOf(after, 1, out var first))
{
    Console.WriteLine("Not a customer id.");
}
```

### Builders

Each key has a static class under `Keys`:

| Key | Builder |
|---|---|
| Partition key of the table | `Keys.Pk` |
| Sort key of the table | `Keys.Sk` |
| Global index | `Keys.ByStatusPk`, and `Keys.ByStatusSk` if it has a sort key |
| Local index | `Keys.<Index>Sk`. Its partition key is the one of the table, so `Keys.Pk` builds it |

The index part is the name of the index in Pascal case: `orders-by_date` gives `OrdersByDate`, `GSI1` gives `Gsi1`.

### A partition key of a template

| Member | Exists | Gives |
|---|---|---|
| `Format(values)` | Always | The key as stored: `Pk.Format("alice")` is `CUSTOMER#alice` |
| `Of(values)` | Always | A `PartitionKey<Order>` for `Query`, which remembers the attribute it was built for. A key built for the table does not fit a query of `ByStatus`, and `Query` throws `ArgumentException` naming the builder to use |
| `TryOf(values, out key)` | Where the template can turn a value away | `false` instead of `KeyFormatException`, at the cost of `Of` |
| `Parse(text)`, `Parse(utf8)`, `TryParse(text, out parts)`, `TryParse(utf8, out parts)` | Always | `PkParts`, a record struct with a property per placeholder. `Parse` succeeds exactly when `Format` of the result gives the text back |

### A sort key of a template

A sort key has no `Of`: its builder makes conditions for `Query`, and `Keys.Of` makes the key of one item.

| Member | Exists | Gives |
|---|---|---|
| `Format(values)` | Always | The key as stored: `Sk.Format(42)` is `ORDER#000042` |
| `Eq(all values)` | Always | `= Format(...)`, one item of the partition |
| `Prefix(leading...)`, `UpperBound(leading...)` | At every literal of the template: before the first placeholder, and after each placeholder that a literal follows. A template of one literal has none | The text up to and with that delimiter, and the same plus U+10FFFF, which sorts above every key under it |
| `BeginsWith(leading...)` | Where `Prefix` is | `begins_with` on that prefix. `BeginsWith()` is every key of the template. `BeginsWith(value)` fills the fields before it as the template formats them, so a value keeps only what its format holds: under `{At:yyyy-MM}#{Id}` a day is its month |
| `PrefixPartial(values)` | When the last placeholder is a `string` | A prefix that ends inside that value, for autocomplete. It matches longer values, which `BeginsWith` does not |
| `Between(leading..., from, to)` | On a field whose stored text sorts like its value | Every key from `from` to `to`, both included, with the fields before it equal |
| `Range(leading..., from, to)` | Where `Between` is, and something follows the field in the template | The same without the keys at `to`, for `[from, to)` |
| `Ge`, `Gt`, `Le`, `Lt(values...)` | When every field through the last argument sorts like its value | One end. They go on past the template, so `Ge` also reaches what sorts after it in the partition |
| `Parse`, `TryParse` | Always | `SkParts`, as for a partition key |

A field sorts like its value when its width is fixed (`d6`, a timestamp, a `Guid`, a date) or when it is a string that ends the key or stands before a space. Plain digits sort `10` before `9`, and an enum sorts by name, so those get no range. A string before another delimiter is reported as `DYNA115`. What is not generated would be wrong, so it does not compile.

For the sample, `Order.Keys.Sk` of `ORDER#{Number:d6}` has `Format`, `Eq`, `Prefix()`, `UpperBound()`, `BeginsWith()`, `Between`, `Ge`, `Gt`, `Le`, `Lt` and the parsers. It has no `Range`, because nothing follows `Number`, and no `PrefixPartial`, because `Number` is no string. `Order.Keys.ByStatusSk` of `{PlacedAt}` has no prefix at all, since it has no literal, and its ranges compare whole keys. [Queries and scans](queries.md#sort-key-conditions) shows them in use. The bounds they write:

| Condition | Key condition | A key that ends in the field |
|---|---|---|
| `Eq(values)` | `= Format(...)` | The same |
| `Between(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND UpperBound(leading..., to)` | `BETWEEN Format(leading..., from) AND Format(leading..., to)` |
| `Range(leading..., from, to)` | `BETWEEN Prefix(leading..., from) AND Prefix(leading..., to)` | Not generated |
| `Ge(x...)` | `>= Prefix(x...)` | `>= Format(x...)` |
| `Gt(x...)` | `> UpperBound(x...)` | `> Format(x...)` |
| `Le(x...)` | `<= UpperBound(x...)` | `<= Format(x...)` |
| `Lt(x...)` | `< Prefix(x...)` | `< Format(x...)` |

A field that a bucket format cuts, such as a day under `{At:yyyy-MM-dd}`, compares as its bucket, so `Gt` of a day leaves out that whole day. A value that cannot be part of a key throws `KeyFormatException`, as `Format` does.

### A plain key

A key on a property, `[PartitionKey] public required string Sku`, has no template, and neither has a template of one bare placeholder over the attribute of its own property, such as `[SortKey("{At}", AttributeName = "At")]`. The attribute is the key, as its converter writes it, so a number stays a number:

| Member | Exists | Gives |
|---|---|---|
| `Of(value)` | Always | A `PartitionKey<T>` for a partition key, and the `AttributeValue` of a sort key for `SortKeyCondition` |
| `Format(value)` | When the key is stored as a string | The text |
| `Eq(value)` | On a sort key | `=` on the stored value |
| `Between`, `Ge`, `Gt`, `Le`, `Lt` | On a sort key that stores a number, binary, `string`, `Guid`, `DateTimeOffset`, `DateTime`, `DateOnly` or `TimeOnly` | The comparison of DynamoDB on the stored value |
| `BeginsWith(prefix)` | On a sort key of `string` | `begins_with` |

There is nothing to parse, no prefix, and no `TryOf`: no template turns a value away, and an empty string is DynamoDB's to reject.

## Attrs

`Order.Attrs.Total` is an `Attr<decimal, NumberConverter<decimal>>`: the name of the attribute and the converter of the property, which makes a condition, an update and a projection typed. There is one per stored attribute, on entities and on documents. The handle is named after the property, and holds the attribute name, so `[DynamoDbAttribute("ttl")] public DateTimeOffset? ExpiresAt` gives `Attrs.ExpiresAt` for the attribute `ttl`.

- The value type is the type without its `?`, which is what `Set` and the conditions take. The converter says the rest: a nullable property reads through `NullableHandleConverter` or `NullableReferenceHandleConverter`, `[OmitEmpty]` through `OmitEmptyConverter` or one of its kin for collections, and a `[Version]` with `VersionMissing.Allow` through one that reads a missing attribute as 0.
- A handle is `internal` unless the property, its type and its converters are public.
- A `[KeyOnly]` property has no attribute and no handle, and neither has an attribute whose name holds `.` or `[`, which a path would split. A raw path cannot address it either.
- A document has handles of its own, and `At` walks into one: `Customer.Attrs.ShipTo.At(Address.Attrs.City)`.

What a handle can do is on [Expressions](expressions.md).

## Indexes

`Order.Indexes.ByStatus` is the `const string` `"ByStatus"`, for `orders.Index(...)`, `shop.Index<Order>(...)` and the request API. The name of the constant follows the rule of the builders, so `GSI1` is `Indexes.Gsi1`. An entity without an index has no `Indexes`. A name that two indexes of one type would share is reported when the type compiles.
