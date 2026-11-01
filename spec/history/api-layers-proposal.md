> Historical record, frozen: this file is not updated, and it is the proposal with its measurements, not the API that shipped.
> Replaced by [api.md](../api.md), which states the public API as shipped.

# DynaSharp - API layers proposal

Spike on `spike/api-layers`, for the owner's decision. Throwaway branch, nothing here is merged. Claims are labelled **[M]** measured, **[R]** reasoned, **[G]** guessed.

How it was measured: BenchmarkDotNet `--job short`, every run started with the working directory in this worktree, `executed benchmarks: N` confirmed for each suite (41 layer, 17 template, 13 composite, 11 floor, 39 item, 9 real call). Allocations are the reliable column. Times are from a busy Windows machine and are rough; compare them only inside one table. `AttributeNameTable` from `m1b/p8-name-cache` is in the branch, so the baselines reproduce: PutItem 2.49 KB, GetItem 4.98 KB, Query of 100 items 142.73 KB **[M]**.

## 1. Recommendation

Make the table API the primary API and keep the request API public beside it. Do not put one on top of the other.

```
                     DynamoDbPipeline  (signing, retries, telemetry, error mapping)
                    /                |                         \
        Request API            Command API                 SendRawAsync
        records, dictionaries  Table<T>, pooled wire       JSON in, JSON out
        1:1 with the docs      state, no objects           anything unmodeled
```

- **`Table<T>` is one engine with two entry points.** `client.Table("Shop", "pk", "sk")` is `Table<Item>`, and `shop.Of<Order>()` is `Table<Order>`. `Item` is one more implementation of `IDynamoDbDocument<T>`, so T decides only how an item is read and written.
- **Commands are lean.** A `ref struct` handle over a pooled `CommandState` writes UTF-8 straight into pooled segments. There is no request object and no dictionary on the path.
- **`Item` replaces `Dictionary` for reads,** behind the existing `IReadOnlyDictionary` signatures, with names shared across items through a shape.
- **The cheap HTTP changes go in,** for both APIs.
- **Add `SendRawAsync`.** It is the only real escape hatch; see section 5.
- **Compile-time templates: not now.** See section 8.

Answers to the three questions:

1. **Are dictionaries the way to go?** No for reads, and not at all on the write path. Yes as an accepted input on the request API, and for items you build by hand beyond 32 attributes.
2. **Do we need request objects?** Not on any hot path, including batch and transactions. Keep them public anyway: they cost little once written, and admin and Streams need them regardless.
3. **Builders: mid level, or replace it all?** Neither. A sibling that becomes the default, with the request API staying as the complete, documented layer.

## 2. The numbers

Allocation per call, canned HTTP. "Prebuilt" reuses one request object, which is how the existing benchmark measures and flatters the low level: a real caller builds per call. "Lean" is the command API. "+HTTP" adds the changes in section 6.

| Call | Low, prebuilt | Low, real caller | Deferred struct | Lean | Lean +HTTP | Typed +HTTP | EfficientDynamoDb |
|---|---|---|---|---|---|---|---|
| PutItem, 10 attributes | 2.49 KB | 4.43 KB | 3.34 KB | 2.21 KB | **1.41 KB** | **1.41 KB** | 4.99 KB entity |
| UpdateItem, ADD + SET + condition | 2.55 KB | 3.30 KB | 2.61 KB | 2.21 KB | **1.41 KB** | **1.41 KB** | 7.99 KB entity |
| GetItem, 10 attributes | 4.98 KB | 5.27 KB | | 3.23 KB | **2.44 KB** | **2.16 KB** | 5.73 KB entity |
| Transaction, 2 puts with conditions | | 3.57 KB | | 2.84 KB | 2.04 KB | **1.41 KB** | |
| Batch of 25 | | 10.29 KB | | 7.09 KB | 6.30 KB | **1.41 KB** | |
| Batch of 60 (3 requests) | | 26.53 KB | | 18.35 KB | 15.96 KB | **4.24 KB** | |

All **[M]**. "Typed" in the transaction and batch rows means entities the caller already holds. The untyped lean rows pay for a `new Item` per write, which is the caller's object, not the path's; a marker transaction without items is 1.41 KB. Lean PutItem uses `.With(...)`, so no `Item` exists; with a fresh `Item` it is 3.07 KB.

Query of 100 items, each variant reading the same 3 of 10 attributes, because a lazy view that is never read would flatter itself:

| Read | Allocated | Time | Lifetime risk |
|---|---|---|---|
| Low level, Dictionary, name table (baseline) | 142.84 KB | 282 us | none |
| EfficientDynamoDb low level | 202.32 KB | 310 us | none |
| Flat `Item`, shared shape | **98.56 KB** | 298 us | none |
| Same, attributes shuffled per item | 98.56 KB | 356 us | none |
| EfficientDynamoDb entity | 88.97 KB | 211 us | none |
| Hand-written typed, generator shape | **70.39 KB** | 204 us | none |
| View over the response, pooled | **13.08 KB** | 313 us | use after dispose, see below |
| Streaming callback | **2.44 KB** | 147 us | span must not escape |
| View, every item copied out | 98.23 KB | 416 us | as view |

All **[M]**. The typed figure replaces the 60-90 KB estimate in generator design section 6. The perf package's idea of shared keys with a values array is what `ItemShape` is, and the measured cut against 142.84 KB is 31 %.

**Real calls.** A canned handler completes synchronously, which hides two costs: async state machines, and what the HTTP stack allocates.

| | Low, prebuilt | Lean +HTTP |
|---|---|---|
| PutItem, suspending handler | 3.13 KB | **1.55 KB** |
| GetItem, suspending handler | 3.53 KB | **1.67 KB** |
| PutItem, real sockets to Dynoxide | 4.49 KB | **2.71 KB** |
| GetItem, real sockets to Dynoxide | 6.29 KB | **3.23 KB** |

All **[M]**. PutItem and the socket GetItem use an 8-attribute item, the suspending GetItem a 3-attribute one, so compare along a row only. The state machines are why the pooled builder matters: suspension adds 0.64 KB to the low level and 0.14 KB to lean.

**Code.** Scenario bodies from `samples-compare/Scenarios.cs`, counted by `dotnet run -- count` **[M]**. The low column is the real code of `samples/DynaSharp.Sample` on `m1b/p10-sample`.

| Scenario (lines / characters) | Low | Mid | Typed |
|---|---|---|---|
| Conditional put | 15 / 442 | 9 / 245 | 9 / 203 |
| Counter update | 9 / 440 | 1 / 134 | 1 / 153 |
| Paged query with a filter | 19 / 710 | 6 / 240 | 6 / 265 |
| Unique email transaction | 39 / 805 | 12 / 457 | 12 / 345 |
| Total | 82 / 2397 | 28 / 1076 | 28 / 966 |

The typed column also needs one declaration per entity, written once.

## 3. Dictionaries: the item

Prototype: `src/DynaSharp/Spike/Item.cs`. Two parallel arrays, the names being a shared `ItemShape`.

- **Slot-in is real.** With `Item` behind every low-level read path: 280 of 280 integration tests on both emulators, and 4033 of 4043 unit tests **[M]**. The 10 failures are tests that inject a counting pool, which the swap ignored, so they say nothing about behaviour **[M]**. Two internal test helpers named `Dictionary` and had to change **[M]**.
- **Reads gain at every size.** One item at 10 / 50 / 400 attributes: 544 / 2464 / 31024 B flat against 1632 / 6560 / 47952 B with the name table **[M]**.
- **Hand-built items are a mixed result.** At 10 attributes, 328 B against 528 B (sized). At 50, 5000 B against 2256 B; at 400, 31088 B against 15648 B **[M]**. Above 32 attributes a hand-built item also carries an index of its own, so a sized `Dictionary` is cheaper from there on **[R]**; the exact crossover was not measured. `.With(...)` needs neither.
- **Lookup needs an index earlier than expected.** Reading every attribute by same-prefix names takes 207 ns linear and 87 ns Dictionary at 8 attributes, and 348 us against 6.5 us at 400 **[M]**. The index is 12-25 % behind `Dictionary`, and 51 % at 8 attributes **[M]**. Because it belongs to the shape, one is built per shape, not per item, so the limit can be low: 8 for shared shapes, 32 for hand-built items, whose index is their own **[M, from two runs]**.

What breaks or changes:

| Topic | Result |
|---|---|
| `IReadOnlyDictionary` semantics | Hold. Only a cast to `Dictionary` breaks; the signatures never promised one **[R]** |
| Attribute order | **Must not be relied on.** Both emulators returned one attribute set in different orders **[M]**. An order-keyed shape would miss constantly, so the prototype's shape is order-insensitive and defines the value order. "Ordering preserved" holds only for hand-built items. A shuffled page costs the same memory and about 20 % more time **[M]**. Real AWS is unknown **[G]** |
| Duplicate names | On read, checked once per shape, not per item. On write, `Add` throws. Both emulators accept a duplicate in a request and keep the last **[M]**; real AWS is unknown **[G]** |
| Nested maps | `AttributeValue.Map` holds an `Item`; `AsMap()` stays the interface **[M]** |
| Equality | `Item` compares by content, ignoring order. `GetItemRequest` with `Item` keys now compares equal; with dictionaries it does not **[M]** |
| Shared names and mutation | Writing to a read item copies first, so items of a page stay independent **[R]**, not separately tested |
| Process-wide state | Name and shape tables are static and direct-mapped: bounded, lock-free, and a collision only loses a cache entry **[R]**. `AttributeNameTable` is per response. The global table is needed for shape sharing through `ReadAttributes`, which has no page context. Attribute names stay in memory; values do not |

**Does a flat item make writing nicer?** Barely. The improvement comes from implicit conversions on `AttributeValue`, which work with `Dictionary` too. `Item` saves the type name **[R]**. A `char` converts to a number through `int`, which needs an `[Obsolete(error)]` overload **[R]**.

## 4. The lean command: settled questions

**(1) One call feeds three JSON members.** The body, expression text, names and values are separate pooled segments, joined at send **[M: prototype]**. Update clauses get a segment each, so `.Add().Set().Add()` works. Names dedup by comparing UTF-8 slices. Values are written to scratch through the converter and compared as bytes, so dedup covers every type without a comparer, hash or box. Linear, and 0 B **[M]**.

**(2) struct, ref struct or pooled class: all three, in layers.** The state is a pooled class, because something has to cross the await. The handle in front is a `ref struct` holding the state and a version number. `await table.Update(...).Add(...).If(...)` and a terminal `SendAsync(ct)` both work **[M]**.

| Misuse | Result |
|---|---|
| Store in a field | Compile error **[R]** |
| Capture in a lambda | Compile error CS8175 **[M]** |
| Use across an await, including `await c; await c;` | Compile error CS4007 **[M]** |
| Two `SendAsync` with no await between | `InvalidOperationException` **[M]** |
| Extend a stale copy | `InvalidOperationException`, because every fluent call consumes its handle **[M]** |
| Stale handle reaching a reused state through an interpolation handler | `InvalidOperationException`; the handler checks first **[M]** |
| Abandon a command, or throw while building | Collected. `ArrayPool` does not track rentals, so one pool entry is lost and nothing leaks. 1000 abandoned, next command clean **[M]** |
| Retry | The body is returned in `finally` after the last attempt. 500 commands, each retried, 16 concurrent: 500 distinct bodies, none mixed up **[M]** |
| Timeout on a reused `CancellationTokenSource` | Fires, is dropped, later calls unaffected **[M]** |

What this costs in usability, plainly:

- A command cannot be stored, reused as a template, or passed to an async helper. It cannot be extended after an `await`, so compute values first **[M]**.
- Conditional building in a sync section works: `if (x) cmd = cmd.Set(...)` **[R]**.
- Conditions can be `static readonly`, and `Condition.Of` composes at run time on the heap **[M]**.
- `await cmd` has no cancellation token; `SendAsync(ct)` does **[R]**.
- The one thing the rejected deferred struct does better is reuse, at 0.27-0.4 KB per call more **[M]**. If stored commands turn out to matter, it can be added later on the same segments.

**(3) Names and values.** Every value goes through `IDynamoDbConverter<T>` and `DynamoDbWriter`, and the untyped path is the same generic code over `AttributeValue`. `u8` literals save no allocation, since string literals are not allocated per call; they save a transcode, so they belong in generated code and not in the hand-written API **[R]**. Interpolated expressions pay: `$"{Name("status")} = {"open"}"` reads as DynamoDB's own grammar, costs 0 B, and allocates the same as struct conditions **[M]**. A plain string hole is a value, so a forgotten `Name(...)` fails with a DynamoDB validation error, not silently **[R]**. Every name is a placeholder, so reserved words stop being a problem: `status`, `size` and `order` in expressions run against both emulators **[M]**.

**(5) Batch and transactions.** Request objects are unnecessary there too **[M]**. Actions are written to the wire as they are added, and a batch's action list is only offsets into one buffer. `UnprocessedItems` has the form of `RequestItems`, so it goes back out as raw bytes, unparsed **[M]**. `LastEvaluatedKey` does the same for paging. Usability survives, with one wart: in the flat transaction form `If` applies to the action added last, and adding `.Add` to a put fails only at DynamoDB **[R]**.

## 5. Request objects and the public low level

**Cost.** The object is the small part. PutItem goes from 2.49 KB prebuilt to 4.43 KB fresh, nearly all dictionaries and `AttributeValue`s **[M]**. The request records are about 100 B **[R]**.

**What they really give:**

| Claimed benefit | Verdict |
|---|---|
| Completeness, mirrors the docs | Real |
| `with` copies | Real, used by the sample for paging |
| Testability by equality | **False.** Two equal requests with dictionaries compare unequal **[M]**. The test seam is the wire, and `StubHandler` serves both APIs |
| Escape hatch for new DynamoDB features | **False.** A sealed record cannot carry a parameter DynaSharp has not modeled |

`SendRawAsync` fills that gap: any operation, or any parameter, with the same signing, retries and errors. `ListTables`, which has no model, ran against both emulators **[M]**. Telemetry names such a call `Raw`; production should tag the real operation name.

**Can the low level go?** No **[R]**.

- Of 94 files in `Model`, 31 are data-plane request, response and action types **[M]**. The other 63 stay public anyway: `CreateTable`, Streams (low level only by spec), `ConsumedCapacity`, enums, `AttributeValue`.
- Going internal saves 31 of 132 public types and no code, because the wire models stay **[R]**.
- The API mirrors a frozen service version, so the surface is large but stable. New parameters are additions **[R]**.
- It is the migration path from the AWS SDK and EfficientDynamoDb, and it is written, tested and documented.

## 6. The floor

What 1.41 KB consists of **[M, from separate measurements]**:

| Part | Bytes | Ours to remove? |
|---|---|---|
| `HttpRequestMessage`, headers, `ByteArrayContent` | 432 | No: the `HttpMessageInvoker` contract |
| `Authorization` string, header entries | 472 | No: headers are strings |
| Canned response objects | 424 | No: harness; on sockets this is `SocketsHttpHandler` |
| Rest (harness task and stream) | about 120 | No |
| **Our layer** | **0** | Done |

So the true minimum is **1.41 KB for PutItem and UpdateItem**, and **2.16 KB for GetItem** of this item, the difference being the entity itself **[M]**. On real sockets, **2.71 KB and 3.23 KB** **[M]**. Going lower means replacing `SocketsHttpHandler` with our own HTTP client, which I would not do **[R]**.

The cheap changes took PutItem from 2.21 KB to 1.41 KB **[M]**:

| Change | Effect |
|---|---|
| Raw `Content-Type` instead of `MediaTypeHeaderValue`, no request validation | Request 712 to 432 B **[M]** |
| Reused `CancellationTokenSource` with `TryReset` | 144 to 0 B **[M]** |
| Body buffer and `Utf8JsonWriter` from the pooled state | 176 to 0 B **[M]** |
| `PoolingAsyncValueTaskMethodBuilder` | Only visible when suspending: 3.13 to 1.55 KB with the other changes **[M]** |
| Date string per second, cached authority, response buffer kept with the command, request id only on demand | Part of the above **[R]** |
| `HttpMessageInvoker` instead of `HttpClient` | 0 B, 16 % faster in one run **[M]**. Keep it simple: optional |
| Custom `HttpContent` | Not tried. `ByteArrayContent` already wraps without a copy, so nothing to gain **[R]** |

These are independent of the builders. The request API gets most of them as well **[R]**.

## 7. Where a dictionary remains, and why

On the command path:

- **`ItemShape` index:** a `Dictionary<string, int>`, one per shape over 8 attributes, not per item.
- **`ConsumedCapacity` per index and `ItemCollectionMetrics`:** only when requested.
- **Exceptions:** `ConditionalCheckFailedException.Item` and `CancellationReason.Item`. Error path, and easy to switch to `Item`.

In the request API, by design: the public `IReadOnlyDictionary` signatures, which accept `Item` too. `AttributeValue.FromMap` still builds a `Dictionary` and should build an `Item`.

Not prototyped, so unproven **[R]**: Scan, BatchGet, TransactGet, parallel scan. They have the same shape as Query and transactions. Multi-table batches and the 16 MB limit are also missing.

## 8. Compile-time JSON

Hand-written exactly as a generator would emit it: `benchmarks/.../ApiLayers/TemplateBenchmarks.cs`. Constant `u8` segments with value holes, for UpdateItem with ADD and a condition, and Query with a key condition.

| | Low, prebuilt | Runtime builder | Template, Utf8JsonWriter | Template, own escaper |
|---|---|---|---|---|
| Update body | 580 ns, 176 B | 945 ns, 0 B | 194 ns, 0 B | **101 ns, 0 B** |
| Query body | 568 ns, 176 B | 814 ns, 0 B | 215 ns, 0 B | **118 ns, 0 B** |
| Whole Update call, canned | 6.7 us, 2552 B | 6.9 us, 1448 B | | 4.0 us, 1448 B |
| Whole Query call, canned | 6.6 us, 2864 B | 5.5 us, 1448 B | | 4.4 us, 1448 B |

All **[M]**, times rough. Your expectation holds: the allocation gain is zero, because the runtime builder is already at 0 B. The gain is 0.7-0.85 us of CPU per call in the body, which is about 0.08 % of a 1 ms round trip, or under 1 % of a core at 10,000 calls per second **[R]**. The whole-call rows show a larger gap than the bodies can explain, and that excess is noise. The runtime builder's body is slower than the low level's, which is worth fixing on its own: a fast path that skips the scratch writer for strings and numbers should reach 300-400 ns **[G]**.

**Delivery options:**

| | Partial method, `[DynamoDbUpdate<Customer>("ADD orders :n", Condition = "attribute_exists(pk)")]` | Interceptors on fluent calls |
|---|---|---|
| Status on this SDK | Stable pattern, as `[LoggerMessage]` and `[GeneratedRegex]` | Needs `<InterceptorsNamespaces>` per project, else CS9137 **[M]**. The file-path attribute form is already rejected, CS9270 **[M]**. SDK targets still say `InterceptorsPreviewNamespaces` **[M]** |
| Tooling | F12 goes to the declaration | The call site looks like the runtime path; a debugger jumps elsewhere **[R]** |
| Generator on netstandard2.0 | Fine | Fine, needs `InterceptableLocation` from the Roslyn API **[R]** |
| Non-constant arguments | Compile error | Silently stays on the runtime path, so performance depends on how a line is written **[R]** |
| A fluent chain | Not applicable | Each call in a chain is a separate site, so a whole chain needs all of them rewritten together **[R]** |
| Validation | Full: expression, attributes, parameter types, at compile time | Only for sites it can read |

**As a mode:** the partial method only, never interceptors. It would cover constant-shape calls with scalar parameters: counters, status changes, key-condition queries. Everything composed at run time stays on the builder, as do items, batches and transactions. It belongs **after M3**, built only when someone shows a CPU-bound hot path. The cost is an expression parser in the generator **[R]**.

Most of the validation benefit is available more cheaply. Generated handles already give attribute and value-type checks, and `Name(...)` removes reserved words as a problem. An analyzer on constant interpolated expressions could cover the rest without emitting code **[G]**.

## 9. Changes to the plan

| Now | Proposed |
|---|---|
| M2 section 6: `GetItemRequest<T>`, `PutItemRequest<T>`, `QueryAsync<T>`, `ScanAsync<T>` | **Drop.** `Table<T>` does this, with a smaller public surface |
| `EntityKey<T>` with two `AttributeValue`s | **Replace** with a generated key struct implementing `IKey`. T is fixed by the table, so the inference problem of section 6 disappears, and a key costs no string **[M]** |
| `AttributeValue.From<T, TConverter>` for condition values | **Drop.** Values go straight through the converter |
| M3 conditions, updates, paging, batch, transactions | **Move before the generator.** They need no generator and can be tested on emulators with `Item` |
| `AttributeNameTable` | Superseded by the shape tables, if `Item` is accepted |

Proposed order:

1. **M1c, core:** `Item`, the HTTP changes, `SendRawAsync`, the implicit conversions.
2. **M2a, commands:** `Table<Item>`, expressions, paging, batch, transactions, plus Scan, BatchGet, TransactGet. Usable product without the generator.
3. **M2b, generator:** entities, keys, handles, as designed, minus section 6.
4. **M3:** what remains needs entities: `[Version]` enforcement, key-template update dependency checks, mixed-type queries, fan-out, `.Unique` on typed keys.

**Migration cost of merged code.** 12 existing files changed, about 500 lines, mostly new pipeline code **[M]**. No public type was removed, and no test had to change outside the experiment **[M]**. The prototype is 3700 lines without docs or tests **[M]**. Productizing to the 100 % coverage and full-docs gates is roughly two to three times that **[G]**. The pipeline's two retry loops must become one before merge, as the prototype duplicates it **[R]**.

Native AOT: publish passes with no warnings, and the smoke test passes through ref struct commands and generic operations on both entry points. The binary goes from 6.87 to 7.33 MB **[M]**.

## 10. Risks

- **Pooled state is a new class of bug.** A missed release only costs an allocation, but a double release would hand one state to two commands **[R]**. The version check does not cover a release bug inside the library, so this needs its own stress test in CI.
- **Response and request buffers rely on the existing assumption** that a handler no longer reads the content after the response. A handler that keeps it would see reused bytes sooner than today **[R]**.
- **AOT code size grows per entity and operation,** because generic operations over structs are not shared. Measured only as +460 KB for two entry points **[M]**; the growth per entity is unknown **[G]**.
- **The static name and shape tables** are process-wide. Benign races are by reasoning, not tested under contention **[R]**.
- **Real DynamoDB was not used.** Attribute order, duplicate handling and placeholder forms are emulator results **[M on emulators, G on AWS]**.
- **`ReadAsStream` is synchronous** on the reduced path. Fine for `SocketsHttpHandler` content, but an unusual handler could block **[R]**.
- **Ref struct commands are unusual.** Users will meet CS4007 and need to read why **[G]**. The message does not explain it well.

## 11. Where the current design is already right

- `DynamoDbPipeline` being generic over `IOperation`. Sibling operations needed three small hooks and no redesign **[M]**.
- The HTTP handler as the test seam, and no interface on the client.
- One serialization path through `DynamoDbReader`, `DynamoDbWriter` and converters. The command API added none **[M]**.
- `IReadOnlyDictionary` in public signatures, which is what made the slot-in possible.
- Generated handles instead of expression trees. EfficientDynamoDb's lambdas cost it 7.99 KB on Update **[M]**.
- Numbers as text, the `AttributeValue` struct, and the public request API as a complete mirror.

## 12. What I would not do

- Remove the low level, or stack the mid level on it.
- Build a deferred builder as the main API. It is safe and reusable, but heavier on every call, and `Condition` on the heap is what the owner wants gone.
- Ship views as the default read. A view saves a lot only if you read little (13 KB), and costs the same as `Item` if you read everything (98 KB, slower). A kept view pins its page, and a 1 MB page is a large object.
- Write our own HTTP client for the last kilobyte.
- Use interceptors.
- Add `ToRequest()` on commands. It would bring back the dictionaries, and `SendRawAsync` covers the need.
- Overload `==` on attributes.

## 13. Decisions for you (taste)

| Question | Options | My lean |
|---|---|---|
| Name of the flat item | `Item`, `Document`, `AttributeMap` | `Item` |
| Condition style | Interpolated DynamoDB syntax, struct `Eq().And()`, or both | Both: interpolated untyped, struct typed |
| Name hole | `{Name("status")}` or `{"status":n}` | `Name`, more explicit |
| Terminal | `await cmd` and `SendAsync(ct)`, or `SendAsync` only | Both |
| Streaming and views | Streaming as an advanced API, views later or never | Ship `ForEachAsync` only if asked for |
| Request API location | On the client, or `client.LowLevel` as in EfficientDynamoDb | On the client |
| Conditions verb | `If`, `When` | `If` |
| Namespace | `DynaSharp.Fluent`. `DynaSharp.Tables` shadowed the test helper `Tables` **[M]** | Anything but `Tables` |

## 14. Sample friction, answered

| Friction from `samples/DynaSharp.Sample` | Answer |
|---|---|
| `FromNumber` everywhere | Implicit conversions, or `.With("n", 1.5m)` with no `AttributeValue` **[M]** |
| Dictionary boilerplate, own `Key` helper | Keys are parameters: `Update(pk, sk)` **[M]** |
| Manual do/while paging | `await foreach`, filtered empty pages followed **[M]** |
| Printing and `[]` throwing | `GetString`, `GetNumber<T>`, `GetBool`, `GetMap` return null when missing **[M]** |
| Reserved words fail at run time | All names are placeholders **[M]** |
| `CancellationReason.Code` by parallel index | `.Put(..., out var marker)`, then `exception.Of(marker)` returns an enum **[M]** |
| `MaxAttempts` is client-wide | `.MaxAttempts(1)` per call **[M]** |
| Waiting for ACTIVE | `table.WaitUntilActiveAsync()` **[M]** |
| Unprocessed batch loops | Chunked and retried, bounded, then an exception holding the rest **[M]** |
| A stream read takes two calls | Not addressed. Streams stay low level by spec |

Prototype code: `src/DynaSharp/Spike/`, `samples-compare/`, `benchmarks/DynaSharp.Benchmarks/ApiLayers/`. Run the harness with `dotnet run --project samples-compare -c Release -- run http://localhost:18000 dynoxide` against your own emulator, and set `SPIKE_LEAN_HTTP=1` for the reduced path. All 43 checks passed on Dynoxide and DynamoDB Local on both HTTP paths **[M]**.
