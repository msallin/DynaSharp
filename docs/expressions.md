# Expressions

Conditions, filters, updates and projections are DynamoDB expressions. DynaSharp writes them for you from typed handles, and takes the grammar as text where a handle has no method for it. Either way every name and value becomes a placeholder, which `ToRequestJson()` shows.

`orders` and `shop` are the tables of [the shop](getting-started.md#the-shop).

## Typed handles

`Attrs` holds a handle per stored property of an entity, with the type of the property. A handle compares with `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge` and `Between`, asks for `Exists()` and `NotExists()`, or `NotExistsOrLt(v)` and `NotExistsOrLe(v)` for an attribute that is missing or below a value (see [Events that arrive out of order](patterns-writes.md#events-that-arrive-out-of-order)), takes `BeginsWith` where the stored value is a string or a binary, and `Contains` on a set, a list or a string. Conditions compose with `And`, `Or` and `Negate`, and allocate nothing.

```csharp
var open = orders.Query(Order.Keys.Pk.Of("alice"))
    .Filter(Order.Attrs.Total.Between(10m, 100m).And(Order.Attrs.Status.Eq(OrderStatus.Placed).Or(Order.Attrs.Status.Eq(OrderStatus.Paid))))
    .ItemsAsync(ct);
```

A chain of one operator renders as one group, `(a OR b OR c)`, and conditions added one by one as `(a) AND (b) AND (c)`. `BeginsWith` on a number, a boolean or a timestamp stored as a number does not compile (`CS0619`), as DynamoDB finds nothing there. An enum, a `Guid` or a date is stored as a string and takes it.

`Contains` takes an element of the type of a set or a list, as in `Post.Attrs.Tags.Contains("gift")` for a `HashSet<string> Tags`, and a substring on a string. It does not compile on a number, a boolean or anything else that holds no elements. An attribute that does not exist holds nothing, so `Post.Attrs.Tags.Contains("gift").Negate()` holds on an item without tags. `size` has no method: it stays in the [grammar](#the-grammar-as-text).

`At` walks into a nested document, as in `Customer.Attrs.ShipTo.At(Address.Attrs.City)`, and the result is a handle of its own, for a condition or an update.

## Names without a model

`Expr.Attr("status")` is a handle of any attribute by its path, for a `Table<Item>` or an attribute outside the entity. A path is names joined by dots with list indexes in brackets, and a dot always separates. `Expr.Attr<T>(path)` is a number handle, for `Return(...)` and a typed `Add`. With `using static DynaSharp.Expr;` at the top of the file it reads `Attr("status")`.

```csharp
await shop.Update("CUSTOMER#alice", "PROFILE")
    .Set("City", "Bern")
    .If(Attr("OrderCount").Gt(0))
    .SendAsync(ct);
```

`Condition.Of(...)` puts a condition on the heap, so that a field can keep it, and `&` joins it with another.

## The grammar as text

Interpolation covers the rest of the grammar: `size`, `contains`, `attribute_type`, `list_append` and arithmetic in `SET`. In an interpolated expression such as `$"{Attr("status")} = {"open"}"`, the text is DynamoDB's grammar, an attribute in a hole stands for its name, and anything else in a hole is a value. DynaSharp generates every placeholder.

```csharp
await shop.Update("CUSTOMER#alice", "PROFILE")
    .Set($"{Attr("OrderCount")} = {Attr("OrderCount")} + {1}")
    .If($"attribute_exists({Attr("pk")}) AND size({Attr("Interests")}) < {10}")
    .SendAsync(ct);
```

A value in a hole is an `AttributeValue`, a number or `handle.Value(x)`, which writes `x` through the converter of the handle. A string in a hole is a value too, so a name needs `Attr`. Every name becomes a placeholder and equal values share one, so reserved words need no care in a hole. A name written into the text itself, as in `.Filter($"Total > {100}")`, is taken as it is, so DynamoDB rejects a reserved word such as `Value` there, and no name in the text is checked against the attributes of the entity.

## Projections

`Project(...)` reads only the attributes it names, as handles or as paths, and a second call adds to the first. It saves bandwidth, not capacity: DynamoDB charges for the whole item. A typed read needs the keys and the discriminator, which join the projection on their own, and every other attribute the entity requires, so a projection suits a `Table<Item>` most:

```csharp
Item? contact = await shop.Get("CUSTOMER#alice", "PROFILE").Project("Name", "Email").ItemAsync(ct);
```

`Select(handle)` reads a single attribute of an entity as its value, see [What a call returns](commands.md#what-a-call-returns).

## What the build checks

The analyzers read a constant expression as DynamoDB would: a string where the grammar wants a name, placeholders of your own, a broken grammar, a path that names nothing on the type or is no path, a handle of another type, and `begins_with` or `contains` on a number. [Expressions](diagnostics.md#expressions) on the diagnostics page lists the rules, `DYNA210` to `DYNA216`, and what to write instead.
