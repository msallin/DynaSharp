# Queries and scans

A query reads one partition, or a range of it, in the order of the sort key, and a scan reads the whole table. Both are sent by `ItemsAsync`, which streams the items, and most also by `PageAsync`, `PagesAsync` or `CountAsync`: `DYNA202` under [Commands](diagnostics.md#commands) says which read has which.

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

`Descending()` reads the sort key downwards, newest first in a partition ordered by time, and `Descending(newestFirst)` takes the direction as a value decided at run time.

A filter can also be text, as in `.Filter($"{Order.Attrs.Total} > {100}")`, where a handle in a hole stands for the name. A bare name goes as it is, so DynamoDB rejects a reserved word such as `Total` (`DYNA220`), and one the entity does not have is `DYNA213`.

`Limit(n)` is per page and counts what DynamoDB reads before the filter, so it is no cap on items. `Take(n)` is: it gives at most `n` items across pages, and asks each page for no more than are still to come, so the newest ten read ten and pay for ten:

```csharp
var newestTen = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
    .Descending()
    .Take(10)
    .ItemsAsync(ct);
```

`Take(n).PageAsync(ct)` gives the items as one page, read from as many responses as it took, with `Next` after the last item, so a client pages in steps of `n`. `HasMore` can say more where nothing follows, see [Page by page](#page-by-page); to know, take one more and look:

```csharp
const int max = 100;
var history = await orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith()).Consistent().Take(max + 1).PageAsync(ct);
var truncated = history.Items.Count > max;
var shown = history.Items.Take(max).ToList();
```

With a filter, `Take` asks for smaller pages as it goes and can take many calls; `ItemsAsync(ct).Take(n)` of `System.Linq` reads full pages instead and pays for the whole last one. A `Limit` stays the most per page. `PagesAsync` stops after `n` items, `CountAsync` counts up to `n`, and a parallel scan and a fan-out have no `Take`.

`ItemsAsync(ct)` asks for a page only once the items of the one before are taken, so a `break` reads no more and memory holds one page. `ToListAsync()` of `System.Linq` takes every page, so keep it for a bounded read, such as a `Take(n)`. The token goes to `ItemsAsync`; passing it to `ToListAsync` too, as CA2016 asks, is harmless.

## Sort key conditions

The builder of a sort key writes the condition from values, as the template formats them, so nobody puts a prefix and an upper bound together by hand.

| Condition | Reads |
|---|---|
| `BeginsWith()` | Every key of the template |
| `BeginsWith(leading...)` | Every key under the leading fields it is given |
| `Between(leading..., from, to)` | One field from `from` to `to`, both ends included, all keys at `to` among them, with the fields before it equal |
| `Range(leading..., from, to)` | The same without the keys at `to`, for `[from, to)`. It exists only where something follows the field in the template. Where the field is the last part of the key, use `Between` and pass the value one step before `to`, as in [Time ranges](#time-ranges) |
| `Ge`, `Gt`, `Le`, `Lt` | One end only, so they can reach keys of other templates. A typed read drops the items that are not its type |

A builder has only the conditions that fit its template, see [Generated code](generated-code.md#a-sort-key-of-a-template); `SortKeyCondition` takes text at hand already.

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

A bare `{At}` is `ticks`, so one tick below midnight is the last key of the day; under `{At:ms}` it is one millisecond. A coarser format makes buckets: under `{At:yyyy-MM-dd}`, `Between(from, to)` takes whole days and `Gt(day)` starts after that day.

## Indexes

An index is read through its own type, which has no writes:

```csharp
var placed = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusSk.Ge(since));
```

A query names the index in `Index(...)` and again in its key builder, `Keys.ByStatusPk`; DynaSharp does not infer one from the other. Keep the index in a field, opened once beside the table:

```csharp
var byStatus = orders.Index(Order.Indexes.ByStatus);
var shipped = byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)).ItemsAsync(ct);
```

A typed query or scan of an index filters on the discriminator, `_type` or the name the entity gives it, so the index must project it. An index that `CreateTable()` creates projects every attribute. One created by another tool with `KEYS_ONLY`, or with `INCLUDE` that leaves it out, makes a typed read of it fail with a `ValidationException` or find nothing; `CreateTable().IfNotExists()` reports such an index as drift, see [A table that exists](tables.md#a-table-that-exists). Read it [without a model](without-a-model.md), or give it a projection with `_type`.

A key built for another index or for the table throws `ArgumentException` at the call, before a request, naming the builder to use: for `byStatus.Query(Order.Keys.Pk.Of("alice"))` that is `Order.Keys.ByStatusPk.Of(...)`. `DYNA224` reports it in the build only where `Index(...)` is in the chain of the query, so for an index in a field such as `byStatus` it shows the first time the call runs.

## Scan

A `Scan` reads the whole table and charges for every item, whatever the filter leaves, so it suits a tool and not a hot path:

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

The changes of a table of several entities are typed the same way, by the discriminator of their images, see [Typed changes](streams.md#typed-changes).

## One list over many partitions

A fan-out query merges partitions by sort key in DynamoDB's own order. A partition is read only when the merge needs it, so breaking out of the loop reads no more.

```csharp
PartitionKey<Order>[] open = [Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)];

await foreach (var order in byStatus.Query(open).Descending().ItemsAsync(ct))
{
    Console.WriteLine(order.Number);
}
```

A fan-out has no `Take`. `PageAsync(pageSize, after)` gives one page of the merge for a client that pages across requests: build the same query, with the same partitions in the same order, and pass the `Next` of the page before. It reads on through empty pages until the page is full, so only a page with fewer items than `pageSize` is sure to be the last.

```csharp
PartitionKey<Order>[] open = [Order.Keys.ByStatusPk.Of(OrderStatus.Placed), Order.Keys.ByStatusPk.Of(OrderStatus.Shipped)];
var first = await byStatus.Query(open).Descending().PageAsync(25, ct);
var second = await byStatus.Query(open).Descending().PageAsync(25, first.Next, ct);
```

## Read a few attributes

`Project` asks DynamoDB for some attributes, and the read still builds the entity, which throws when a `required` member is left out. `Select(handle)` reads one attribute without the entity, and `And(handle)` adds up to three more, as a tuple:

```csharp
await foreach (var (total, status) in orders.Query(Order.Keys.Pk.Of("alice")).Select(Order.Attrs.Total).And(Order.Attrs.Status).ValuesAsync(ct))
{
    Console.WriteLine($"{status}: {total}");
}
```

Every other clause comes before `Select`. It saves bandwidth, not capacity: DynamoDB charges for whole items. See [What a call returns](commands.md#what-a-call-returns) for what a missing attribute gives.

## Skip items that fail to map

An item that its type cannot read, such as one with an enum value a newer deployment wrote, ends the read with `DynamoDbMappingException`, which names the attribute and holds the key in `Key`, because such an item is a bug of the model or of a writer. `OnMappingFailure` skips it instead and hands the exception to a callback:

```csharp
var unreadable = new List<Item?>();
await foreach (var order in orders.Query(Order.Keys.Pk.Of("alice")).OnMappingFailure(failure => unreadable.Add(failure.Key)).ItemsAsync(ct))
{
    Console.WriteLine(order.Number);
}
```

`OnMappingFailure(static _ => { })` skips without a callback of its own. Query, scan, fan-out and mixed reads have it, for every call that sends a read of items; after a `Select` the response has no key, so `Key` is null. `ReadChangesAsync` of a stream skips the same way, see [Streams](streams.md#skip-changes-that-fail-to-map).

- The callback runs once per skipped item, before the items of its page are given out. An exception it throws ends the read, and no retry repeats it. On a parallel scan and a fan-out it can run concurrently.
- `Page.Items` leaves the item out, and `Page.Count` is still the count DynamoDB sent, except on a fan-out, whose page counts what it gives.
- `Limit(n)` and `CountAsync` count the item, because DynamoDB read it. `Take(n)` does not, so it reads on until it has `n` items, and `ItemAsync` gives the first item that can be read.
- `Page.Next` continues after the page, so the item is not read again, except on a fan-out, which continues after the last item it gave from each partition.
- Each skipped item counts in the metric `dynasharp.client.operation.skipped_items`, see [Observability](observability.md#metrics).

## Page by page

To read one page at a time, `PageAsync` takes the position: `null` is the first page, and `page.Next` is where the next one starts while `HasMore` holds. Build the same query for every page.

```csharp
ContinuationToken? position = null;
Page<Order> page;
do
{
    page = await orders.Query(Order.Keys.Pk.Of("alice")).Limit(25).PageAsync(position, ct);
    foreach (var placed in page.Items)
    {
        Console.WriteLine(placed.Number);
    }

    position = page.Next;
}
while (page.HasMore);
```

A page can be empty and still have more, so the loop asks `HasMore` and not `Items.Count`. A filter can leave a page empty, and when the items end exactly at the `Limit`, DynamoDB still returns a `LastEvaluatedKey`: that page says `HasMore`, and the page after it is empty and the last.

## Paging for web APIs

A token holds the position and comes back with the next request. It is safe in a URL and shows the key values of the last item, as `LastEvaluatedKey` does. What the client sends back is input like any other: `TryParse` turns text that is no token into `false`, and `PageAsync` throws `FormatException` for a token that does not fit the query or the signing of the client. Both are a 400, after which the client starts its read again. A missing or empty `after` is the first page.

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

A plain token can be edited, so a client can choose its position, though only among the items the key condition reads anyway: DynamoDB rejects a start key outside it. Where the key condition is not what keeps clients apart, sign the tokens with a key the service holds:

```csharp
using var signing = new DynamoDbClient(new DynamoDbClientOptions
{
    TokenSigner = new ContinuationTokenSigner(Convert.FromBase64String(Environment.GetEnvironmentVariable("PAGE_TOKEN_KEY")!)),
});
```

- **Signed:** each `Next` of that client, with HMAC-SHA256 over the token, the table, the index, the key condition and, for a fan-out, the partitions. The filter, the projection, `Limit` and the direction are not signed, so a client can change its page size between pages.
- **Checked:** by `PageAsync`, before a request goes out. An edited or made-up token, one of another query, or one without a signature throws `FormatException`, whose message quotes nothing of the token.
- **Not hidden:** signing does not hide the key values.

A key is 32 random bytes or more, such as `RandomNumberGenerator.GetBytes(32)`, the same on every instance. The first key signs, and every key is tried to check. To rotate:

1. Pass the new key after the current one, on every instance.
2. Move it to the front.
3. Drop the old one once its tokens are no longer in use.

A client without a signer rejects a signed token, and one with a signer a plain token that came as text, so clients start their read again once when signing is switched on. A token from `ContinuationToken.FromKey` goes through either way.
