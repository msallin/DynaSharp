# Queries and scans

A query reads one partition, or a range of it, in the order of the sort key. A scan reads the whole table. Both end in `ItemsAsync`, which streams the items, and most reads also end in `PageAsync`, `PagesAsync` or `CountAsync`: `DYNA202` under [Commands](diagnostics.md#commands) says which read has which.

`orders`, `customers`, `shop` and `byStatus` are the tables of [the shop](getting-started.md#the-shop).

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

A filter can also be text, as in `.Filter($"{Order.Attrs.Total} > {100}")`, where a handle in a hole stands for the name. Text takes a bare name as it is, so DynamoDB rejects a reserved word such as `Total` or `Value` (`DYNA220`), where `Attrs` needs no care, and a bare name the entity does not have is `DYNA213`.

`Limit(n)` is per page and counts what DynamoDB reads before the filter, so it is no cap on items. `Take(n)` is, and it stops reading once it has them:

```csharp
var firstTen = orders.Query(Order.Keys.Pk.Of("alice"))
    .Filter(Order.Attrs.Total.Gt(100m))
    .ItemsAsync(ct)
    .Take(10);
```

## Sort key conditions

The builder of a sort key writes the condition from values, as the template formats them, so nobody puts a prefix and an upper bound together by hand. `BeginsWith(leading...)` reads everything under the fields it is given, and `BeginsWith()` everything of the template. `Between(leading..., from, to)` varies one field, keeps the fields before it equal, and includes both ends, all keys at `to` among them. `Range` is the same without the keys at `to`, for `[from, to)`, and exists only where something follows the field in the template: a key that ends in the field takes `Between` with `to` one step lower. `Ge`, `Gt`, `Le` and `Lt` have one end, so they go on past the template, and a typed read drops what is not its type. A builder has only the conditions that are right for its template, see [Generated code](generated-code.md#a-sort-key-of-a-template), and `SortKeyCondition` takes text that is at hand already.

### Time ranges

A timestamp in a key has a fixed width, so its text sorts like its time:

```csharp
[DynamoDbEntity]
[Discriminator("VISIT")]
[PartitionKey("STORE#{StoreId}")]
[SortKey("AT#{At}#{VisitId}")]
public partial record StoreVisit
{
    public required string StoreId { get; init; }
    public required DateTimeOffset At { get; init; }
    public required Guid VisitId { get; init; }
}
```

```csharp
var visits = client.Table<StoreVisit>("Visits");
var bern = StoreVisit.Keys.Pk.Of("bern");
var today = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

var yesterday = visits.Query(bern, StoreVisit.Keys.Sk.Range(today.AddDays(-1), today)).ItemsAsync(ct);     // [yesterday, today)
var lastHour = visits.Query(bern, StoreVisit.Keys.Sk.Ge(DateTimeOffset.UtcNow.AddHours(-1))).ItemsAsync(ct);
StoreVisit? latest = await visits.Query(bern, StoreVisit.Keys.Sk.Le(today)).Descending().Limit(1).ItemAsync(ct);

var placedToday = byStatus.Query(
    Order.Keys.ByStatusPk.Of(OrderStatus.Placed),
    Order.Keys.ByStatusSk.Between(today, today.AddDays(1).AddMilliseconds(-1))).ItemsAsync(ct);             // {PlacedAt} ends the key
```

`{At}` keeps milliseconds, so one millisecond below midnight is the last key of the day. A coarser format makes buckets: under `{At:yyyy-MM-dd}`, `Between(from, to)` takes whole days and `Gt(day)` starts after that day.

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
