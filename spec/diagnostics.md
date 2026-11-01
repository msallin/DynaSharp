# DynaSharp - diagnostics

What the generator and the analyzers of DynaSharp report about entities and the code that uses them, and how to fix it. The help link of every
diagnostic in the IDE or the build output points at a section of this page, one per family of rules, so the headings here are stable: a test keeps every
link on a heading that exists and names the rule.

## Entities

The source generator reports these on `[DynamoDbEntity]` and `[DynamoDbDocument]` types. Each comes with what to change, and a type with an error generates no code, so the `CS0311` on
its table that a build shows beside it goes away with it.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA001` | The type or a containing type is not `partial` | Make them all `partial` |
| `DYNA002` | An entity has no partition key | Add `[PartitionKey]`, or use `[DynamoDbDocument]` for a nested type |
| `DYNA003` | A property has a type that cannot be stored | Add a `[DynamoDbConverter]`, or `[DynamoDbIgnore]` |
| `DYNA004` | Two members want one attribute | Rename one with `[DynamoDbAttribute]` or `AttributeName` |
| `DYNA005` | No single constructor can be used | Mark one with `[DynamoDbConstructor]` |
| `DYNA006` | A constructor parameter has no property | Name it after a mapped property |
| `DYNA007` | The type cannot be mapped, for the reason the message gives | Map another type, or change what the message names |
| `DYNA008` | A converter does not implement `IDynamoDbConverter<T>` for the property | Fix its interface, or make it accessible |
| `DYNA009` | A key is declared twice | Keep one declaration |
| `DYNA010` | An attribute name DynamoDB would refuse | Pick another |
| `DYNA011` | `[Version]` on something that is no single integer property | Put it on one such property, without a converter |
| `DYNA012` | An attribute that only an entity can have, on a document | Remove it, or make the type an entity |
| `DYNA013` | A type is an entity and a document | Keep one |
| `DYNA014` | A member clashes with what is generated, such as `Keys` | Rename the member |
| `DYNA015` | The project is below C# 11 | Raise `LangVersion` |
| `DYNA016` | A type a member needs is less visible than the generated code | Make it as visible as the entity |
| `DYNA017` | A key whose type DynamoDB cannot hold | Use a string, number or binary, or a converter |
| `DYNA018` | An index that cannot exist, as the message says | Fix its name or keys |
| `DYNA019` | The type is outside a nullable context | Enable nullable reference types |
| `DYNA020` | An entity has no discriminator | Add the `[Discriminator("ROOM")]` the message suggests, or `[Discriminator(OptOut = true)]` for a table written by other tools |
| `DYNA021` | A plain key is `[DynamoDbIgnore]` | Remove one of the two |
| `DYNA022` | `[Discriminator]` has an empty value, or both a value and `OptOut`, or neither | Set one |
| `DYNA023` | A `[Version]` property feeds a key | Take it out of the key: a write changes it |
| `DYNA024` | An enum without members | Add a member, make the property nullable, or add a converter |
| `DYNA025` | A type is named like a class generated into it | Rename the type |

`DYNA026` (warning): two entities of a compilation have the same discriminator in the same attribute. A typed read and a mixed read tell types apart by that value, so on one table the items of one read as
the other, or fail to. Give one of them another value. Entities that never share a table are fine, which is why this is a warning: suppress it there. It needs every type of the compilation, so an IDE shows
it with full solution analysis, a build always does, and a type in another assembly is not looked at.

`DYNA030`: `[OmitEmpty]` on a property that is no `string?`, or that feeds a key. An empty string counts as null only where null can be left out, which a key cannot be, so take it off, or make the property a `string?` outside the keys. On a type the attribute passes over such
properties by itself. `[OmitEmpty]` on a type never reports this.

## Key templates

The source generator reports these on key templates. The grammar is in `requirements.md`, and the message of each says which part of a template it is about.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA100` | A template does not parse | Fix it at the position the message gives |
| `DYNA101` | A literal holds something other than printable ASCII | Keep literals to that |
| `DYNA102` | A placeholder names no property | Check the spelling |
| `DYNA103` | A property of a type that a key cannot hold | Use a type of the format table, or a plain key with a converter |
| `DYNA104` | A format that does not fit the type | Use one that the message lists: `plain`, `d<w>` or `n<i>` for integers, `plain` or `n<i>.<f>` for `decimal`, `yyyy` to `ticks` for timestamps, `d<w>` or none for enums, and none for `string`, `Guid`, `DateOnly` and custom types |
| `DYNA105` | A format with a width that is out of range | Use a smaller one |
| `DYNA106` | Two placeholders follow each other with nothing to tell them apart | Put a literal between them, or give the first a fixed width |
| `DYNA107` | A nullable property in a key that is not sparse | Make the index `Sparse`, or the property required |
| `DYNA108` (warning) | A number without a fixed width in a sort key, so `10` sorts before `9` | Use `d<w>` or `n<i>`, or `plain` if the order does not matter |
| `DYNA109` | `[KeyOnly]` that cannot work, as the message says | Hold the value in a key without loss, or store it |
| `DYNA110` (warning) | A sparse index whose inputs are all required, so it holds every item | Remove `Sparse`, or make an input nullable |
| `DYNA111` | A property twice in one template | Use it once |
| `DYNA112` | A template on a property, or a plain key on a type | Move it |
| `DYNA113` | An enum with `[Flags]` or aliases, whose names are not one-to-one | Use `d<w>` |
| `DYNA114` | A computed property in an index template, which no update can follow | Use a stored property |
| `DYNA115` (info) | A string before a delimiter in a sort key, so values with a smaller character sort out of order | Use a delimiter below the values, or accept it: the caveat is real but rare |

`DYNA116` (warning): a `DateTime` or `DateTimeOffset` in the default format, or in `s`, `ms` or `us`, is the last placeholder of the key of a table. `[SortKey("{At}")]` keeps milliseconds, so two puts
0.5 ms apart write one key, and the second replaces the first without an error. Add a placeholder after the timestamp that breaks the tie, such as `{At}#{Id}`, or write `{At:ticks}`, which keeps all seven
digits. It is the end of the whole key that counts: the sort key, or the partition key of a table without one. So a partition key that ends in a day beside a sort key is fine, and an index is exempt,
because it is not unique. A format of a minute or coarser, such as `{Window:yyyy-MM-ddTHH:mm}` or `{Month:yyyy-MM}`, says one item per period, which is the design of a counter or a summary, so it is
not reported. A plain `[SortKey]` on the property goes through its converter and keeps every digit.

`DYNA117` (info): a key property starts empty and nothing has to set it, such as `public string EntityPartition { get; set; } = string.Empty;`, set later with a `with`. Forgetting that compiles, and the
write throws `KeyFormatException` for a template or is rejected by DynamoDB for a plain key. Make it `required`, or set it in a constructor. It looks at every key of the entity, because a put writes the
keys of its indexes too. Only a default that cannot be a key counts: `""`, `string.Empty`, `null!`, `default!`, and for a binary key `[]` and `Array.Empty<byte>()`. A default such as `"main"` or
`Guid.NewGuid().ToString()` is a key of its own, and so is not reported, and neither is a property that is `required`, computed, nullable or has no initializer, or that a constructor mentions.

## Commands

A command such as `PutCommand<T>` is a `ref struct` over pooled state. That is what makes building a request free of allocation, and it
is also what stops a handle from being used after its buffers went to another request. So a command is built and sent in one expression.

`DYNA201` (warning) stands beside the compiler error when a command outlives its expression. The compiler says what is wrong; this says
what to write instead.

| What the code does | The compiler says | Write instead |
|---|---|---|
| Stores a command in a field or property | CS8345 (no `DYNA201`: the compiler stops before analyzers run) | A method that builds it, which is the template: `UpdateCommand<Customer> IncrementOrders(string id) => customers.Update(Customer.Keys.Of(id)).Add(Customer.Attrs.OrderCount, 1);` |
| Keeps one across an `await` or a `yield return` | CS4007 | Compute every value first, then build and send in one synchronous stretch. `if (x) { command = command.Set(...); }` between the steps is fine: each call returns a new handle to the same local. |
| Takes one as a parameter of an `async` method, or as an `async` local before C# 13 | CS4012, CS9202 | A synchronous helper that takes and returns the command, and an `await` in the caller. |
| Captures one in a lambda or a local function | CS8175, CS9108 | Build inside the lambda: `pipeline.ExecuteAsync(ct => customers.Update(key).Add(attr, 1).SendAsync(ct))`. A retry from outside has to rebuild anyway, because the buffers are gone after the send. |

Sending a command twice is not a compile error. It throws `InvalidOperationException`, and so does a second use of a handle after a
fluent call: build the command again. `DYNA204` reports what the build can see of that.

`DYNA202` (warning) reports `await shop.Query("a")` and `shop.Query("a").SendAsync()`. A read can end in several ways and none of them is
the default, so name one: `ItemsAsync`, `ItemAsync` on a query, `PageAsync`, `PagesAsync` or `CountAsync`. Without this rule the compiler answers with a constraint
on a type parameter (CS0315), which says nothing about what to write. An extension method of your own that makes a read awaitable is not
reported.

`DYNA203` (error) reports a statement whose value is a command or a step of a transaction: `events.Put(e);`, `query.Descending();`, or a helper of your own that returns one. Nothing sends it, and a
call on a handle uses that handle up, so a chain with such a statement in the middle throws at its next use. Send it, or keep what the call returns: `query = query.Descending();`. An assignment, a
`return` and an `await` use the value, and `_ = command;` says that dropping it is meant. A body such as `() => table.Put(item)` of a lambda that returns nothing is such a statement too: without
this rule, `Task.Run(() => table.Put(item))` is a write that never happens. It is an error, because the code around it does not do what it says, and no use of a command is left out by it.

`DYNA204` (warning) reports a call on a command in a variable when an earlier call has used that variable up on some path: `var a = q.Limit(1); var b = q.Limit(2);`, or
`_ = command.ToRequestJson(); await command.SendAsync();`. Every call gives the next handle back, so assign it: `q = q.Limit(1);`. It follows branches and loops, so a call on each side of an `if`, a
reassignment in a loop and an early `return` are fine, and `if (debug) log(command.ToRequestJson());` before the send is not: that path throws, when someone turns debug on. Build a command for the log
instead. A handle copied to a second variable is not followed, and a graph does not know that two conditions go together, so two separate `if`s on one flag count as a path through both.

`DYNA205` (warning) reports `Set`, `SetIfNotNull`, `Add`, `Remove`, `SetIfNotExists` or `Delete` by the handle of a property that the key of the table is made of, on a command of that entity:
`events.Update(key).Set(DeviceEvent.Attrs.At, now)`. DynamoDB cannot change a key, so the same call throws when the command ends, or DynamoDB rejects it for a plain key. Delete the item and put a new one.
It is about the property itself: a property that feeds only a key of an index stays writable, and a raw path is not looked at.

`DYNA206` (warning) reports `IfNotExists()` on a `Put` whose item shows a version above 0 in the chain, in a transaction too: `orders.Put(new Order { Id = id, Revision = 1 }).IfNotExists()`, or
`orders.Put(order with { Revision = 1 })`. An item that does not exist has no version, so the two can never both hold, and the command throws `InvalidOperationException` when it ends. A first write leaves
the version at 0, which already means that no item has the key, so `order with { Revision = 0 }` and `IfNotExists` agree. To write without looking at versions, `IgnoreVersion()` gives the check up, and
then `IfNotExists` is yours to use. Only a constant in an object initializer or a `with` in the chain counts: a version in a variable or from a method is not known here, and it is 0 as often as not, so
it is not reported, and the run-time check answers for it.

`DYNA207` (warning) reports a constant name that the entity does not declare, in the `Index` that takes the keys from the entity: `events.Index("Alertz")`, or `shop.Index<Order>("Alertz")`. It would
throw `ArgumentException` when the code runs. Take the name from `T.Indexes`. A name that is not constant is not looked at, and neither is `Index(name, partitionKey, sortKey)`, which names its keys itself.

## Expressions

In an interpolated expression such as `$"{Attr("status")} = {"open"}"`, the text is DynamoDB's grammar, an attribute in a hole stands for
its name, and anything else in a hole is a value. DynaSharp generates every placeholder.

`DYNA210` (warning): a string stands where the grammar wants a name, as in `$"{"status"} = {"open"}"`. It is a value there, so the
expression compares two values and DynamoDB accepts that as a constant. Wrap the name in `Attr(...)`, from `using static DynaSharp.Expr;`.
The IDE offers this as a fix. For a string that is not constant the analyzer sees nothing, and an expression that names no attribute at
all throws `ArgumentException` when the command is built.

`DYNA211` (error): the text holds `#name` or `:value` placeholders of its own. They would clash with the generated ones. Write the name
in `Attr(...)` and the value in a hole.

`DYNA212` (warning in 0.x): the expression breaks DynamoDB's grammar: a parenthesis that is not closed, a function that does not exist or
has the wrong number of operands, `BETWEEN` without `AND`, more than 100 operands of `IN`. Only the first error is reported, because what
follows it is noise. Function names are case-sensitive, and condition functions and update functions are separate groups. Fix the
expression, or if DynamoDB accepts it, suppress the warning and report the case: the grammar is a copy of the service's, and a warning
is what lets a false one pass.

`DYNA213` (warning): a constant path on a command of a mapped type starts with a name the type does not have, for example
`orders.Update(key).Set("Totl", 1)`. Check the spelling. An attribute outside the entity is legitimate, so suppress the warning there, or
pass the path in a variable, which the analyzer does not look at.

`DYNA214` (warning in 0.x): a constant path is not a path. A path is names joined by dots, with list indexes in brackets, such as
`a.b[2].c`, so `a..b` and `a[` are not. The same path throws `ArgumentException` when the command is built. An attribute whose name holds a
dot or a bracket cannot be addressed by a path at all.

`DYNA215` (warning): a handle of one mapped type on a command of another, as in `invoices.Query(key).Filter(Subscription.Attrs.Seats.Gt(1))`. A handle is a path and a converter, so this compiles, DynamoDB
looks for the attribute on items that do not have it, and the result is empty. Use a handle of the type of the command, or a raw path for an attribute outside it. A mixed read takes a handle of any of its
types, a type and its base types share their attributes, and a handle of a nested document belongs inside `.At(...)`. A command that cannot be followed back to its type, such as one in a generic helper, is
not looked at.

`DYNA216` (warning): `begins_with` or `contains` with a handle of a number or a boolean as its first operand, as in `$"contains({Order.Attrs.Lines}, {1})"`. It is valid grammar, DynamoDB finds no item, and the
result is empty. `Handle.BeginsWith(...)` on such a handle does not compile (`CS0619`), and this is the same mistake where a type cannot say so, because the text of an expression is not checked by the
compiler. Compare the attribute with `Eq`, `Gt` or `Between`, or store text. `contains` on a set or a list wants a handle of that type, and a converter of your own is not looked at.
