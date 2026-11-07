# API levels

There is one client and one pipeline, with four ways in. Use the highest level that fits. You can drop down for one call: all levels share a client, its retries and its telemetry.

`orders` and `shop` are the tables of [the shop](getting-started.md#the-shop).

| Level | You write | Use it for |
|---|---|---|
| **`Table<T>`** with generated entities | `orders.Get(Order.Keys.Of("alice", 1))` | Application code. Start here. |
| **`Table<Item>`** | `shop.Get("CUSTOMER#alice", "PROFILE")` | The same commands without a model: tools, migrations, attributes you do not map. |
| **Requests** (`using DynaSharp.Requests`) | `client.GetItemAsync(new GetItemRequest { ... })` | Every DynamoDB operation as a request and a response that mirror the service. Table administration beyond `CreateTable()` lives here. |
| **Raw** | `client.SendRawAsync("ListTables", """{}"""u8.ToArray())` | UTF-8 JSON in, UTF-8 JSON out, through the same signing and retries. |

You can tell which level a line uses by what it names: `Keys` and `Attrs` mean a typed table, strings mean `Table<Item>`, and `new ...Request` means the request API. `item.To<T>()` and `Item.From(entity)` cross between levels.

## One command engine

The first two levels share one command engine, whose chains are built and sent in one expression: a command cannot cross an `await`, and `DYNA201` under [Commands](diagnostics.md#commands) explains the compiler error if one does. [How it is built](design.md#commands-are-ref-structs-over-borrowed-buffers) says why.

The [API reference](api/index.md) lists every type of every level.
