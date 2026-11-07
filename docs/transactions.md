# Transactions and batches

A transaction writes or reads up to 100 items as one step: all of it or nothing, at twice the capacity. A batch moves many items in as few requests as DynamoDB takes, and is not atomic.

`customers`, `products`, `carts` and `emails` are the tables of [the shop](getting-started.md#the-shop).

## TransactWriteItems

Everything is written together or not at all. A marker tells which action failed, and each step type only offers what that action can do, so `.Add` after `.Put` does not compile.

A value that must be unique gets a claim item, whose key is that value. All entities of one table share its key shape, or `Including` throws that they "cannot share a table", so a claim has a constant `[SortKey]`, as `EmailClaim` of [the shop](getting-started.md#the-shop) has:

```csharp
[DynamoDbEntity(Table = ShopTable.Name)]
[Discriminator("EMAIL")]
[PartitionKey("EMAIL#{Email}")]
[SortKey("CLAIM")]
public partial record EmailClaim(string Email, string CustomerId);
```

```csharp
TransactMarker emailTaken = default;
try
{
    await client.TransactWrite()
        .Unique(emails, new EmailClaim(customer.Email, customer.Id), out emailTaken).ReturnOldOnFailure()
        .Put(customers, customer).IfNotExists()
        .SendAsync(ct);
}
catch (TransactionCanceledException exception) when (exception.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
{
    Console.WriteLine(exception.ItemFor(emailTaken)!.To<EmailClaim>().CustomerId);
}
```

`Put` returns a step that takes the clauses of a put, `Update` one of an update, and `Delete` and `Check` one with conditions only. Each step offers the verbs of the next action as well. `Unique(table, item, out marker)` is a put with `attribute_not_exists` on the partition key. `TransactionCanceledException` has `ReasonFor(marker)`, `ItemFor(marker)` and `Failures`, the reason of every action.

`PutAll(table, items)` adds a put of each item, as many `Put` steps without clauses would. The sequence is enumerated at the call, and the put that would be action 101 throws `InvalidOperationException`, so a longer list takes a transaction per chunk:

```csharp
foreach (var chunk in catalog.Chunk(99))
{
    await client.TransactWrite()
        .Check(customers, Customer.Keys.Of("admin")).IfExists()
        .PutAll(products, chunk)
        .SendAsync(ct);
}
```

With `DynaSharp.Coordination`, `IsHeld(lease)` is a step too: a condition check on the item of a lock, so the transaction commits only while a lease holds it. See [Fencing](coordination.md#fencing).

## TransactGetItems

Two gets can see two items at two different moments. `TransactGet` reads up to 100 items as of one point in time. Each get hands out a slot, which reads its item from the result, with its type.

```csharp
var snapshot = await client.TransactGet()
    .Get(customers, Customer.Keys.Of("bob"), out var buyerSlot)
    .Get(carts, Cart.Keys.Of("bob"), out var cartSlot)
    .SendAsync(ct);

Customer? buyer = snapshot.Read(buyerSlot).Item;
```

## Batches

**Batches of any size.** A batch cuts its work into requests of 25 puts or 100 gets and sends again what DynamoDB left unprocessed, with bounded retries.

```csharp
var written = await products.BatchWrite().PutAll(catalog).SendAsync(ct);
var found = await products.BatchGet().GetAll(skus.Select(Product.Keys.Of)).SendAsync(ct);
```

A batch deletes by key the same way, and one batch can put and delete:

```csharp
await products.BatchWrite().DeleteAll(skus.Select(Product.Keys.Of)).SendAsync(ct);
await products.BatchWrite().PutAll(catalog).Delete(Product.Keys.Of("KB-01")).SendAsync(ct);
```

A delete takes a generated key or the entity itself, so what a query read goes back into a batch as it is:

```csharp
var history = await orders.Query(Order.Keys.Pk.Of("alice")).ItemsAsync(ct).ToListAsync();
await orders.BatchWrite().DeleteAll(history).SendAsync(ct);
```

`written` is a `BatchWriteResult` with `Written`, `Requests` and `ConsumedCapacity`. Getting it means every write was applied, and what cannot be written ends in a `BatchIncompleteException`.

A batch of a `[Version]` entity cannot carry the condition of the version, so it takes `IgnoreVersion()`, see [Optimistic locking](commands.md#optimistic-locking).
