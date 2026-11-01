# DynaSharp - diagnostics

What the analyzers of DynaSharp report about code that uses its commands and expressions, and how to fix it. The help link of a
diagnostic in the IDE or the build output points at a section of this page, so the headings here are stable: a test keeps every link on
a heading that exists. The rules on entities and key templates (`DYNA001` to `DYNA115`) have no section, because their messages say what
to change.

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
the default, so name one: `ItemsAsync`, `PageAsync`, `PagesAsync` or `CountAsync`. Without this rule the compiler answers with a constraint
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
