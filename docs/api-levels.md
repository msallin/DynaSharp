# API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry.

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
| **Requests** (`using DynaSharp.Requests`) | `client.GetItemAsync(new GetItemRequest { ... })` | Every DynamoDB operation as a request and a response that mirror the service. Table administration beyond `CreateTable()` lives here. |
| **Raw** | `client.SendRawAsync("ListTables", """{}"""u8.ToArray())` | UTF-8 JSON in, UTF-8 JSON out, through the same signing and retries. |

You can tell which level a line uses by what it names: `Keys` and `Attrs` mean a typed table, strings mean `Table<Item>`, and `new ...Request` means the request API. `item.To<T>()` and `Item.From(entity)` cross between levels.

## One command engine

The first two levels share one command engine: ref struct chains that write JSON as you call them. A chain is built and sent in one expression, so it works in pooled buffers that go back at the end, and [the benchmarks](benchmarks.md) show what a call allocates in all. An analyzer explains the compiler error if you hold on to one across an `await`: `DYNA201` under [Commands](diagnostics.md#commands) on the diagnostics page.

The [API reference](https://msallin.github.io/DynaSharp/api/DynaSharp.html) lists every type of every level.
