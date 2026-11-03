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

The builder of a sort key writes the condition from values, as the template formats them, so nobody puts a prefix and an upper bound together by hand. `BeginsWith(leading...)` reads everything under the fields it is given, and `BeginsWith()` everything of the template. `Between(leading..., from, to)` varies one field, keeps the fields before it equal, and includes both ends, all keys at `to` among them. `Range` is the same without the keys at `to`, for `[from, to)`. It exists only where something follows the field in the template. Where the field is the last part of the key, use `Between` and pass the value one step before `to`, as in [Time ranges](#time-ranges). `Ge`, `Gt`, `Le` and `Lt` have one end, so they can reach keys of other templates; a typed read drops the items that are not its type. A builder has only the conditions that are right for its template, see [Generated code](generated-code.md#a-sort-key-of-a-template), and `SortKeyCondition` takes text that is at hand already.

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
    Order.Keys.ByStatusSk.Between(today, today.AddDays(1).AddTicks(-1))).ItemsAsync(ct);                    // {PlacedAt} ends the key
```

A bare `{At}` or `{PlacedAt}` is `ticks` and keeps all seven fraction digits, so one tick below midnight is the last key of the day; under `{PlacedAt:ms}` it would be one millisecond. A coarser format makes buckets: under `{At:yyyy-MM-dd}`, `Between(from, to)` takes whole days and `Gt(day)` starts after that day.

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

## Items that do not fit

An item that its type cannot read, such as one with an enum value a newer deployment wrote, ends the read with `DynamoDbMappingException`, which names the attribute and holds the key of the item in `Key`. That is the default, because such an item is a bug of the model or of a writer. `OnMappingFailure` skips it instead and hands the exception to a callback, which decides what to do with the key:

```csharp
var unreadable = new List<Item?>();
await foreach (var order in orders.Query(Order.Keys.Pk.Of("alice")).OnMappingFailure(failure => unreadable.Add(failure.Key)).ItemsAsync(ct))
{
    Console.WriteLine(order.Number);
}
```

`OnMappingFailure(static _ => { })` skips without a callback of its own. Query, scan, fan-out and mixed reads have it, and it covers every terminal that reads items, including a `Select` after it, whose response has no key, so `Key` is null there.

- The callback runs once per skipped item, after its page arrived and before the items of that page are given out. An exception it throws ends the read as it is, and no retry repeats it. A parallel scan and a fan-out read pages on several threads at once, so there it can run concurrently.
- `Page.Items` leaves the item out. `Page.Count` is still the count DynamoDB sent, so `Count - Items.Count` is what the page skipped, except on a fan-out, whose page counts what it gives.
- `Limit(n)` counts the item, because DynamoDB read it. `Take(n)` does not, so it reads on until it has `n` items, and `ItemAsync` gives the first item that can be read. `CountAsync` reads no item and counts it.
- `Page.Next` continues after the page, so the item is not read again. A fan-out continues after the last item it gave from each partition, so a skipped item behind that one comes up again on the next page.
- Each skipped item counts in the metric `dynasharp.client.operation.skipped_items`, see [Observability](observability.md#metrics). Nothing logs the key.

## Paging for web APIs

A token holds the position, and comes back with the next request. It is safe in a URL, and it shows the key values of the last item, as `LastEvaluatedKey` does. What the client sends back is input like any other: `TryParse` turns text that is no token into `false`, and `PageAsync` throws `FormatException` for a token that does not fit the query or the signing of the client. Both are a 400 for the client, which starts its read again. A missing or empty `after` is the first page.

```csharp
if (!ContinuationToken.TryParse(after, out var position))
{
    return; // 400 Bad Request: not a token, or one of another release
}

Page<Order> page;
try
{
    page = await orders.Query(Order.Keys.Pk.Of("alice")).Limit(25).PageAsync(position, ct);
}
catch (FormatException)
{
    return; // 400 Bad Request: a token of another query, or one whose signature does not hold
}

string? next = page.HasMore ? page.Next.ToString() : null;
```

A plain token can be edited, so a client can choose its position in the partition. Within the key condition that is only a place among the items the query reads anyway: DynamoDB rejects a start key outside it, and a filter applies from any position. Where the key condition is not what keeps clients apart, sign the tokens with a key the service holds:

```csharp
using var signing = new DynamoDbClient(new DynamoDbClientOptions
{
    TokenSigner = new ContinuationTokenSigner(Convert.FromBase64String(Environment.GetEnvironmentVariable("PAGE_TOKEN_KEY")!)),
});
```

Each `Next` of that client is then signed with HMAC-SHA256 over the token and over the table, the index and the key condition of the query, and for a fan-out the list of partitions. `PageAsync` checks the signature before a request goes out, and throws `FormatException` for a token that was edited or made up, one of another query, or one without a signature. The message quotes nothing of the token, so a web handler can return it as a bad request. The filter, the projection, `Limit` and the direction are not part of the signature, so a client can change its page size between pages. Signing does not hide the key values.

A key is 32 random bytes or more, for example `RandomNumberGenerator.GetBytes(32)`, and every instance of the service holds the same keys. To rotate, pass the new key after the current one on every instance, then move it to the front, and drop the old one once its tokens are no longer in use: the first key signs, and every key is tried to check. A client without a signer rejects a signed token, and one with a signer rejects a plain token that came as text, so clients start their read again once when signing is switched on. A token from `ContinuationToken.FromKey` is built by the service itself and goes through either way.
