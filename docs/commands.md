# Reading and writing

A command is a chain: the operation, its clauses, and the last call, such as `SendAsync` or `ItemAsync`, that sends it. Build and send it in one expression. [Commands](diagnostics.md#commands) on the diagnostics page says what the compiler and the analyzers report when it is not. `orders`, `customers`, `products` and `carts` are the tables of [the shop](getting-started.md#the-shop).

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

An `Update` that creates an item leaves out the inputs of the key unless `.StoreKeyInputs()` writes them, and every other attribute it does not set. A read of the entity throws `DynamoDbMappingException` for a `required` member that is missing, so an update that may create the item sets each of them, or calls `IfExists()`: [`DYNA241`](diagnostics.md#commands) warns of one that does neither. A `[Version]` adds a condition of its own, see [Optimistic locking](#optimistic-locking).

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

On an entity with a discriminator, every update also writes `_type = if_not_exists(_type, "CUSTOMER")`, so an item that an update creates reads back as its entity, once it holds every `required` member.

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
| `Get(...).Select(handle).SendAsync(ct)`, `Query(...).Select(handle).ValueAsync(ct)` | `SelectResult<TValue>` | `Found`, `Value`, which throws when no item was found, and `GetValueOrDefault(...)` |
| The same with `.And(handle)` after `Select` | `SelectResult<(T1, T2)>`, up to four | The same, with the values as a tuple in the order of the calls |
| `Put`, `Update`, `Delete` with `SendAsync(ct)` | `WriteResult<T>` | `HasItem` and `Item`, what `ReturnOld()` or `ReturnNew()` asked for; `Version`; `ConsumedCapacity` and `ItemCollectionMetrics`; `ConditionMet`, always true |
| `Put`, `Update`, `Delete` with `TrySendAsync(ct)` | `WriteResult<T>` | The same, or `ConditionMet` false, with `Item` what the condition saw under `ReturnOldOnFailure()` |
| `Update(...).Return(handle).SendAsync(ct)` | The value | The attribute after the update |
| `Update(...).Return(handle).TrySendAsync(ct)` | `ReturnResult<TValue>` | `ConditionMet`, and `Value`, the attribute after the update, when it is true |
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

`Value` of a `SelectResult` throws when the read found no item, because a 0 would read like a stored 0. Where no item means a fallback, say so; `DYNA227` reports a `Value` in a member that never looks at `Found`:

```csharp
var total = await orders.Get(Order.Keys.Of("alice", 1)).Select(Order.Attrs.Total).SendAsync(ct);
decimal amount = total.GetValueOrDefault(0m);    // total.Found ? total.Value : 0m
```

`And(handle)` adds an attribute to the same request, up to four, and the read gives them as a tuple. It is what `Project` cannot be on an entity with `required` members, because no entity is built, and each attribute follows the rules of `Select` alone: a missing one is null for a nullable handle, empty for a set, and an exception otherwise.

```csharp
var order = await orders.Get(Order.Keys.Of("alice", 1)).Select(Order.Attrs.Total).And(Order.Attrs.Status).SendAsync(ct);
if (order.Found)
{
    var (amount, status) = order.Value;
    Console.WriteLine($"{status}: {amount}");
}

await foreach (var (total, placed) in orders.Query(Order.Keys.Pk.Of("alice")).Select(Order.Attrs.Total).And(Order.Attrs.PlacedAt).ValuesAsync(ct))
{
    Console.WriteLine($"{total} at {placed}");
}
```

`await command` is `SendAsync()` without a token; a read has none, so `await orders.Query(...)` does not compile.

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

Where a failed condition is routine rather than an error, `TrySendAsync` returns it instead of throwing: `ConditionMet` is false, nothing was written, and with `ReturnOldOnFailure()` `Item` is what the condition saw. Every other error throws as from `SendAsync`.

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

`ReturnOld()` and `ReturnNew()` give nothing for a failed condition; only `ReturnOldOnFailure()` does. A failed condition of `TrySendAsync` is no conflict to `RetryOnConflictAsync` and no error on the span, and it can be the client's own retry after a lost response, see [Where a retry applies a write twice](errors.md#where-a-retry-applies-a-write-twice). `TrySendAsync` is on `Put`, `Update`, `Delete` and `Return(handle)`, not on a transaction. After `Return(handle)` it gives a `ReturnResult<TValue>`, whose `Value` is the attribute after the update when `ConditionMet` is true, which takes no `try` around a claim:

```csharp
var claimed = await products.Update(Product.Keys.Of("KB-01"))
    .Add(Product.Attrs.Stock, -1)
    .If(Product.Attrs.Stock.Gt(0))
    .Return(Product.Attrs.Stock)
    .TrySendAsync(ct);
int? left = claimed.ConditionMet ? claimed.Value : null;
```

`ConditionalCheckFailedException.Key` says which item it was, as stored, for code that does not hold the key, such as a wrapper around a write it was handed. No message and no log of the client contains it.

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

| The write | Write | What it does |
|---|---|---|
| An edit of what the user saw | `Update(key)...IfVersion(seen.Revision)`, `Delete(key).IfVersion(seen.Revision)`, or `Put(seen with { ... })` | Fails if anyone wrote since `seen` was read |
| A counter, an import, an overwrite that does not depend on a read | `IgnoreVersion()` | Writes without the check, and still advances the version |
| A create | `Put` of a new entity, at version 0 | Fails if an item has the key |

`seen` is the copy the decision was made on: the item a `Get` returned when the user opened the cart, or the version the user's client sent back with the edit.

```csharp
var saved = await carts.Update(Cart.Keys.Of("bob"))
    .Set(Cart.Attrs.Lines, lines)
    .IfVersion(seen.Revision)
    .SendAsync(ct);

long next = saved.Version!.Value;
```

`IgnoreVersion()` drops the check and keeps the version moving: a `Put` or a batch stores the version of its entity plus one, and an `Update` adds 1 to the stored version, or stores 1 on an item without one. A copy read before such a write fails its next checked write, so the write is never lost under a stale one, while what the write itself overwrites is not checked. That suits a change that does not depend on what was read, such as a checkout that replaces the lines of the cart whatever a tab saved last:

```csharp
await client.TransactWrite()
    .Put(orders, order)
    .Update(carts, Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines).IgnoreVersion()
    .SendAsync(ct);
```

An `Update` that sets the version attribute itself keeps that value, and the version is not advanced besides it. A `Delete` has nothing to advance.

The entity of a `Put` stays as it was, so it still holds the old version. The result says what was stored: `ReturnNew()` gives the item at its new version, read from the request at no cost, and `Version` the version, or null where the write does not know one (a delete, an update with `IgnoreVersion()`, a `TrySendAsync` whose condition did not hold). A put of a `[Version]` entity and an `IfVersion(n)` update sent with `SendAsync` always know it, so `!.Value` is safe there. Take the returned item for the next write:

```csharp
var stored = await carts.Put(seen with { Lines = lines }).ReturnNew().SendAsync(ct);
Cart current = stored.Item!;
```

A counter on a versioned item, bumped with `Add` and `IgnoreVersion()`, needs no read, but each bump advances the version, so the checked writes of every copy read before it fail. `ClubMember` is the entity of [Versions](entities.md#versions):

```csharp
var members = client.Table<ClubMember>();
await client.TransactWrite()
    .Put(orders, order)
    .Update(members, ClubMember.Keys.Of("alice")).Add(ClubMember.Attrs.Visits, 1).IgnoreVersion().IfExists()
    .SendAsync(ct);
```

That suits a counter that changes now and then. A hot counter, one that changes while people edit the item, goes on an item of its own without a `[Version]`.

## Retry on a conflict

`RetryOnConflictAsync` of a table runs a read-modify-write again when it loses to another writer: 5 runs in all, or what `maxAttempts` says, with a short random wait between them, and the failure of the last run is rethrown. `onConflict`, below, hears of each run that lost. The delegate reads again itself, or the next run changes nothing, and it can write other tables and send a transaction.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

A versioned write whose response was lost fails against its own first attempt on the retry of the client, which counts as a conflict, so the delegate must be safe to run twice, see [Where a retry applies a write twice](errors.md#where-a-retry-applies-a-write-twice).

A value read before the call can serve the first run, as long as every later run reads again. The delegate that takes the number of the run, 1 for the first, says which run it is:

```csharp
var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(ct);
await carts.RetryOnConflictAsync(async (run, token) =>
{
    var current = run == 1 ? cart : await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(token);
    await carts.Put(current! with { Lines = lines }).SendAsync(token);
}, ct);
```

`onConflict` hears of each run that lost and is followed by another, with its number and exception, before the wait, so a log needs no `catch` in the delegate. It is not called for the last run, whose exception surfaces, and what it throws ends the retries.

```csharp
await carts.RetryOnConflictAsync(
    async token =>
    {
        var cart = await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(token);
        await carts.Put(cart! with { Lines = lines }).SendAsync(token);
    },
    ct,
    onConflict: (run, conflict) => Console.WriteLine($"Run {run} lost with {conflict.ErrorCode}, trying again"));
```

The token comes right after the delegate, and the options after it: `RetryOnConflictAsync(run, ct, ConflictKind.TransactionConflict, maxAttempts: 10)`. A run whose failed condition is its answer, such as a claim on a value that is taken, passes `ConflictKind.TransactionConflict`: it runs again only when a transaction is in the way, and a failed condition surfaces at once, see [Uniqueness constraints](patterns-writes.md#uniqueness-constraints).

The delegate can return a `ValueTask` or a `Task`, with a result or without, so a method of yours goes in by name: `carts.RetryOnConflictAsync(SaveAsync, ct)` for a `Task SaveAsync(CancellationToken)`. A lambda takes the `ValueTask` form.

A transaction cancelled only for throttling (`TransactionFailure.Throttled` on every failed action) is no conflict, so `RetryOnConflictAsync` does not run it again, and the client does not repeat a cancelled transaction either, although `IsTransient` is true for it. Catch it around the call when `IsTransient` says so, and send it again after a wait of your own.

## See what goes over the wire

`ToRequestJson` renders what a chain would send, without sending it. It ends the chain as `SendAsync` does, so a command is rendered or sent, not both.

```csharp
string json = products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).IfExists().ToRequestJson();
```

Every command has it but a parallel scan. A batch renders its first request, and `CreateTable()` the body of its `CreateTable` without the calls around it. `CreateTable()` is an ordinary class, so it stays usable after rendering:

```csharp
CreateTableCommand create = products.CreateTable().IfNotExists();
string body = create.ToRequestJson();
await create.SendAsync(ct);
```

## Helpers that return a command

A method of your own may build part of a command and return it for the caller to finish and send, with the rules of any command:

```csharp
static UpdateCommand<Customer> CountOrder(Table<Customer> customers, string id) =>
    customers.Update(Customer.Keys.Of(id)).Add(Customer.Attrs.OrderCount, 1);

await CountOrder(customers, "alice").IfExists().SendAsync(ct);
```

- **The helper is synchronous.** A command cannot be a parameter of an `async` method (CS4012) or cross an `await` (CS4007), see [Commands](diagnostics.md#commands). Compute the values first, call the helper, chain and send in one stretch.
- **It returns the command it built and does not use it.** Every call on a command uses up the handle it was called on, so the helper returns the last handle, and the caller sends it once. Sending it twice, or calling on a used handle, throws `InvalidOperationException`.
- **A command nobody sends needs no clean-up.** It has no `Dispose`, and a command that is dropped, or whose caller throws before the send, is no leak. Only a statement that drops one is an error, `DYNA203`.
- **The analyzers see one method at a time.** A rule that reads a whole chain sees only the part in front of it, so a helper hides what it added from the checks of its caller and the other way round, and a helper with a type parameter for the entity is not traced to its type; the [diagnostics page](diagnostics.md#commands) says under each rule what it does not see.

A transaction step works the same way: a helper takes the chain, adds its steps, and returns it, as `IsHeld` of [Coordination](coordination.md#fencing) does.
