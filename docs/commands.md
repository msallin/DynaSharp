# Reading and writing

Every operation on a table is a command: a chain that starts with the operation, takes its clauses, and ends in a terminal such as `SendAsync` or `ItemAsync`, which sends it. A chain is built and sent in one expression, which [API levels](api-levels.md) explains, and the [diagnostics](diagnostics.md#commands) say what the compiler and the analyzers report when it is not. The examples use the shop of the [sample](https://github.com/msallin/DynaSharp/tree/main/samples).

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

`SendAsync` of a write is a `ValueTask<WriteResult<T>>`. `Item` and `HasItem` hold what `ReturnOld` or `ReturnNew` asked for, and `Version` what a `[Version]` entity stored. `Found` is on a read only.

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

## Results

What a terminal returns depends on the operation, and a write has no `Found`: it finds nothing, unless it asks for the item that was there.

| Call | Returns | Holds |
|---|---|---|
| `Get(...).ItemAsync(ct)` | `T?` | The item, or null |
| `Get(...).SendAsync(ct)` | `GetResult<T>` | `Found`, `Item` and `ConsumedCapacity` |
| `Get(...).Select(handle).SendAsync(ct)` | `SelectResult<TValue>` | `Found` and `Value`, which throws when no item was found |
| `Put`, `Update`, `Delete` with `SendAsync(ct)` | `WriteResult<T>` | `HasItem` and `Item`, what `ReturnOld()` or `ReturnNew()` asked for; `Version`, what a `[Version]` entity stored; `ConsumedCapacity` and `ItemCollectionMetrics` |
| `Update(...).Return(handle).SendAsync(ct)` | The value | The attribute after the update |
| `PageAsync(...)` of a read | `Page<T>` | `Items`, `Count`, `ScannedCount`, `ConsumedCapacity`, `HasMore` and `Next` |
| `BatchWrite()`, `BatchGet()` | `BatchWriteResult`, `BatchGetResult<T>` | `Written` or `Items`, `Requests` and `ConsumedCapacity` |
| `TransactWrite()`, `TransactGet()` | `TransactWriteResult`, `TransactGetResult` | `ConsumedCapacity`, and `Read(slot)` of a get, which gives a `GetResult<T>` |
| `CreateTable()` | `CreateTableResult` | `Table`, `Created` and `TimeToLiveEnabled` |

`ConsumedCapacity` is null, or empty, unless `ReturnCapacity()` asked for it. Whether a delete removed anything is a question for `ReturnOld()`:

```csharp
var deleted = await orders.Delete(Order.Keys.Of("alice", 1)).ReturnOld().SendAsync(ct);
if (!deleted.HasItem)
{
    Console.WriteLine("There was no such order.");
}
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

`ConditionalCheckFailedException.Key` says which item it was, as it is stored. A caller that holds the key gains nothing from it. It is for code that does not, such as a wrapper around a write it was handed. It is in no message and no log of the client.

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

A `[Version]` property turns every write of the entity into a compare-and-swap. A `Put` takes the version from its entity: 0 says that no item has the key, any other value must be the stored one, and otherwise it throws `ConditionalCheckFailedException`. An `Update` or a `Delete` by key names the version with `IfVersion(n)`, or gives the check up with `IgnoreVersion()`, and throws before anything is sent without either. A batch has only `IgnoreVersion()`.

```csharp
var saved = await carts.Update(Cart.Keys.Of("bob"))
    .Set(Cart.Attrs.Lines, lines)
    .IfVersion(seen.Revision)
    .SendAsync(ct);
```

## Retry on a conflict

`RetryOnConflictAsync`, on the client or on a table, runs a read-modify-write again when it loses to another writer: 5 runs in all, with a short random wait between them, and the failure of the last run is rethrown. The delegate reads again itself, or the next run changes nothing.

```csharp
await carts.RetryOnConflictAsync(async token =>
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(token);
    await carts.Put(cart! with { Lines = lines }).SendAsync(token);
}, ct);
```

## See what goes over the wire

`ToRequestJson` renders what a chain would send, without sending it. It ends the chain as `SendAsync` does, so a command is rendered or sent, not both.

```csharp
string json = products.Update(Product.Keys.Of("KB-01")).Set(Product.Attrs.Price, 79.00m).ToRequestJson();
```
