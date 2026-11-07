# Reading and writing

A command is a chain: the operation, its clauses, and a terminal such as `SendAsync` or `ItemAsync` that sends it. Build and send it in one expression. [Commands](diagnostics.md#commands) on the diagnostics page says what the compiler and the analyzers report when it is not. `orders`, `customers`, `products` and `carts` are the tables of [the shop](getting-started.md#the-shop).

## GetItem

`ItemAsync` returns the item, or null when there is none. `SendAsync` is the same request, with `Found` and the consumed capacity.

```csharp
Order? order = await orders.Get(Order.Keys.Of("alice", 1)).ItemAsync(ct);

var result = await orders.Get(Order.Keys.Of("alice", 1)).Consistent().SendAsync(ct);
```

## PutItem

A put replaces the whole item. Conditions are part of the same atomic step.

```csharp
await orders.Put(order).IfNotExists().SendAsync(ct);
```

Which write to take:

| You want to | Write | An item exists | No item exists |
|---|---|---|---|
| Create, and never overwrite | `Put(entity).IfNotExists()` | `ConditionalCheckFailedException` | Creates it |
| Create or replace | `Put(entity)` | Replaces all of it | Creates it |
| Replace what is there | `Put(entity).IfExists()` | Replaces all of it | `ConditionalCheckFailedException` |
| Patch, or create | `Update(key).Set(...)` | Changes what it names | Creates it from the key and what it names |
| Patch what is there | `Update(key).Set(...).IfExists()` | Changes what it names | `ConditionalCheckFailedException` |

An `Update` that creates an item leaves out the inputs of the key, and `.StoreKeyInputs()` writes them, as a `Put` does. A `[Version]` adds a condition of its own, see [Optimistic locking](#optimistic-locking).

## UpdateItem

An update changes what it names and leaves the rest. `At` walks into a nested document, and `ReturnNew` gives back the item in one round trip.

```csharp
var moved = await customers.Update(Customer.Keys.Of("alice"))
    .Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), "Bern")
    .Add(Customer.Attrs.Interests, ["keyboards"])
    .IfExists()
    .ReturnNew()
    .SendAsync(ct);
```

`Set` of null removes the attribute, and `SetIfNotNull` adds nothing for null, which suits a change where only some fields came in:

```csharp
await customers.Update(Customer.Keys.Of("alice"))
    .SetIfNotNull(Customer.Attrs.ShipTo, customer.ShipTo)
    .IfExists()
    .SendAsync(ct);
```

On an entity with a discriminator, every update also writes `_type = if_not_exists(_type, "CUSTOMER")`, so an item that an update creates reads back as its entity. `ToRequestJson()` below shows it.

## DeleteItem

A delete takes the key, or the entity, which gives the key and, on a `[Version]` entity, the version it was read with.

```csharp
await orders.Delete(Order.Keys.Of("alice", 1)).IfExists().SendAsync(ct);
```

## What a call returns

A write has no `Found`: it finds nothing, unless it asks for the item that was there with `ReturnOld()`.

| Call | Returns | Holds |
|---|---|---|
| `Get(...).ItemAsync(ct)` | `T?` | The item, or null |
| `Get(...).SendAsync(ct)` | `GetResult<T>` | `Found`, `Item` and `ConsumedCapacity` |
| `Get(...).Select(handle).SendAsync(ct)`, `Query(...).Select(handle).ValueAsync(ct)` | `SelectResult<TValue>` | `Found` and `Value`, which throws when no item was found; `GetValueOrDefault()` and `GetValueOrDefault(fallback)`, which do not |
| `Put`, `Update`, `Delete` with `SendAsync(ct)` | `WriteResult<T>` | `HasItem` and `Item`, what `ReturnOld()` or `ReturnNew()` asked for; `Version`, what a `[Version]` entity stored, or null; `ConsumedCapacity` and `ItemCollectionMetrics`; `ConditionMet`, always true |
| `Put`, `Update`, `Delete` with `TrySendAsync(ct)` | `WriteResult<T>` | The same, or for a condition that did not hold `ConditionMet` false, with `Item` what the condition saw if `ReturnOldOnFailure()` asked for it, and no `Version`, capacity or metrics |
| `Update(...).Return(handle).SendAsync(ct)` | The value | The attribute after the update |
| `PageAsync(...)` of a read | `Page<T>` | `Items`, `Count`, `ScannedCount`, `ConsumedCapacity`, `HasMore` and `Next` |
| `BatchWrite()`, `BatchGet()` | `BatchWriteResult`, `BatchGetResult<T>` | `Written` or `Items`, `Requests` and `ConsumedCapacity` |
| `TransactWrite()`, `TransactGet()` | `TransactWriteResult`, `TransactGetResult` | `ConsumedCapacity`, and `Read(slot)` of a get, which gives a `GetResult<T>` |
| `CreateTable()` | `CreateTableResult` | `Table`, `Created`, `TimeToLiveEnabled` and `Drift` |

`ConsumedCapacity` is null, or empty, unless `ReturnCapacity()` asked for it. Whether a delete removed anything is a question for `ReturnOld()`:

```csharp
var deleted = await orders.Delete(Order.Keys.Of("alice", 1)).ReturnOld().SendAsync(ct);
if (!deleted.HasItem)
{
    Console.WriteLine("There was no such order.");
}
```

`Value` of a `SelectResult` throws when the read found no item, because a 0 would read like a stored 0. Where no item means a fallback, say so, as `Nullable<T>` does; `DYNA227` reports a `Value` in a member that never looks at `Found`:

```csharp
var total = await orders.Get(Order.Keys.Of("alice", 1)).Select(Order.Attrs.Total).SendAsync(ct);
decimal amount = total.GetValueOrDefault(0m);    // total.Found ? total.Value : 0m
```

`await command` is `SendAsync()` without a token, on the commands that have one. A read has no such default, so `await orders.Query(...)` does not compile.

## Conditions

Each `.If` adds one condition, and they join with `AND`. In a transaction it belongs to the step before it. A condition can also return what stopped it, which saves the read:

```csharp
try
{
    await products.Update(Product.Keys.Of("KB-01"))
        .Set(Product.Attrs.Price, 79.00m)
        .If(Product.Attrs.Price.Eq(95.00m))
        .If(Product.Attrs.Stock.Gt(0))
        .ReturnOldOnFailure()
        .SendAsync(ct);
}
catch (ConditionalCheckFailedException exception)
{
    Console.WriteLine(exception.Item!.To<Product>().Price);
}
```

Where a failed condition is routine rather than an error, `TrySendAsync` returns it instead of throwing: `ConditionMet` is false, nothing was written, and with `ReturnOldOnFailure()` `Item` is what the condition saw, read as the entity. Every other error throws as from `SendAsync`, and the request is the same.

```csharp
var result = await products.Update(Product.Keys.Of("KB-01"))
    .Set(Product.Attrs.Price, 79.00m)
    .If(Product.Attrs.Price.Eq(95.00m))
    .ReturnOldOnFailure()
    .TrySendAsync(ct);
if (!result.ConditionMet)
{
    Console.WriteLine(result.Item?.Price);
}
```

`ReturnOld()` and `ReturnNew()` give nothing for a failed condition; only `ReturnOldOnFailure()` does. A failed condition of `TrySendAsync` is no conflict to `RetryOnConflictAsync` and no error on the span. A retry of the client after a lost response can fail against its own first attempt, which looks the same as a second delivery of the change, see [Where a retry applies a write twice](errors.md#where-a-retry-applies-a-write-twice). `TrySendAsync` is on `Put`, `Update` and `Delete`, not on `Return(handle)` or a transaction.

`ConditionalCheckFailedException.Key` says which item it was, as it is stored. It is for code that does not hold the key, such as a wrapper around a write it was handed. No message and no log of the client contains it.

```csharp
async Task SaveAsync(Func<CancellationToken, Task> save, CancellationToken token)
{
    try
    {
        await save(token);
    }
    catch (ConditionalCheckFailedException exception) when (exception.Key is { } key)
    {
        Console.WriteLine(Order.Keys.Pk.Parse(key.GetString("pk")!).CustomerId);
        throw;
    }
}
```

What a condition can say, typed or as text, is on [Expressions](expressions.md).

## Optimistic locking

A `[Version]` property turns every write of the entity into a compare-and-swap. A `Put` takes the version from its entity: 0 says that no item has the key, any other value must be the stored one, and otherwise it throws `ConditionalCheckFailedException`. An `Update` or a `Delete` by key names the version with `IfVersion(n)`, or gives the check up with `IgnoreVersion()`, and throws before anything is sent without either, which the build reports as [`DYNA239`](diagnostics.md#commands) where it can see the whole write. A batch has only `IgnoreVersion()`.

```csharp
var saved = await carts.Update(Cart.Keys.Of("bob"))
    .Set(Cart.Attrs.Lines, lines)
    .IfVersion(seen.Revision)
    .SendAsync(ct);

long next = saved.Version!.Value;
```

`IgnoreVersion()` drops the check and keeps the version moving: a `Put` or a batch still stores the version of its entity plus one, and an `Update` adds 1 to the stored version (`SET #v = if_not_exists(#v, :zero) + :one`, so an item without one gets 1). A copy read before such a write fails its next checked write, so the write is never lost under a stale one. What the write itself overwrites is not checked, which suits a change that does not depend on what was read, such as a checkout that replaces the lines of the cart whatever a tab saved last. A tab that still holds the cart it read before then fails its next save:

```csharp
await client.TransactWrite()
    .Put(orders, order)
    .Update(carts, Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines).IgnoreVersion()
    .SendAsync(ct);
```

An `Update` that sets the version attribute itself keeps that value, and the version is not advanced besides it. A `Delete` has nothing to advance.

The entity of a `Put` stays as it was, and the result says what was stored: `Version` is the version, or null where the write does not know one (a delete, an update with `IgnoreVersion()`, which advances a version it did not read, a `TrySendAsync` whose condition did not hold). A put of a `[Version]` entity and an `IfVersion(n)` update sent with `SendAsync` always store one, so `!.Value` is safe there; elsewhere `saved.Version is { } next` asks.

## Retry on a conflict

`RetryOnConflictAsync` of a table runs a read-modify-write again when it loses to another writer: 5 runs in all, with a short random wait between them, and the failure of the last run is rethrown. The delegate reads again itself, or the next run changes nothing. Nothing ties a run to the table it is called on: it can write other tables and send a transaction.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

Inside it, a versioned write whose response was lost meets its own first attempt on the retry of the client, fails its condition, and counts as a conflict, so the delegate runs again, see [Where a retry applies a write twice](errors.md#where-a-retry-applies-a-write-twice). A delegate must be safe to run twice.

A value read before the call, for a decision that came first, can serve the first run, as long as every later run reads again: the delegate that takes the number of the run, 1 for the first, says which run it is, without a variable that the delegate clears after its first use.

```csharp
var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(ct);
await carts.RetryOnConflictAsync(async (run, token) =>
{
    var current = run == 1 ? cart : await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(token);
    await carts.Put(current! with { Lines = lines }).SendAsync(token);
}, ct);
```

A run that reads nothing, and whose failed condition is its answer, such as a claim on a value that is taken, passes `ConflictKind.TransactionConflict`: it runs again only when a transaction is in the way, and a failed condition surfaces at once. See [Uniqueness constraints](patterns-writes.md#uniqueness-constraints).

## See what goes over the wire

`ToRequestJson` renders what a chain would send, without sending it. It ends the chain as `SendAsync` does, so a command is rendered or sent, not both.

```csharp
string json = products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).ToRequestJson();
```

Every command has it but a parallel scan, which sends one request per segment. A batch renders its first request, and `CreateTable()` the body of its `CreateTable`, without the calls that `IfNotExists()`, `WaitUntilActive()` and `TimeToLive(...)` make around it. `CreateTable()` is an ordinary class, so it stays usable after rendering:

```csharp
CreateTableCommand create = products.CreateTable().IfNotExists();
string body = create.ToRequestJson();
await create.SendAsync(ct);
```

## Helpers that return a command

A method of your own may build part of a command and return it for the caller to finish and send. That is supported, with the rules of any command, because a command is a `ref struct` over pooled state that the client lends it:

```csharp
static UpdateCommand<Customer> CountOrder(Table<Customer> customers, string id) =>
    customers.Update(Customer.Keys.Of(id)).Add(Customer.Attrs.OrderCount, 1);

await CountOrder(customers, "alice").IfExists().SendAsync(ct);
```

- **The helper is synchronous.** An `async` method cannot take a command (CS4012), a `Task` cannot hold one, and the caller cannot keep one across an `await` (CS4007), see the table of [Commands](diagnostics.md#commands). Compute the values first, call the helper, chain and send in one stretch.
- **It returns the command it built and does not use it.** Every call on a command uses up the handle it was called on, so the helper returns the last handle, and the caller sends it once. Sending it twice, or calling on a handle the helper already used, throws `InvalidOperationException`.
- **A command nobody sends costs nothing to clean up.** It has no `Dispose`: its state goes back to the pool when it is sent, and a command that is dropped, or whose caller throws before the send, is collected with its buffers. Only a statement that drops one is an error, `DYNA203`.
- **The analyzers see one method at a time.** Rules that read a whole chain, from `Update(...)` to `SendAsync`, see only the part in front of them, so a helper hides what it added from the caller's checks and the other way round: overlapping actions (`DYNA223`), an index key without all its inputs (`DYNA226`), what a query reads for its filter (`DYNA221`, `DYNA222`), a version in the chain (`DYNA206`), a `Project` that leaves out a required member (`DYNA208`) and a batch of a versioned entity that the helper returns (`DYNA236`). The rules of a single call, such as a handle of another type (`DYNA215`) or a write of a key input (`DYNA205`), still apply inside the helper. The [diagnostics page](diagnostics.md#commands) says under each rule what it does not see.
- **A helper with a type parameter** for the entity is not traced to its type, so the rules that need the entity skip it.

A transaction step works the same way: a helper takes the chain, adds its steps, and returns it, as `IsHeld` of [Coordination](coordination.md#fencing) does.
