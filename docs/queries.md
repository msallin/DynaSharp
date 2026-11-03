# Queries and scans

A query reads one partition, or a range of it, in the order of the sort key. A scan reads the whole table. Both end in `ItemsAsync`, which streams the items, and most reads also end in `PageAsync`, `PagesAsync` or `CountAsync`: `DYNA202` under [Commands](diagnostics.md#commands) says which read has which.

## Query

`ItemsAsync` follows pages for you, including pages a filter left empty. `Keys` builds the key, and `Attrs` makes the filter typed, so comparing a total with a string does not compile.

```csharp
var bigOrders = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Filter(Order.Attrs.Total.Gt(100m))
    .Descending()
    .ItemsAsync(ct);

await foreach (var order in bigOrders)
{
    Console.WriteLine(order.Number);
}
```

`BeginsWith(value)` fills the leading fields as the template formats them, so a day under `{At:yyyy-MM}` is the month.

A file that works on one entity can shorten this with `using Attrs = Shop.Order.Attrs;` at the top, and write `Attrs.Total.Gt(100m)`.

A filter can also be text, as in `.Filter($"Total > {100}")`. Text takes reserved words as they are, so DynamoDB rejects a name such as `Value`, where `Attrs` needs no care. No name in it is checked against the attributes of the entity, so a wrong one finds nothing.

`Limit(n)` is per page and counts what DynamoDB reads before the filter, so it is no cap on items. `Take(n)` is, and it stops reading once it has them:

```csharp
var firstTen = orders.Query(Order.Keys.Pk.Of("alice"))
    .Filter(Order.Attrs.Total.Gt(100m))
    .ItemsAsync(ct)
    .Take(10);
```

## Indexes

An index is read through its own type, which has no writes:

```csharp
var placed = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusSk.Ge(since));
```

`byStatus` is `orders.Index(Order.Indexes.ByStatus)`, see [Tables](tables.md#opening-a-table).

## Scan

DynamoDB finds items by key. To find one by another value, a `Scan` reads the whole table and charges for every item, whatever the filter leaves, so it suits a tool and not a hot path:

```csharp
var holders = customers.Scan().Filter(Customer.Attrs.Email.Eq("alice@example.com")).ItemsAsync(ct);
```

A lookup that runs often needs a key: an index on the value, or a claim item as under [TransactWriteItems](transactions.md#transactwriteitems).

## Several types in one query

Each item arrives as what it is, without a dictionary in between.

```csharp
await foreach (var item in shop.Query(Order.Keys.Pk.Of("alice")).Of<Customer, Order>().ItemsAsync(ct))
{
    Console.WriteLine(item.Match(profile => profile.Name, order => $"#{order.Number}"));
}
```

## One list over many partitions

A fan-out query merges partitions by sort key in DynamoDB's own order. A partition is read only when the merge needs it, so breaking out of the loop reads no more.

```csharp
PartitionKey<Order>[] open = [Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)];

await foreach (var order in byStatus.Query(open).Descending().ItemsAsync(ct))
{
    Console.WriteLine(order.Number);
}
```

## Paging for web APIs

A token holds the position, and comes back with the next request. It is not signed, so a client can choose its position in the partition.

```csharp
var page = await orders.Query(Order.Keys.Pk.Of("alice")).Limit(25).PageAsync(ContinuationToken.Parse(after), ct);
string? next = page.HasMore ? page.Next.ToString() : null;
```
