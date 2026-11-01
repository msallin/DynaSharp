> Historical record, frozen: this file is not updated, and it holds review dialogue and designs that were dropped.
> Replaced by [api.md](../api.md), which states the public API as shipped.

# DynaSharp - API design

Binding contract for the public API. The evidence (measurements, prototype, alternatives) is in `api-layers-proposal.md`; the reasoning below is an independent review of that proposal. Where the two differ, this file wins. The owner adopted the direction on 2026-10-31: the table API is the primary API, the request API stays as a sibling.

Lead decisions on the review: every item under "Must land" and "Should land" in section 8 is accepted as written, with these notes:
- `HttpMessageInvoker` stays out. Views, streaming callbacks and deferred commands are not shipped.
- `AttributeNameTable` stays until the shape-churn benchmark proves `ItemShape` at least as good under many shapes.
- `DynaSharp.Expressions` holds the composition structs; `Attr`, `Condition`, `SortKey` are in `DynaSharp`.
- Compile-time templates are not planned. The analyzer (`DYNA201`, `DYNA210` to `DYNA214`) is planned after the command API settles.
- Requests stay `Task`-returning extension methods in `DynaSharp.Requests`; `DynamoDbStreamsClient` moves to `DynaSharp.Streams`.

Scope: `spec/api-layers-proposal.md`, the prototype in `src/DynaSharp/Spike`, `samples-compare/`, and the lead specs. Labels: **FACT** (read in the sources or a language rule), **INFERENCE** (follows from facts, not verified by running code), **OPINION** (taste; I say what I would pick and why). Where the proposal cites a measurement I quote it as FACT [proposal, M].

The short version: the direction is right, the engine is sound, and the usability problems are all at the surface. Most of them can be fixed with types rather than docs. Section 8 has the ordered list; sections 1 to 7 give the reasons.

---

## 1. The shape of the API

### 1.1 What stays

- `Table<T>` as the one engine with two codecs, `Item` and generated entities. FACT: `TableOperations.cs` already does this and the samples read well.
- Keys as parameters (`Update(pk, sk)`, `Update(Customer.Keys.Of(id))`). FACT: the generated key struct through `IKey` removes the inference problem of generator design section 6 and costs no string.
- Expression text rendered straight to UTF-8 segments, every name a placeholder, values deduplicated by bytes. FACT: reserved words stop being a user problem (`status`, `size`, `order` ran on both emulators [proposal, M]).
- Paging as `IAsyncEnumerable<T>` that follows filtered empty pages. FACT, and it fixes the most common DynamoDB bug in user code.
- No `ToRequest()` on commands, no stacking of one API on the other. Agreed.

### 1.2 Fixes to the shape, with reasons

**Indexes get their own type.** FACT: `Table<T>.Index(...)` returns `Table<T>` (TableOperations.cs line 47), so `shop.Index("StatusIndex", ...).Put(...)` compiles and fails at DynamoDB. OPINION: `Index<T>` with `Query`, `Scan` and nothing else. Compile error over `ValidationException`. For typed tables the generator knows the index key names from `[GlobalIndexKey]`, so `orders.Index("StatusIndex")` needs no names (M2b).

**Attribute-by-attribute `Put()` only on `Table<Item>`.** FACT: `Put()` without an item and `.With(name, value)` exist on `PutCommand<T>` for every `T` (lines 62, 293). On `Table<Order>` that writes an item with no `_type` and no key check. OPINION: make `Put()` and `With` extension methods on `Table<Item>` and `PutCommand<Item>` (extension methods on ref struct receivers are legal; `MemoryExtensions` on `Span<T>` is the precedent, FACT). Then `orders.Put()` does not exist, and `orders.Put(order)` is the only put a typed table has.

**`Of<T>()` requires an entity.** FACT: `Of<TOther>()` is constrained to `IDynamoDbDocument<TOther>` (line 43), so `shop.Of<Address>()` of a key-less document compiles. INFERENCE: `Put(address)` then fails at DynamoDB for a missing key. Constrain `Of<T>()` and `client.Table<T>()` to `IDynamoDbEntity<T>`; `Table<T>` itself keeps `IDynamoDbDocument<T>` because `Item` is not an entity.

**Return values.** `SendAndReadAsync<int>("orderCount")` is poor because it names the mechanism, not the DynamoDB concept, and FACT: `UpdateValueOp.ReadResponse` returns `default(TValue)` when the attribute is absent (TableOperations.cs lines 101-129), so a `REMOVE` followed by `SendAndReadAsync<int>` yields 0 silently. Also FACT: `UPDATED_NEW` is a partial item, which cannot be read into a generated entity with required members (generator design section 4 throws `DynamoDbMappingException.Missing`). So the typed path has exactly two safe shapes: the whole item (`ALL_NEW`, `ALL_OLD`) read as `T`, or one attribute read through its handle. OPINION, the family:

```csharp
.ReturnOld()                 // Put, Update, Delete: ALL_OLD, result.Attributes is T?
.ReturnNew()                 // Update: ALL_NEW, result.Attributes is T?
.Return(attr)                // Update: UPDATED_NEW of one attribute, typed; ends the chain, SendAsync gives TValue
.ReturnOldOnFailure()        // the condition-failure item on ConditionalCheckFailedException.Item
```

`Return(attr)` returns a small ref struct with only `SendAsync(ct)` and the awaiter, so clauses after it do not compile. A missing attribute in the response throws `DynamoDbMappingException` naming the attribute, never `default`.

**Conditions: two styles, one rule for when to use which.** Keep struct conditions (`Attr("status").Eq("SHIPPED")`, `Order.Attrs.Status.Eq(OrderStatus.Shipped)`) and the interpolated form (`$"{Attr("status")} = {"SHIPPED"}"`). OPINION on the rule: struct conditions for comparisons, existence and `begins_with`, where a method exists; interpolation for the rest of the grammar (`size`, `contains`, `attribute_type`, `list_append`, arithmetic in `SET`). Docs lead with struct conditions for typed tables and with interpolation for `Item`, because the untyped table has no handles and the interpolated form reads like the DynamoDB reference. Drop the separate `Name` struct: `Attr` in a hole already renders as a path (FACT: `ExpressionHandler.AppendFormatted(Attr<,>)`), and one type that works in both styles is one fewer thing to learn.

**Vocabulary for untyped handles.** `using static DynaSharp.Expr;` gives `Attr("status")` (an `AttributeValue`-typed handle, so `.Eq("SHIPPED")`, `.Gt(100)` work through the implicit conversions) and `Attr<int>("orderCount")` (a number handle, needed for `Return(...)` and typed `Add`). Two overloads of one name, by arity.

**`IfNotExists()` and `IfExists()` instead of `IfNew()`.** OPINION: those are the DynamoDB function names, searchable in the AWS docs and consistent with `SetIfNotExists`. On a transaction they need no table argument: FACT, the spike's `IfNew<T>(Table<T>)` (line 1162) takes the table only to learn the partition key name, which `Begin(...)` already saw; keep it in the state.

**Terminal verb.** Keep `SendAsync(ct)` as the documented terminal on every command (it mirrors `SendRawAsync`, DynamoDB Toolbox's `send()` and the wire vocabulary), and keep `GetAwaiter()` as sugar for `await shop.Put(x).SendAsync()`, marked `[EditorBrowsable(Never)]` so it does not appear in IntelliSense. OPINION: two terminals cost one sentence of docs; the awaiter form is what makes the counter scenario one line and it costs nothing. Every async terminal takes a `CancellationToken`: `SendAsync(ct)`, `ItemsAsync(ct)` (also `[EnumeratorCancellation]`), `PagesAsync(ct)`, `PageAsync(token, ct)`.

**Transactions: named `TransactWrite()` and `TransactGet()`, with step types and typed markers.** Three problems in the flat form, all fixable with types:

- FACT: `.Add(...)` after `.Put(...)` compiles and fails at DynamoDB (proposal section 4, "one wart"). Fix: `Put` returns a `PutStep`, `Update` an `UpdateStep`, `Delete` and `Check` a `ConditionStep`; each exposes only its own clauses plus the next-action verbs and `SendAsync`. Four small ref structs, all over the same state. Compile error over `ValidationException`.
- FACT: the marker is an `int` (`out int action`, line 1090). Replace with `readonly struct TransactMarker` and typed `TransactSlot<T>` for gets, and put the lookup on the exception: `e.ReasonFor(marker)` returning `TransactionFailure`, `e.ItemFor(marker)` returning `Item?`, plus `e.Failures` listing `(index, reason)` for logging. Keep `CancellationReasons` as the raw list.
- FACT: `TransactionCommand.SendAsync` returns `ValueTask<byte>` (line 1192). It must return `ValueTask<TransactWriteResult>` with `ConsumedCapacity` and `ItemCollectionMetrics`, or plain `ValueTask` if those are dropped; `byte` is a prototype artefact.

**Typed Query and Scan filter on the discriminator.** INFERENCE: `customers.Query("CUSTOMER#alice")` without a sort key condition returns the PROFILE and all ORDER items of that partition, and the typed read throws `DynamoDbMappingException` on the first order (requirements, "A typed read checks `_type`"). That is a day-1 surprise in exactly the single-table design the library is built for. OPINION: a typed `Query` or `Scan` on an entity with a discriminator adds `#_type = :t` to the filter automatically. It costs two placeholders and 0 B, does not change consumed capacity (filters run after the read), is visible in the recorded request, and is what ElectroDB does. Mixed-type reads go through `Table<Item>` now and the M3 mixed query later. Document that `ScannedCount` then exceeds `Count`.

**Keys from entities.** `customers.Delete(customer)` and `customers.Get(customer)` taking the key from the entity through `WriteKey`, as extension methods constrained to `IDynamoDbEntity<T>`. Cheap, and it is how a "reload" or "delete what I hold" reads.

**Continuation tokens round-trip.** FACT: `ContinuationToken.ToString()` is base64 and there is no `Parse` (lines 639-651). Stateless paging over HTTP needs `ContinuationToken.Parse(string)` and `TryParse`, and a bridge to the request API (`ToKey()` as `Item`, `FromKey(Item)`). Document that the token carries the key values of the last item, as `LastEvaluatedKey` does in every SDK.

### 1.3 The final API, every operation

```csharp
using DynaSharp;                         // client, Table<T>, Item, AttributeValue, commands, exceptions
using static DynaSharp.Expr;             // Attr("x"), Attr<int>("n")

using var client = new DynamoDbClient(options);

// ---- entry points -------------------------------------------------------------------------
Table<Item>     shop      = client.Table("Shop", "pk", "sk");
Table<Customer> customers = shop.Of<Customer>();            // same table, key names from Customer
Table<Order>    orders    = client.Table<Order>("Shop");    // the same thing from the client
Index<Order>    byStatus  = orders.Index("StatusIndex");    // typed: key names from [GlobalIndexKey]
Index<Item>     byStatus2 = shop.Index("StatusIndex", "status", "placedAt");

// ---- Get ----------------------------------------------------------------------------------
GetResult<Item> got = await shop.Get("CUSTOMER#alice", "PROFILE").Consistent().Project("fullName", "city").SendAsync(ct);
if (got.Found) Console.WriteLine(got.Item.GetString("fullName"));

GetResult<Customer> alice = await customers.Get(Customer.Keys.Of("alice")).SendAsync(ct);
Customer? reloaded = (await customers.Get(existing).SendAsync(ct)).Item;        // key from the entity

// ---- Put ----------------------------------------------------------------------------------
await shop.Put(new Item { ["pk"] = "CUSTOMER#alice", ["sk"] = "PROFILE", ["fullName"] = "Alice" }).SendAsync(ct);
await shop.Put().With("pk", "CUSTOMER#alice").With("sk", "PROFILE").With("fullName", "Alice")     // Table<Item> only
    .IfNotExists().SendAsync(ct);
await customers.Put(new Customer("alice", "Alice")).IfNotExists().ReturnOldOnFailure().SendAsync(ct);
WriteResult<Customer> replaced = await customers.Put(customer).ReturnOld().SendAsync(ct);         // replaced.Attributes: Customer?

// ---- Update -------------------------------------------------------------------------------
await shop.Update("CUSTOMER#alice", "PROFILE")
    .Set("city", "Bern")
    .Add("orderCount", 1)
    .Remove("legacyFlag")
    .If(Attr("version").Eq(3))                                   // struct condition
    .SendAsync(ct);

await shop.Update(pk, sk)
    .Set("tags", ...)
    .If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) < {10}")   // the grammar, for functions
    .SendAsync(ct);

int count = await customers.Update(Customer.Keys.Of("alice"))
    .Add(Customer.Attrs.OrderCount, 1)
    .MaxAttempts(1)                                              // an ADD must not count twice
    .Return(Customer.Attrs.OrderCount)                           // UPDATED_NEW, one attribute, typed; ends the chain
    .SendAsync(ct);

int count2 = await shop.Update(pk, sk).Add("orderCount", 1).Return(Attr<int>("orderCount")).SendAsync(ct);
WriteResult<Customer> after = await customers.Update(key).Set(Customer.Attrs.Email, email).ReturnNew().SendAsync(ct);

// ---- Delete -------------------------------------------------------------------------------
await shop.Delete("CUSTOMER#alice", "PROFILE").If(Attr("orderCount").Eq(0)).SendAsync(ct);
await customers.Delete(customer).SendAsync(ct);
WriteResult<Customer> gone = await customers.Delete(Customer.Keys.Of("alice")).ReturnOld().SendAsync(ct);

// ---- Query, items (follows pages, filtered empty pages included) ---------------------------
await foreach (Order order in orders
    .Query(Order.Keys.Pk.Format("alice"), SortKey.BeginsWith(Order.Keys.Sk.Prefix()))
    .Filter(Order.Attrs.Status.Eq(OrderStatus.Shipped))        // plus #_type = ORDER, added for entities
    .Descending()
    .Limit(25)                                                   // per page, as DynamoDB means it
    .ItemsAsync(ct)) { }

// ---- Query, one page (stateless paging over HTTP) ------------------------------------------
Page<Order> page = await orders.Query(pk, SortKey.BeginsWith("ORDER#")).Limit(25)
    .PageAsync(ContinuationToken.Parse(tokenFromClient), ct);
page.Items; page.Count; page.ScannedCount; page.ConsumedCapacity; page.HasMore; page.Next.ToString();

// ---- Query, all pages in process -----------------------------------------------------------
await foreach (Page<Order> p in orders.Query(pk).PagesAsync(ct)) { }

int total = await orders.Query(pk).CountAsync(ct);               // Select COUNT, sums over pages

// ---- Index --------------------------------------------------------------------------------
await foreach (var o in byStatus.Query("SHIPPED", SortKey.Ge("2026-01-01")).Project(...).ItemsAsync(ct)) { }

// ---- Scan ---------------------------------------------------------------------------------
await foreach (Item item in shop.Scan()
    .Filter($"begins_with({Attr("sk")}, {"ORDER#"}) AND {Attr("amount")} > {100}")
    .ItemsAsync(ct)) { }
Page<Item> segmentPage = await shop.Scan().Segment(2, of: 8).PageAsync(token, ct);     // one segment, paged by the caller
await foreach (Item item in shop.Scan().Parallel(segments: 8).ItemsAsync(ct)) { }      // 8 segments concurrently, merged, unordered
// Parallel(...) returns ParallelScanCommand<T>: ItemsAsync only, no PageAsync (a merged page has no single key)

// ---- BatchGet (chunked by 100, unprocessed keys retried with backoff) ----------------------
BatchGetResult<Customer> found = await customers.BatchGet()
    .Key(Customer.Keys.Of("alice"))
    .Keys(ids.Select(Customer.Keys.Of))                          // any IEnumerable<TKey>
    .Consistent()
    .Project(Customer.Attrs.FullName)
    .SendAsync(ct);
found.Items;                                                     // IReadOnlyList<Customer>, unordered; missing keys are absent
found.ConsumedCapacity;

// ---- BatchWrite (chunked by 25, unprocessed writes retried, bounded) -----------------------
BatchWriteResult written = await shop.BatchWrite()
    .Put(item1).Put(item2)
    .Delete("CUSTOMER#x", "PROFILE")
    .Put(orders, order)                                          // another type, or another table, in the same batch
    .SendAsync(ct);                                              // BatchIncompleteException holds what was never written

// ---- TransactGet --------------------------------------------------------------------------
TransactGetResult snapshot = await client.TransactGet()
    .Get(customers, Customer.Keys.Of("alice"), out TransactSlot<Customer> aliceSlot)
    .Get(orders, Order.Keys.Of("alice", 1), out TransactSlot<Order> orderSlot)
    .Get(shop, "TENANT#1", "QUOTA", out TransactSlot<Item> quotaSlot)
    .SendAsync(ct);
Customer? a = snapshot.Read(aliceSlot).Item;                     // parsed on demand from the kept response

// ---- TransactWrite (one ClientRequestToken per SendAsync, so retries are idempotent) -------
TransactMarker emailTaken = default;                                       // declared outside, so the catch filter can see it
try
{
    await client.TransactWrite()
        .Unique(markers, new EmailMarker(email, id), out emailTaken).ReturnOldOnFailure()   // put with attribute_not_exists
        .Put(customers, new Customer(id, name, email)).IfNotExists()
        .Update(customers, Customer.Keys.Of("admin")).Add(Customer.Attrs.OrderCount, 1).If(Customer.Attrs.OrderCount.Lt(100))
        .Check(shop, "TENANT#1", "QUOTA").If(Attr("remaining").Gt(0))
        .Delete(shop, "CART#x", "ITEM#1")
        .SendAsync(ct);
}
catch (TransactionCanceledException e) when (e.ReasonFor(emailTaken) == TransactionFailure.ConditionalCheckFailed)
{
    string? holder = e.ItemFor(emailTaken)?.GetString("customerId");
}

// ---- fan-out query (M3) -------------------------------------------------------------------
await foreach (Order o in orders.Query(shardKeys, SortKey.Between(from, to)).Descending().ItemsAsync(ct)) { }
// shardKeys: ReadOnlySpan<string> or IEnumerable<TKeyPart>; k-way merge by sort key; PageAsync returns a composite token

// ---- per-call options, on every command where DynamoDB has them ------------------------------
.MaxAttempts(1)                                   // this call only
.Consistent()                                     // Get, Query, Scan, BatchGet, TransactGet is always consistent
.ReturnCapacity(ReturnConsumedCapacity.Total)     // result.ConsumedCapacity, also the metric
.ReturnMetrics()                                  // ItemCollectionMetrics on writes
.SendAsync(ct) / .ItemsAsync(ct) / .PagesAsync(ct) / .PageAsync(token, ct) / .CountAsync(ct)

// ---- escape hatches -----------------------------------------------------------------------
byte[] json = await client.SendRawAsync("ListTables", """{"Limit":1}"""u8.ToArray(), ct);
Item asItem = Item.From(customer);  Customer back = asItem.To<Customer>();   // typed <-> Item, both directions
```

### 1.4 Does it stay true to DynamoDB?

FACT: every verb is a DynamoDB operation or expression clause (`Set`, `Add`, `Remove`, `Delete`, `If`, `Filter`, `Project`, `Limit`, `Consistent`, `Return*`, `Segment`). `Put` replaces the whole item, `Update` is an upsert unless `IfExists()`, `Limit` is per page, batches are not atomic and the result says how many requests were sent, transactions carry one token. There is no `Save`, no identity map, no lazy load, no implicit read before write. INFERENCE: the only hidden behaviour after my changes is the discriminator filter on typed queries, and it is visible in the request. `[Version]` enforcement (M3) is opt-in per type and will add a condition the user can see with `ToRequestJson()` (section 7).

Two places where DynamoDB semantics must be stated in the docs because the API cannot express them: a filtered `ItemsAsync()` consumes capacity for scanned items, not returned ones; and `MaxAttempts(1)` is the only way to make `Add` exactly-once, because a retry after a lost response repeats it (the client docs already say this well).

---

## 2. How a developer knows which API is in use

### 2.1 The problem today

FACT: `DynamoDbClient` has one `XxxAsync(XxxRequest)` per operation (about 20), plus `Table`, `Table<T>`, `Transaction` and `SendRawAsync` in the spike. INFERENCE: a developer who comes from the AWS SDK types `client.` and sees `GetItemAsync` first, alphabetically and by familiarity, and never meets `Table`. The primary API is not primary in IntelliSense.

### 2.2 Decision: three namespaces, the request API as extension methods

OPINION, with reasons:

| Namespace | Holds | Who imports it |
|---|---|---|
| `DynaSharp` | `DynamoDbClient`, `DynamoDbClientOptions`, `AwsRegion`, exceptions, `Table<T>`, `Index<T>`, all commands and results, `Item`, `AttributeValue`, `AttributeType`, `Attr`, `Condition`, `SortKey`, `ContinuationToken`, `Page<T>`, `Expr`, enums both APIs share (`ReturnConsumedCapacity`, `ConsumedCapacity`, `TableStatus`) | everyone |
| `DynaSharp.Expressions` | the composition structs `Compare<,>`, `And<,>`, `Or<,>`, `Not<>`, `Between<,>`, `AttributeExists`, `BeginsWith`, `ICondition`, `ExpressionSink`, `ExpressionHandler`, `Arg<,>` | nobody by hand; they are return types |
| `DynaSharp.Requests` | every `XxxRequest`/`XxxResponse` record and structure type, and the `client.XxxAsync(request)` methods as extension methods on `DynamoDbClient` | migrators from the AWS SDK or EfficientDynamoDb, admin code, anything unmodeled |
| `DynaSharp.Streams` | `DynamoDbStreamsClient` and its records | stream consumers |
| `DynaSharp.Mapping`, `DynaSharp.Serialization`, `DynaSharp.Keys`, `DynaSharp.Credentials` | as designed | generator output, custom converters, credentials setup |

What `using DynaSharp;` gives you: `client.` shows `Table`, `Table<T>`, `TransactWrite`, `TransactGet`, `SendRawAsync`, `Dispose`. That is the whole primary API in six members. Add `using DynaSharp.Requests;` and the twenty `XxxAsync(XxxRequest)` methods appear, each tagged as an extension in IntelliSense. Rule for placing a type: it is in `DynaSharp` iff a `Table<T>` user has to name it.

Why extension methods and not `client.LowLevel` as EfficientDynamoDb does: a property adds a hop on every call and a type that exists only to hold methods; extension methods are the idiomatic .NET way to layer an API by namespace (`Microsoft.Extensions.*`, `System.Linq`), they keep the XML docs and the `DynamoDbClient` receiver, and they let the pipeline stay `internal` (same assembly). Telemetry does not change: both APIs run the same operations.

Why `Requests` and not `LowLevel`: it names what the layer is (requests and responses that mirror the service), not a judgement about it. The spec may keep calling it the low-level API in prose.

`DynaSharp.Model` goes away: `AttributeValue` moves to the root (the `Item` user needs it on day 1), the request records to `DynaSharp.Requests`. This is a pre-1.0 move and the right time for it is now; moving `AttributeValue` later would break every consumer.

### 2.3 How the three are told apart in code

| | Request API | `Table<Item>` | `Table<Entity>` |
|---|---|---|---|
| Entry | `client.GetItemAsync(new GetItemRequest { ... })` | `client.Table("Shop", "pk", "sk")` | `shop.Of<Order>()`, `client.Table<Order>("Shop")` |
| Argument shapes | `XxxRequest` records, `IReadOnlyDictionary<string, AttributeValue>` | `AttributeValue` keys, `string` paths, `Item` | `Order.Keys.Of(...)`, `Order.Attrs.X`, `Order` |
| Result shapes | `XxxResponse` | `GetResult<Item>`, `WriteResult<Item>`, `Page<Item>` | the same over `Order` |
| Async type | `Task<T>` | `ValueTask<T>` | `ValueTask<T>` |
| Where it fails on a bad expression | DynamoDB (`ValidationException`) | DynamoDB, with the rendered expression attached (section 5) | compile time for handles, DynamoDB for raw paths |

The request shape is the tell: if you wrote `new XxxRequest`, you are on the request API. If you chained verbs off a table, you are on the command API, and `T` says which codec.

### 2.4 Migration between them

- Request to `Table<Item>`: `Item` implements `IReadOnlyDictionary<string, AttributeValue>` (FACT), so an `Item` goes into any request, and request responses can return `Item` behind the interface (FACT: the slot-in passed 280 of 280 integration tests [proposal, M]). `ContinuationToken.ToKey()`/`FromKey(Item)` bridges paging.
- `Table<Item>` to `Table<Entity>`: `Item.From(entity)` exists (FACT); add `item.To<T>()`. Same table object, `.Of<T>()`, keys become `T.Keys.Of(...)`, string paths become `T.Attrs.X`; raw string paths keep working on typed commands as the escape hatch the requirements ask for.
- From the AWS SDK: the `DynaSharp.Requests` names match botocore's (FACT), so a search-and-replace migration lands on the request API first, then moves table by table.

### 2.5 The docs story

One page per layer, in this order on the landing page: "Tables" (the `Table<T>` API, 90 % of readers stop here), "Entities" (the generator), "Requests" (the mirror, with a sentence saying when: admin, Streams, unmodeled parameters, migration), "Raw" (`SendRawAsync`). The `samples/DynaSharp.Sample` tour is rewritten on `Table<T>` with one section that drops to `Requests` for `CreateTable` and Streams, so a reader sees the switch once and knows what it looks like.

---

## 3. The ref struct command

### 3.1 Is the trade worth it?

Measured gain: FACT [proposal, M] the deferred struct costs 0.27 to 0.4 KB more per call, on a floor of 1.41 KB canned and 2.71 KB on sockets. That alone is 10 to 15 % and would not justify the usability cost.

What justifies it is safety, not bytes. INFERENCE: the pooled `CommandState` is what makes the write path 0 B; a plain struct or class handle over pooled state can be copied, stored, and used after the state was released to another request, which is silent data corruption across requests. The version check catches a stale handle, but only on the next fluent call; a handle kept in a field and read later would race. The ref struct turns those bugs into compile errors. So: yes, worth it, on the condition that the three things developers will try are all possible, documented, and the compile errors are explained. They are, see 3.3.

### 3.2 What cannot be written, and what to write instead

| Attempt | Result | What to write instead |
|---|---|---|
| Store a command in a field or property | compile error, CS8345 (FACT, language) | a method that builds it: `UpdateCommand<Customer> IncrementOrders(string id) => customers.Update(Customer.Keys.Of(id)).Add(Customer.Attrs.OrderCount, 1);` The method is the template. |
| Keep one across an `await` | compile error, CS4007 when the command is in the awaited expression and reused (FACT [proposal, M]); related CS4012/CS4013 forms | compute every value first, then build and send in one synchronous stretch. `if (x) cmd = cmd.Set(...)` between is fine (FACT [proposal, R], and correct: each call returns a new handle to the same local). |
| Capture in a lambda, LINQ `Select`, local function that is async | compile error, CS8175 (FACT [proposal, M]) | build inside the lambda: `pipeline.ExecuteAsync(ct => customers.Update(k).Add(a, 1).SendAsync(ct))`. For Polly this is also the correct semantics: a retry must rebuild, because the buffer is released after send. |
| Pass to an async helper | compile error: a ref struct parameter is not allowed in an async method (FACT, language) | a synchronous helper that takes and returns the command (below), then the caller awaits. |
| Reuse one command twice | `InvalidOperationException` from `CommandState.Check` (FACT) | build twice, or `Condition` on the heap for the part worth sharing. |
| Enumerate a query twice | `InvalidOperationException` "A query can be enumerated once" (FACT) | call `ItemsAsync()` again on a new command. |
| Mock it | cannot: sealed `Table<T>`, no interface, value handles | the wire is the seam (section 6); wrap your data access in your own interface, which is the right boundary anyway. |

### 3.3 Helper-method composition, which does work

FACT (language): extension methods may take a ref struct receiver by value, ref structs may implement interfaces since C# 13, and a generic method with `where TCommand : allows ref struct` may call interface members on it without boxing. That gives three forms of helper, all zero-allocation:

```csharp
// 1. per command type: an extension method
public static UpdateCommand<T> Audited<T>(this UpdateCommand<T> cmd, string user) where T : IDynamoDbDocument<T> =>
    cmd.Set("updatedBy", user).Set("updatedAt", DateTimeOffset.UtcNow);

await customers.Update(key).Set(Customer.Attrs.Email, e).Audited(user).SendAsync(ct);

// 2. across command types: an interface the commands implement
public interface IConditional<TSelf> where TSelf : IConditional<TSelf>, allows ref struct
{
    TSelf If<TCondition>(TCondition condition) where TCondition : ICondition;
}

public static TCommand OnlyIfOwned<TCommand>(this TCommand cmd, string owner) where TCommand : IConditional<TCommand>, allows ref struct =>
    cmd.If(Attr("owner").Eq(owner));

// 3. a condition built at run time, kept in a field: Condition (heap, one object)
private static readonly Condition Open = Condition.Of(Attr("status").Eq("open"));
await shop.Update(pk, sk).Set("status", "closed").If(Open & Attr("version").Eq(v)).SendAsync(ct);
```

Decision: `PutCommand<T>`, `UpdateCommand<T>`, `DeleteCommand<T>`, the transaction steps and `QueryCommand<T>`/`ScanCommand<T>` (for `Filter`) implement small public interfaces (`IConditional<TSelf>`, `IUpdatable<TSelf>`, `IRetryable<TSelf>`) with the `allows ref struct` anti-constraint on their type parameter. These interfaces are the extension points for day 300 (audit fields, tenant conditions, soft-delete filters) and the whole answer to "I want a helper that adds conditions". They cost nothing at run time.

### 3.4 Error messages, and help

FACT: the compiler messages say "ref struct" or "cannot be used in an async method"; none says why DynaSharp did this. Three mitigations, in order of value:

1. A Roslyn analyzer in the generator assembly (it already ships as an analyzer) reports `DYNA201` next to any CS4007/CS4012/CS4013/CS8175/CS8345 whose type is a DynaSharp command: "A DynaSharp command is built and sent in one expression. Compute values first, or move the chain into a method. See <link>." Analyzers cannot suppress compiler errors, but a second diagnostic with a help link on the same span is what the developer reads (FACT, Roslyn).
2. The run-time message in `CommandState.Check` is already good (FACT). Add the operation and table: "This PutItem on Shop was already sent or extended."
3. `[DebuggerDisplay]` on every command rendering the body built so far, and the same on `CommandState`. A developer stepping through a chain today sees byte arrays.

### 3.5 Two prototype details that must change before anything is built

- FACT: `BatchWriteCommand.Run` uses `Task.Delay` with `Random.Shared` (TableOperations.cs line 1361). It must use the client's `TimeProvider` and the retry policy's jitter, or consumers cannot test unprocessed-item retries without waiting, and the `StubHandler` docs promise exactly that test.
- FACT: `Table<T>.WaitUntilActiveAsync` polls with a fixed 250 ms `Task.Delay` (line 128). Same fix.

---

## 4. Item ergonomics

Construction. FACT: both `new Item { ["pk"] = "x" }` (indexer setter) and `new Item { { "pk", "x" } }` (`Add`) compile, and values convert implicitly. Docs pick the indexer form: it is what the request API and the AWS SDK use, so code moves between layers unchanged. `Item.From(entity)` exists; add `item.To<T>()`.

Reading. FACT: `item["x"]` throws `KeyNotFoundException` naming the attribute; `GetString`, `GetNumber<T>`, `GetBool`, `GetMap` return null when missing. Good. Three gaps:
- `GetNumber<int>` on `"19.90"` throws a bare `FormatException` from `INumberBase<int>.Parse` (INFERENCE). Wrap it so the message names the attribute and the text.
- Nested access reads `item.GetMap("address")?.GetString("city")`. Fine, keep it; do not add dotted paths to getters, because attribute names may contain dots.
- Lists: `item["tags"].AsList()` exists on `AttributeValue`; add `GetList`, `GetStringSet` for symmetry with the other getters.

Printing. FACT: `Item` does not override `ToString()`, so interpolation prints `DynaSharp.Item`. The sample needed an `Output.Show` helper for dictionaries (FACT, P10 friction). Must: `ToString()` returns the DynamoDB JSON of the item (`AttributeValue.ToString` already does for a value), plus `[DebuggerTypeProxy]` showing name, type and value per row.

Numbers. Numbers stay text until read, which is the right call (FACT, decided). `AsNumber<T>()` parses on every call; fine. The `char` footgun: FACT (tasks.md P14) `'a'` becomes 97 through the `int` conversion. Add `[Obsolete("...", error: true)] public static implicit operator AttributeValue(char)`.

Mutation and identity. `Item` is mutable, compares by content, and its hash changes with content (FACT). Document: do not use as a dictionary key or in a `HashSet`. Writing into a page item copies the shared names first (FACT), so page items are independent.

Order. FACT: both emulators returned one attribute set in different orders, and the shape defines the value order. Document "no order" for read items and insertion order for hand-built ones, and make `Equals` the only sanctioned comparison.

Size. FACT: above 32 attributes a hand-built `Item` costs more than a sized `Dictionary` and the lookup index is per item. Acceptable; say so in the `Item` remarks for import jobs.

---

## 5. Errors

What a failed call tells you today: FACT, `DynamoDbException` carries `ErrorCode`, `StatusCode`, `RequestId`, `OperationName`, `TableName` and the service message. That is better than the AWS SDK.

What is missing for the command API. INFERENCE: with every name a placeholder, DynamoDB's validation messages become "attribute value :v3 is not defined" or "Syntax error; token: ':v1'", and the developer has no way to map `#n2`/`:v3` back to what they wrote. Decision: when a command fails with `ValidationException`, the exception gets `Expressions` (the rendered `ConditionExpression`, `UpdateExpression`, `FilterExpression`, `KeyConditionExpression`, `ProjectionExpression`) and `ExpressionNames` (`#n0 = status`). Values are not attached, by the telemetry rule's spirit, only `:v3 (N)` with the type. The message gains one line: "Expression: #n0 = :v0 AND #n1 > :v1, where #n0 = status, #n1 = total".

The forgotten-`Attr` mistake. FACT (corrected by the analyzer work against DynamoDB Local 3.3.2 and Dynoxide): a plain string hole is a value, so `$"{"status"} = {"SHIPPED"}"` renders `:v0 = :v1`, which DynamoDB **accepts**. It compares two values, so the condition or filter is constant and the mistake fails silently. This is the number one day-1 error. The analyzer (below) catches the literal case at compile time; an expression that names no attribute at all is rejected at run time with `ArgumentException`, which catches the variable case.

Cancellation reasons. `e.ReasonFor(marker)` returning the `TransactionFailure` enum, `e.ItemFor(marker)` returning the old item when `ReturnOldOnFailure()` was set, and `e.Failures` for logs. Keep `CancellationReasons` with the string code because the service adds codes (FACT, decided). Add the actionable guidance to the enum's XML docs: `ConditionalCheckFailed` and `ValidationError` will not pass on retry; `TransactionConflict` and `Throttled` may.

A key built from bad input. Typed: `KeyFormatException` (an `ArgumentException`) at the call site, never containing the value (FACT, requirements). Untyped `Table<Item>`: an empty string key goes to DynamoDB and comes back as `ValidationException`. OPINION: check empty key strings client-side in `Update/Get/Delete(pk, sk)` and throw `ArgumentException` naming the parameter; it is cheap and puts the stack trace at the call site. Do not validate more than that; DynamoDB owns the rules.

Compile errors over run-time errors, the list: `Index<T>` without writes; `Put()`/`With` on `Table<Item>` only; transaction step types; `Return(attr)` ends the chain; `SortKey` only with legal operators (FACT, already); `Of<T>()` entities only; `Parallel()` without `PageAsync`. Each one is a `ValidationException` or a `DynamoDbMappingException` that no longer exists.

The analyzer for constant expression strings. Scope, in this order of value:
1. A string literal in a hole on the left of an operator or inside a function's path position (`$"{"status"} = ..."`, `$"attribute_exists({"pk"})"`) is almost certainly a name: warning `DYNA210`, fix: wrap in `Attr(...)`.
2. Literal parts containing `#` or `:` placeholders: error `DYNA211`, "DynaSharp generates placeholders; write the name in `Attr(...)` and the value in a hole".
3. Grammar of the literal parts: unbalanced parentheses, unknown function names (`attribute_exists`, `attribute_not_exists`, `attribute_type`, `begins_with`, `contains`, `size`, `if_not_exists`, `list_append`), `BETWEEN` without `AND`, `IN` arity: warning `DYNA212`.
4. On a typed table, a raw string path in `Set("Totl", ...)` that matches no generated attribute: warning `DYNA213` (raw names are legitimate for attributes outside the entity, so a warning, not an error).
5. A `Set`/`Add`/`Remove` path whose syntax is invalid (`a..b`, `a[`): warning `DYNA214`.

This is medium effort (a small expression grammar in the generator project) and it replaces most of the compile-time validation that the rejected template approach would have given (the proposal says so, [G]; I agree and would call it INFERENCE).

---

## 6. Testability for consumers

The seam is the wire and it serves both APIs (FACT, decided and prototyped). What a consumer testing code over `Table<Customer>` has to write today is the canned DynamoDB JSON for a `Customer` by hand. That is the real friction. Decision:

```csharp
// DynaSharp.Testing additions, all built on T.WriteAttributes, so they cannot drift from the codec
StubResponse.Item(customer);                                   // GetItem: {"Item":{...}}
StubResponse.Item<Customer>(null);                             // GetItem miss: {}
StubResponse.Items(orders, lastEvaluatedKey: Order.Keys.Of(...));   // Query/Scan page, Count and ScannedCount filled in
StubResponse.Attributes(customer);                             // Put/Update/Delete with ReturnValues
StubResponse.ConditionalCheckFailed(oldItem);                  // with the item, as ReturnValuesOnConditionCheckFailure gives it
StubResponse.TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed);   // P4 noted this gap
StubResponse.Unprocessed(keys or writes);                      // batch partial results
```

and on the recorded side, `RecordedRequest.Body` as a `JsonElement` plus `request.ReadItem<T>("Item")` and `request.ReadKey<T>()`, so an assertion reads "the put carried this customer" instead of comparing JSON strings.

How a developer tests code that uses `Table<T>`: `new DynamoDbClient(options with { HttpHandler = stub, MaxAttempts = 1, TimeProvider = fake })`, hand it to the code under test, queue responses, assert on `stub.Requests`. No network, no Docker, synchronous completion, 0 B of their own ceremony. Document this as the first thing in the Testing page, with the `MaxAttempts = 1` rule the `StubHandler` remarks already explain (FACT).

`ToRequestJson()` on commands (section 7) is the second test seam: "what would this chain send" without a client at all, which is also how a developer learns the API. It is a terminal, allocates one string, and is for tests and debugging only.

The fake (M4) stays where it is in the plan. It is the right tool for functional tests of the whole data layer, and nothing in the command API changes what it needs. Do not pull it forward; the typed `StubResponse` helpers are two days of work and cover unit tests now.

Mocking: `Table<T>` is sealed with an internal constructor (FACT) and gets no interface. Say so plainly in the docs with the reason (the handler is the seam, as decided) and the pattern (wrap data access in your own interface). The AWS SDK's `IAmazonDynamoDB` interface is what people will ask for, and the answer is the stub, which tests the serialization too.

---

## 7. What is wrong, over-engineered or missing

### Wrong or needs changing

- FACT: `TransactionCommand.SendAsync` returns `ValueTask<byte>`. Prototype artefact; see 1.2.
- FACT: `UpdateValueOp` returns `default` for a missing attribute. Must throw; see 1.2.
- FACT: `BatchWriteIncompleteException.UnprocessedItems` is raw bytes. A caller cannot do anything with them except `SendRawAsync` after wrapping them in `{"RequestItems":...}`. It is the error path, so parse: `IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>>`, the request API's own type, which `client.BatchWriteItemAsync` accepts directly. Same for `BatchGet`'s unprocessed keys.
- FACT: the process-wide shape table has 256 direct-mapped slots. INFERENCE: an application with more than a few hundred distinct attribute sets (multi-tenant schemaless data, a table with many entity types and optional attributes) thrashes it, and then every item allocates a validated names array and a shape, which is more than the per-response `AttributeNameTable` it supersedes. Before `AttributeNameTable` is removed, add a shape-churn benchmark (1000 distinct shapes, 100 items each) and, if it shows regression, keep a per-response shape cache as the first level. Cheap to measure, expensive to discover in production.
- FACT: `Table<T>.Index` returns `Table<T>`, `Put()`/`With` on every `T`, `Of<T>()` on documents: section 1.2.
- FACT: batch retry and `WaitUntilActiveAsync` bypass `TimeProvider`: section 3.5.
- `Transaction()` as the name while `TransactGet` also exists: `TransactWrite()` and `TransactGet()`.
- `Table<T>.Of<TOther>()` allocates a `Table` per call (FACT, `new`). Fine, but the sample calls it inline; the docs must say "hold tables as fields", or the table caches one `Table<TOther>` per type in a small `ConcurrentDictionary<Type, object>` (no reflection, AOT-safe). I would cache: the cost of forgetting is one allocation per request forever.

### Over-engineered, cut from the public surface

- `PageView`, `ItemView`, `ViewsAsync`, `ForEachAsync`, `ItemHandler`, `UnpooledViews`. The proposal itself says ship only if asked (FACT). Make them internal benchmark code or delete. A view that is cheaper only when you read little and pins a 1 MB page otherwise is a footgun the measured numbers do not justify (FACT: 13 KB against 98 KB only when three of ten attributes are read).
- `DeferredPut`, `DeferredUpdate`, `DeferredCommands`, `DeferredAction`: comparison code, delete.
- `ItemReadMode`, `ItemShape.LinearLimit` setters: benchmark knobs; freeze the chosen values as constants.
- `HttpMessageInvoker` instead of `HttpClient`: the proposal says optional and 0 B; skip it unless it simplifies the pipeline.

### Missing from the proposal

- Scan, parallel scan, BatchGet, TransactGet: acknowledged as unprototyped (FACT). Shapes above.
- `CountAsync()` (`Select: COUNT`), the cheapest way to answer "how many".
- `ContinuationToken.Parse` and the bridge to `LastEvaluatedKey`.
- `TransactGet` typed slots and a result that parses on demand.
- `ToRequestJson()` on every command: the rendered body as a string, without sending. ElectroDB's `params()` and DynamoDB Toolbox's `.params()` are the most praised debugging feature in both libraries, and the prototype already holds the finished body in segments, so it is a `Finish` and an `Encoding.UTF8.GetString`. It is not the `ToRequest()` the proposal rejects: no dictionaries, no request object, debug and test only.
- Typed query discriminator filter (1.2).
- `Table<T>.DescribeAsync()` beside `WaitUntilActiveAsync()`, so a `Table<T>` user never needs `DynaSharp.Requests` for the two admin calls everyone makes.
- Per-call attempt timeout. Not asked for; `MaxAttempts` per call is enough for now. Later.
- A statement on `ConsumedCapacity` across `ItemsAsync()`: it is lost. Offer it on `PagesAsync()` only and say so.

### Compared with the others, what they get right

- **EfficientDynamoDb.** Right: one entry object (`context`) whose members are the operations, so IntelliSense teaches the API; `ToListAsync`/`ToPageAsync`/`ToAsyncEnumerable` as explicit terminals; nested per-action builders in transactions (`Transact.PutItem(x).WithCondition(...)`), which avoid "applies to the last action". Wrong, and measured: expression-tree conditions at 7.99 KB per update (FACT [proposal, M]), reflection mapping, `client.LowLevel` as a property. DynaSharp's step types give the nested-builder safety without lambdas.
- **AWS DynamoDBContext.** Right: `CreateBatchGet`/`CreateBatchWrite` objects you fill and `Execute`, and `CreateMultiTableBatchWrite` to combine them. Wrong: `Save`/`Load` vocabulary that hides Put/Get, version handling you cannot see, no key templates, reflection. DynaSharp should not borrow anything but the batch-combination idea, and only if multi-table batches are asked for (today the request API covers them).
- **ElectroDB.** Right: composite key templates with validation (DynaSharp's key grammar is the stricter cousin), `params()` to see the request, automatic entity filter on queries, `where((attr, op) => ...)` typed conditions with an operator object, string cursors for paging, `collections` for mixed-entity queries over a shared index. Wrong, and recorded in the requirements: default lowercasing of key values. DynaSharp should take `params()`, the entity filter and the cursor string; it already has better keys.
- **DynamoDB Toolbox v1.** Right: `send()` as the universal terminal, `.params()`, an update DSL (`$add`, `$remove`) typed so that `$add` exists only on numbers and sets, `Parser`/`Formatter` as separate reusable pieces, options as one object. DynaSharp's `Add<TNumber>` constraint is the same idea done with generics; `SendAsync` is its `send()`.

Nobody surveyed has: zero-allocation expression building, a compile-time key grammar with order-preserving encodings, OpenTelemetry out of the box, or a wire-level test seam. Those are the selling points; the API above should not hide them.

---

## 8. Prioritized changes

### Must land before anything is built on it (they change public shapes)

1. **Namespaces.** `DynaSharp` for the table API and shared types, `DynaSharp.Requests` for the mirror as extension methods, `DynaSharp.Expressions` for the condition structs, `DynaSharp.Model` dissolved, `AttributeValue` in the root. (Section 2)
2. **Return values.** `ReturnOld()`, `ReturnNew()`, `Return(attr)` ending the chain, `ReturnOldOnFailure()`; delete `SendAndReadAsync`; a missing attribute throws. (1.2)
3. **Transactions.** `TransactWrite()`/`TransactGet()`, step types, `TransactMarker`/`TransactSlot<T>`, `ReasonFor`/`ItemFor`, `IfNotExists()` without a table argument, a real result type. (1.2)
4. **Compile-time guards.** `Index<T>`; `Put()`/`With` on `Table<Item>` only; `Of<T>()` for entities; `Parallel()` without `PageAsync`. (1.2, 5)
5. **Composition interfaces** on the commands with `allows ref struct` (`IConditional<TSelf>`, `IUpdatable<TSelf>`, `IRetryable<TSelf>`), so helpers exist from day 1 and the ref struct decision is defensible. (3.3)
6. **Typed Query/Scan add the discriminator filter.** Decide now; changing it later changes results silently. (1.2)
7. **`ContinuationToken.Parse`/`TryParse`, `PagesAsync`, `CountAsync`.** (1.3)
8. **`Item`**: `ToString()` as DynamoDB JSON, `DebuggerTypeProxy`, `To<T>()`, `char` blocked, `GetNumber` error naming the attribute. (4)
9. **`TimeProvider`** in batch retry and `WaitUntilActiveAsync`. (3.5)
10. **Remove from the public surface**: views, streaming callback, deferred commands, read-mode knobs. (7)
11. **Shape-churn benchmark** before `AttributeNameTable` is deleted. (7)
12. **`BatchIncompleteException`** carries parsed `WriteRequest`s, not bytes. (7)

### Should land with M2a (same milestone, do not block the start)

13. `ValidationException.Expressions`/`ExpressionNames` for commands. (5)
14. `ToRequestJson()` on every command. (7)
15. `StubResponse` typed helpers and `RecordedRequest.ReadItem<T>()`. (6)
16. `[DebuggerDisplay]` on commands and state; `[EditorBrowsable(Never)]` on `GetAwaiter`. (3.4)
17. Client-side empty-key check on untyped keys. (5)
18. `Table<T>.DescribeAsync()`, cached `Of<T>()`. (7)
19. The sample rewritten on `Table<T>`, dropping to `Requests` once, visibly. (2.5)

### Can come later

20. The analyzer: `DYNA201` next to ref struct compiler errors, `DYNA210` to `DYNA214` for expression strings. After M2a, because it needs the final method names. (3.4, 5)
21. `orders.Index("GSI1")` without key names, `Project(Order.Attrs...)` with generated projections including `_type` and keys: M2b.
22. Fan-out query with composite token, `[Version]` enforcement, mixed-type query: M3 as planned.
23. Multi-table `BatchGet`/`BatchWrite` on the client, only if asked; the request API covers it.
24. Per-call attempt timeout; `ConsumedCapacity` aggregation over `ItemsAsync()`.
25. The in-memory fake: M4 as planned.

### What I would not do, agreeing with the proposal

Remove the request API, stack layers, build the deferred builder as the main API, ship views by default, write an HTTP client, use interceptors, add `ToRequest()`, overload `==` on attributes. One addition: do not add an interface on `Table<T>` however often it is asked for; the stub with typed helpers answers the need and tests more.

## 9. M3 contracts (lead, 2026-11-01)

Decided before implementation; changes need the lead.

**`[Version]`.** The property holds the version the caller last read; 0 means "new". Every write path enforces it and every enforced write is visible in `ToRequestJson()`:
- `Put(entity)`: version 0 adds `attribute_not_exists(<version>)`, any other adds `<version> = :old`, and the item is written with `old + 1`. `WriteAttributes` itself does not bump, so `Item.From` stays a plain copy; the command does. The entity is not mutated: `WriteResult<T>.Version` returns the stored version.
- `Update(key)` and `Delete(key)`: the key does not carry the version. `.IfVersion(n)` adds the condition (and on Update `SET <version> = :new`, with `:new` being `n + 1`). Without it, `SendAsync` throws `InvalidOperationException` naming the entity, unless `.IgnoreVersion()` was called. `Update(entity)`-style overloads are not added.
- Transaction steps: the same rules, with the same verbs. `BatchWrite` cannot carry a condition: a `Put` or `Delete` of a versioned entity throws `InvalidOperationException` unless `.IgnoreVersion()` was called on the batch.
- A failed condition is the ordinary `ConditionalCheckFailedException`. No retry and no read happens implicitly. The runtime check runs in the terminal, and it is 0 B for an entity without a version.

**Template inputs on updates.** A typed `Set`/`Remove` by handle of a property that feeds a key template must also leave that key consistent. The command checks at the terminal: if some but not all inputs of a template are set, it throws `InvalidOperationException` naming the template and the missing inputs; if all are set, it writes the derived attribute in the same `SET`. A `Remove` of an input of a sparse index removes the derived attributes. Inputs of a primary key are rejected: DynamoDB cannot update a key. Raw string paths are not checked; that is the documented escape hatch. Metadata is generated per entity, so the check costs nothing for an entity with no template index.

**TTL.** `EpochSecondsConverter` by attribute, already planned. `[TimeToLive]` is not added.

**`.Unique` on typed keys.** `Unique(table, key, out marker)` for `IKey<T>`, writing a marker item of the key only, with `attribute_not_exists` on the partition key.

**Mixed-type reads.** `shop.Query(pk).Of<Customer, Order>()`, and on an `Index<Item>` and `Scan`, arities 2 to 4, returns a `MixedQueryCommand<T1, T2>` with the same read clauses and terminals `ItemsAsync`, `PagesAsync`, `PageAsync`. Items are `Mixed<T1, T2>`, a readonly struct with `Is(out T1)`, `Is(out T2)` and `Match(...)`. The filter gains `#_type IN (:a, :b)`, so other types never reach the client and `ScannedCount` exceeds `Count`. A type without a discriminator does not compile as an argument. Dispatch copies the reader, finds `_type`, rewinds, and reads through the generated `ReadAttributes`, with no `Item` in between. A read of `Table<T>` for an entity stays as it is.

**Fan-out query.** `orders.Query(IEnumerable<AttributeValue> partitionKeys, SortKey sortKey = default)`, on `Table<T>` and `Index<T>`, returns a `FanOutQueryCommand<T>` with `Filter`, `Project`, `Limit` (items per partition page, as DynamoDB means it), `Descending`, `Consistent`, `MaxAttempts`, `ReturnCapacity`, `MaxConcurrency(n)` (default 8), and terminals `ItemsAsync`, `PageAsync(pageSize, after, ct)`, `CountAsync`, `ToRequestJson`. No `PagesAsync`, and no `Parallel`. Items merge by sort key in DynamoDB's order: bytes for S and B, numeric for N, ties by the position of the partition key in the input. `PageAsync` takes `pageSize` and returns a `Page<T>` whose `Next` is a composite token that holds one entry per partition: its start key and the key of its next item as a hint, or "done". The hint spares a read of a partition the merge does not need yet, and never reaches a request. Tokens are validated like plain ones and rejected against a different partition list by a count check; nothing is signed. If the table changes between pages, an item written before a hinted key can come later than its place. A partition is read only when the merge needs it, so an early `break` does not read the rest. Bounded memory: at most one page per partition is buffered.

**Polish after the sample (package A5).**
- `GetCommand<T>.ItemAsync(ct)` returns `T?`, so the common read has neither parentheses nor `!`. `SendAsync` stays for `Found` and capacity.
- Each transaction step converts implicitly to `TransactWriteCommand`, so `TransactWriteCommand tx = client.TransactWrite(); foreach (...) tx = tx.Put(...).IfNotExists();` compiles. The step types still decide what a chain may say next.
- The generator emits `Order.Indexes.ByStatus`, a `const string` per index, on every entity with an index. `Indexes` is reserved as a member (DYNA014), and as the name of an entity with an index (DYNA025).
- `BatchWriteCommand<T>.PutAll(IEnumerable<T>)` and `DeleteAll(IEnumerable<IKey<T>>)`.
- `Delete(entity)` on a `[Version]` entity takes the version from the entity: the condition is `IfVersion(entity.Version)`. `IgnoreVersion()` still opts out. `Delete(key)` keeps its rule.
- Sparse siblings: setting a template input of a sparse index when the update does not also produce the other key of that index throws at the terminal, naming the other key, because the item would silently stay out of the index. An input of the table key that the update cannot write counts as not produced; such an item enters the index by `Put`.

**After the independent review (package R1).**
- `Set(handle, null)` is a `Remove`, for every attribute, as `IUpdatable` says. A handle of a non-nullable type still warns at the call through nullable analysis.
- `MaxAttempts` on a batch means what it means everywhere, HTTP attempts per request. `MaxStalledRounds(n)` on `BatchGet` and `BatchWrite` bounds requests in a row without progress, default 8.
- `PutCommand<T>.IgnoreVersion()` and the same on `PutStep` write the entity as it is, with no condition and no bump, for imports and migrations, the same as a batch that ignores versions.
- A mixed read on an index keyed by the discriminator skips its `IN` filter, as a typed read does.
- `TransactGetCommand.Get<T>(table, partitionKey, out slot)` for a table without a sort key.
- The sample uses `Of<Customer, Order>()` and shows a fan-out query, `IfVersion`, `Delete` and `TransactGet`.

Kept as they are: the discriminator value in `DynamoDbMappingException`, because requirements asks for the type found, and it is a type tag, not item data. DYNA212 and DYNA214 were kept as errors at first, then lowered to warnings for 0.x after the maintainability review: the grammar is a copy of the service's from 2026, and a false error in a consumer's build can only be fixed by a new package. They can go back to errors once the corpus has been run against the service again.

**After the recipes (package R2).**
- `Update` of an entity can create the item, because `UpdateItem` is an upsert. Such an item must not be invisible to the typed API:
  - An `Update` on an entity with a discriminator always ends its `SET` with `#_type = if_not_exists(#_type, :t)`, visible in `ToRequestJson()`, also in a transaction. A new item gets the type, and an existing item keeps the one it has, so an update at a key held by another type cannot rewrite it.
  - A generated read that finds a key input missing restores it from the key attribute it is derived from, when that format is lossless, the same way `[KeyOnly]` reads. This costs nothing when the attribute is there. A lossy format stays a `DynamoDbMappingException` naming the attribute.
- A key of a sparse index that holds only literals counts as produced: an update that sets the inputs of the other key writes the literal key in the same `SET`, and a `Remove` that takes the item out of the index removes it too. `PartitionKey = "FEATURED"` can be entered by `Update`.
- `Query(IEnumerable<string>, SortKey)` on `Table<T>` and `Index<T>`, because a computed list of shards is the normal case. It must not make a call that compiles today ambiguous.
- DYNA115 becomes `Info`. The ordering caveat is real but rare, and a warning fails a warnings-as-errors build on the flagship hierarchical key.

Left to the owner: a mixed read on an index repeats key names, because `Of<...>()` hangs off `Table<Item>`; conflict retries are hand-written, about 12 lines each, by contract; changing one input of a composite index key resends the others.
