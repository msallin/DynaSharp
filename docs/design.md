# How it is built

Why the client looks the way it does. The [design principles](design-principles.md) say what DynaSharp holds itself to; this page says what follows from them in the code, and what you get for it. Nothing here changes how a call behaves: the API reference says what each member does, and [Performance](performance.md) has the numbers.

## Commands are `ref struct`s over borrowed buffers

A command such as `orders.Get(key).Consistent()` writes its request as you call it, straight into UTF-8 buffers that the client borrows from a pool and gives back when the request is sent. That is why a command is built and sent in one expression: a `ref struct` cannot be stored in a field, captured in a lambda or kept across an `await`, and the analyzer explains the compiler error ([`DYNA201`](diagnostics.md#commands)). Every clause returns a new command and spends the one it was called on, so a command that is reused throws, and the same request is sent twice by building it twice. The gain is a `GetItem` or `PutItem` that allocates nothing beyond its result, which the [benchmarks](performance.md#benchmarks) show.

`CreateTableCommand` is the exception: administration is rare, so it is an ordinary class that can be kept and sent again.

`ToRequestJson()` renders the whole request into one string, for tests and debugging. It ends the command like a send does.

## Conditions are nested structs

`Order.Attrs.Total.Gt(100m).And(Order.Attrs.Status.Eq(OrderStatus.Placed))` is a struct whose type names its parts, `And<Compare<...>, Compare<...>>`. Writing it into the request is a chain of direct calls with no delegate, no boxing and nothing on the heap. The price is a static type that changes with every `.And()`, which is awkward for a field or a condition assembled in a loop. `Condition.Of(...)` wraps one in a `Condition` object, whose type stays the same and which `&`, `|` and `!` combine; that costs one allocation per condition, once, where the field is set.

The same holds for the converter of an attribute: `Attr<decimal, NumberConverter<decimal>>` carries its converter as a type argument, so writing a value is a direct call. A value of the wrong type does not compile.

## Keys hold values, not text

`Order.Keys.Of("alice", 1)` is a struct that holds the two values and formats the key into the request when the command is written. It has checked the values by then, so `Keys.Of("a#b")` throws `KeyFormatException` at the call, and `Keys.TryOf` returns `null` for the same values. No string of the key exists until something needs one: `Order.Keys.Pk.Of(...)`, `Format` and `ToString` make it.

An interpolated expression, `$"{Attr("status")} = {"open"}"`, works the same way: the handler writes each literal part and each value into the request as the compiler reaches it, with a placeholder for every name and value, so the expression never exists as a string either.

## Reads go straight into the entity

A typed read parses the JSON of the response into the properties of the entity, through code the source generator wrote, with no `AttributeValue` and no dictionary in between. `Table<Item>` and the request API build an `AttributeValue` per attribute, because that is their contract, and `To<T>()` reads those again. Numbers stay text until a converter parses them, so the 38 digits of a DynamoDB number survive a round trip through `AttributeValue`, and a `decimal` reads exactly or throws.

An `Item` keeps its names and values in two arrays, and the items of one response share their names. A read item costs little more than its values, at any size. An item built by hand is cheap at the usual sizes, and above 32 attributes costs more than a `Dictionary` of known capacity, because it then keeps a lookup index of its own.

## What is checked at the call, and what at the send

Arguments are checked where they are passed: a null, an empty key, a path that is not valid. What depends on the whole command is checked when it is sent: a version without `IfVersion` or `IgnoreVersion`, an index key that an update cannot keep consistent, a transaction without an action. In a transaction, such a check runs at the next action, where the action before it settles. A `Select` of an attribute that an item can lack, and a `Filter` that a key condition makes pointless, are reported by analyzers at build time instead ([`DYNA209`](diagnostics.md#commands), [`DYNA221`](diagnostics.md#commands)).

## Failure paths pay, success paths do not

`ConditionalCheckFailedException.Key` is read back from the request only when the condition fails. `[OmitEmpty]` adds a length check to the write of a property that has it and nothing to a type that does not. `IsOmitted` and `TryReadMissing` of a converter are called only for the converters that implement them: the generator sees the implementation and leaves the call out everywhere else. Telemetry without a listener is a null check per call.

## One client, shared providers

A `DynamoDbClient` owns its HTTP connections, its retry quota and, without `Credentials` in the options, a credentials provider with its own cache, which is why there is [one per application](getting-started.md#configure-the-client). A second client, also a `DynamoDbStreamsClient`, makes a second set of requests to STS or the metadata service unless both get one provider, as [Credentials](credentials.md) describes.

## Testing through the handler

`Table<T>` is sealed and has no interface. The seam is the `HttpMessageHandler` of the client, so a test through [`StubHandler`](testing.md) or [`FakeDynamoDb`](testing.md) covers the mapping, the expressions and the wire format too, and a test cannot pass on a request the client would never send. The body of a request is valid only until `SendAsync` of the handler returns: the client reuses the memory for the next call, so a handler reads the body before it returns.
