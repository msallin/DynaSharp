# API reference

The reference lists the public types of `DynaSharp`, `DynaSharp.Testing` and `DynaSharp.Coordination`, with the XML docs of every member. Most code needs two namespaces: `DynaSharp` for the client, the tables and the commands, and `DynaSharp.Mapping` for the attributes on your types.

## Where to start

- <xref:DynaSharp.DynamoDbClient> and <xref:DynaSharp.DynamoDbClientOptions>: the client, and what it reads from the environment. The remarks of `MaxAttempts` are the rules of retries.
- [Table&lt;T&gt;](xref:DynaSharp.Table%601): every command of a table, from `Get` to `CreateTable`. A command such as [UpdateCommand&lt;T&gt;](xref:DynaSharp.UpdateCommand%601) lists its clauses and terminals, and its `SendAsync` says what a retry does to it.
- <xref:DynaSharp.Item> and <xref:DynaSharp.AttributeValue>: an item without a model.
- <xref:DynaSharp.DynamoDbException> and the types derived from it: what a failure carries.

## Namespaces

| Namespace | You need it for |
|---|---|
| <xref:DynaSharp> | Everything a user of a table names: the client and its options, `Table<T>` and `Index<T>`, the commands and their results, `Item` and `AttributeValue`, `Attr`, `Expr`, `Condition`, `SortKeyCondition`, `PartitionKey<T>`, `ContinuationToken`, and every exception. `using static DynaSharp.Expr;` brings `Attr("name")` |
| <xref:DynaSharp.Mapping> | The attributes that map a type, from `[DynamoDbEntity]` to `[Version]`, and the interfaces the generator implements |
| <xref:DynaSharp.Serialization> | Converters: the built-in ones, such as `EpochSecondsConverter`, and `IDynamoDbConverter<T>` for a converter of your own |
| <xref:DynaSharp.Requests> | The request API: a method per DynamoDB operation, its request and response records, and table administration beyond `CreateTable()` |
| <xref:DynaSharp.Credentials> | The providers of credentials, for a source other than the default chain |
| <xref:DynaSharp.Streams> | `DynamoDbStreamsClient` and its records, the stream of a table, and its changes as entities: `Change<T>` and `MixedChange<...>` |
| <xref:DynaSharp.Testing> | The package `DynaSharp.Testing`: `StubHandler`, `StubClient` and `StubResponse` for tests without AWS, and `FakeDynamoDb`, an in-memory DynamoDB |
| <xref:DynaSharp.Coordination> | The package `DynaSharp.Coordination`: `LockTable`, `Lease` and `IsHeld`, for locks with fencing tokens and leader election |
| <xref:DynaSharp.Coordination.Testing> | What a test of the locks does as another process would, and `LockState` |
| <xref:DynaSharp.Expressions> | `ICondition`, for a condition type of your own. The structs that conditions are made of are return types, not written by hand |

## What is not here

- **Generated members.** `Keys`, `Attrs` and `Indexes` are generated into your assembly, so they have no page here: [Generated code](../generated-code.md) lists them.
- **Infrastructure.** Types and members that exist for generated code, such as `KeyWriter`, `DynamoDbEntityInfo` and the structs of conditions, are public but hidden from IntelliSense and from this reference. They change with the package.
