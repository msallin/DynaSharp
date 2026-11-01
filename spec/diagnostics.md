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
fluent call: build the command again.

`DYNA202` (warning) reports `await shop.Query("a")` and `shop.Query("a").SendAsync()`. A read can end in several ways and none of them is
the default, so name one: `ItemsAsync`, `ItemAsync` on a query, `PageAsync`, `PagesAsync` or `CountAsync`. Without this rule the compiler answers with a constraint
on a type parameter (CS0315), which says nothing about what to write. An extension method of your own that makes a read awaitable is not
reported.

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
