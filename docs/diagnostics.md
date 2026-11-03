# Diagnostics

What the generator and the analyzers of DynaSharp report about entities and the code that uses them, and how to fix it. The help link of every
diagnostic in the IDE or the build output points at a section of this page, one per family of rules, so the headings here are stable: a test keeps every
link on a heading that exists and names the rule.

## Entities

The source generator reports these on `[DynamoDbEntity]` and `[DynamoDbDocument]` types, and `DYNA026`, `DYNA032` and `DYNA033` come from analyzers. Each comes with what to change, and a type with an error generates no code, so the `CS0311` on
its table that a build shows beside it goes away with it. `DYNA020` is the one exception: the type is generated as if it opted out, so its uses compile and the build fails on that error alone.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA001` | The type or a containing type is not `partial` | Make them all `partial` |
| `DYNA002` | An entity has no partition key | Add `[PartitionKey]`, or use `[DynamoDbDocument]` for a nested type |
| `DYNA003` | A property has a type that cannot be stored | Add a `[DynamoDbConverter]`, or `[DynamoDbIgnore]` |
| `DYNA004` | Two members want one attribute, also after `Naming` converted their names | Rename one with `[DynamoDbAttribute]` or `AttributeName` |
| `DYNA005` | No single constructor can be used | Mark one with `[DynamoDbConstructor]` |
| `DYNA006` | A constructor parameter has no property | Name it after a mapped property |
| `DYNA007` | The type cannot be mapped, for the reason the message gives: it is abstract, static, generic, a ref struct, an interface, or file-local, which the generated code cannot merge with | Map another type, or change what the message names |
| `DYNA008` | A converter does not implement `IDynamoDbConverter<T>` for the property, or for the type that `[assembly: DynamoDbDefaultConverter]` names | Fix its interface, or make it accessible |
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
| `DYNA020` | An entity has no discriminator. The type is still generated, as with `OptOut`, so its uses compile and this error alone fails the build | Add the `[Discriminator("TRANSPORT_RECORD")]` the message suggests for `TransportRecord`, its name in snake case and upper case, or `[Discriminator(OptOut = true)]` for a table written by other tools. `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]`, which the message names as well, does that for every entity of a project |
| `DYNA021` | A plain key is `[DynamoDbIgnore]` | Remove one of the two |
| `DYNA022` | `[Discriminator]` has an empty value, or both a value and `OptOut`, or neither | Set one |
| `DYNA023` | A `[Version]` property feeds a key | Take it out of the key: a write changes it |
| `DYNA024` | An enum without members | Add a member, make the property nullable, or add a converter |
| `DYNA025` | A type is named like a class generated into it | Rename the type |

`DYNA026` (warning): two entities of a compilation have the same discriminator in the same attribute. A typed read and a mixed read tell types apart by that value, so on one table the items of one read as
the other, or fail to. Give one of them another value. Entities that never share a table are fine, which is why this is a warning: suppress it there. It needs every type of the compilation, so an IDE shows
it with full solution analysis, a build always does, and a type in another assembly is not looked at.

`DYNA029`: `[assembly: DynamoDbDefaultConverter]` that cannot work: a second default for a type, or a type that no property could match, which is a nullable type, because a default for the type covers its nullable form, and an open generic type, because a default is for one closed type. Keep one, or name the closed type without its `?`. It is reported once, on the attribute, whether a type uses it or not, and a type that would use it takes the first default or the built-in, so nothing else fails with it.

`DYNA030`: `[OmitEmpty]` on a property that is no string, list, set, dictionary or array, that feeds a key, or that a read cannot do without. An empty value counts as null only where null can be left out, which a key cannot be, so take it off, or move the property out of the keys. A `required` property that is neither nullable nor a set, or a constructor parameter of that kind without a default, could never be read back, because a read throws for the attribute that an empty value leaves out: take the attribute off, make the property nullable, or give it a default. On a type the attribute passes over such
properties by itself, and they stay written. `[OmitEmpty]` on a type never reports this, and neither does `[OmitEmpty(false)]`.

`DYNA031` (warning): a property of an entity whose type is an entity too, or a list, set, dictionary or array of one, such as `public Customer Buyer { get; init; }` on an `Order`. It is stored as a map, and the copy carries the keys, the index keys and the discriminator of a customer, which no read of an order takes and no index sees. Give the property a `[DynamoDbDocument]` type that holds what the order needs. A snapshot of an entity that is meant to be one is fine: suppress the warning there. A property that is `[DynamoDbIgnore]`, computed, or has a converter of its own is not stored as a map and is not reported, and neither is an entity inside a document.

`DYNA032` (warning): the getter of a property with `[ComputedFrom]` reads a stored property of the same instance that the attribute does not name, such as `[ComputedFrom(nameof(First))] public string Full => First + Last;` beside `[GlobalIndexKey("ByName", PartitionKey = "NAME#{Full}")]`. An update knows what a key is computed from only by the attribute, so `people.Update(key).Set(Person.Attrs.Last, "Doe")` goes through, and the index keeps the key with the old last name. Name every stored property the getter reads. It is reported once for each such property, where the getter reads it first. The getter is read as it is written: what a method or a property of the type that it calls reads, such as `Format()`, is not followed, and neither is a property of another instance. The version, a `[KeyOnly]` property and a computed one cannot be named, so they are not reported. A getter that reads a property that does not change its result is reported as well: suppress the warning there.

`DYNA033` (warning): `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` on an enum whose generated converter does not read it by name, so the attribute changes nothing. A `[Flags]` enum is stored as a number, which has no name to miss, and an enum with a `[DynamoDbConverter]` of its own is read by that converter: take the attribute off, or answer `CountsAsMissing` in the converter. It is reported once, on the attribute, whether a type uses the enum or not. `UnknownEnumName.Throw` is what happens without the attribute, so it is never reported.

## Key templates

The source generator reports these on key templates. The grammar is in `requirements.md`, and the message of each says which part of a template it is about. Analyzers report what code shows of a template: `DYNA120` and `DYNA121` through the getter of a computed input, and `DYNA122` on a call of a key builder.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA100` | A template does not parse | Fix it at the position the message gives |
| `DYNA101` | A literal holds something other than printable ASCII | Keep literals to that |
| `DYNA102` | A placeholder names no property | Check the spelling |
| `DYNA103` | A property of a type that a key cannot hold | Use a type of the format table, or a plain key with a converter |
| `DYNA104` | A format that does not fit the type | Use one that the message lists: `plain`, `d<w>` or `n<i>` for integers, `plain` or `n<i>.<f>` for `decimal`, `yyyy` to `ticks` for timestamps, `d<w>` or none for enums, `raw` or none for `string`, and none for `Guid`, `DateOnly` and custom types |
| `DYNA105` | A format with a width that is out of range | Use a smaller one |
| `DYNA106` | Two placeholders follow each other with nothing to tell them apart | Put a literal between them, or give the first a fixed width |
| `DYNA107` | A nullable property in a key that is not sparse | Make the index `Sparse`, or the property required |
| `DYNA108` (warning) | A number without a fixed width in a sort key, so `10` sorts before `9` | Use `d<w>` or `n<i>`, or `plain` if the order does not matter |
| `DYNA109` | `[KeyOnly]` that cannot work, as the message says | Hold the value in a key without loss, or store it |
| `DYNA110` (warning) | A sparse index whose inputs are all required, so it holds every item | Remove `Sparse`, or make an input nullable |
| `DYNA111` | A property twice in one template | Use it once |
| `DYNA112` | A template on a property, or a plain key on a type | Move it |
| `DYNA113` | An enum with `[Flags]` or aliases, whose names are not one-to-one | Use `d<w>` |
| `DYNA114` | A computed property in an index template, which no update can follow | Add `[ComputedFrom]` naming the stored properties it reads, or use a stored property |
| `DYNA115` (info) | A string before a delimiter in a sort key, so values with a smaller character sort out of order | Use a delimiter below the values, or accept it: the caveat is real but rare |

`DYNA116` (warning): a `DateTime` or `DateTimeOffset` in `s`, `ms` or `us` is the last placeholder of the key of a table. `[SortKey("{At:ms}")]` keeps milliseconds, so two puts
0.5 ms apart write one key, and the second replaces the first without an error. Add a placeholder after the timestamp that breaks the tie, such as `{At:ms}#{Id}`, or drop the format: a bare `{At}` is
`ticks`, which keeps all seven digits and is not reported. It is the end of the whole key that counts: the sort key, or the partition key of a table without one. So a partition key that ends in a day beside a sort key is fine, and an index is exempt,
because it is not unique. A format of a minute or coarser, such as `{Window:yyyy-MM-ddTHH:mm}` or `{Month:yyyy-MM}`, says one item per period, which is the design of a counter or a summary, so it is
not reported. A plain `[SortKey]` on the property goes through its converter and keeps every digit.

`DYNA117` (warning): a key property starts empty and nothing has to set it, such as `public string EntityPartition { get; set; } = string.Empty;`, set later with a `with`. Forgetting that compiles, and the
write throws `KeyFormatException` for a template or is rejected by DynamoDB for a plain key. Make it `required`, or set it in a constructor. It looks at every key of the entity, because a put writes the
keys of its indexes too. Only a default that cannot be a key counts: `""`, `string.Empty`, `null!`, `default!`, and for a binary key `[]` and `Array.Empty<byte>()`. A default such as `"main"` or
`Guid.NewGuid().ToString()` is a key of its own, and so is not reported, and neither is a property that is `required`, computed, nullable or has no initializer, or that a constructor mentions.

`DYNA118`: a placeholder with the format `raw` that does not end the template, such as `{Key:raw}#{Id}` or `{Key:raw}#x`. A raw value keeps its delimiters, so nothing in the text says where it stops, and `Parse` and a read take everything to the end of the key. Anything behind it could not be read back. Move the placeholder to the end of the template, or take off `raw`, which makes a value with a delimiter throw `KeyFormatException`.

`DYNA119`: a `[ComputedFrom]` that cannot work, as the message says. It belongs on a computed property that a key template uses, and it names at least one property, each once, of the same type: one with an attribute of its own, so neither computed nor `[DynamoDbIgnore]` nor `[KeyOnly]`, and not the `[Version]`, which an update moves without a handle, so that nothing would see the change. On a document it is `DYNA012`.

`DYNA120` (warning): a property that is `raw` in one template and strict in another, such as `[PartitionKey("A#{Key:raw}")]` beside `[GlobalIndexKey("ByKey", PartitionKey = "B#{Key}")]`. A format belongs to its template, so this compiles, and a `Put` of `a#b` passes the first key and throws `KeyFormatException` at the second. It is reported on the strict template, once for each one. Use `raw` in both, at the end of each template, or in neither: `raw` must end its template, so where text follows the placeholder only neither is left, and anything else is `DYNA118`. Only a template with a delimiter refuses anything, so a strict `{Key}` with nothing beside it and a plain key are not reported, and a property that really takes a smaller set of values in one key can keep this with `#pragma warning disable DYNA120`.

It follows `[ComputedFrom]` too. A computed input that is strict in a template with a delimiter, such as `[GlobalIndexKey("ByUpper", PartitionKey = "B#{Upper}")]` over `[ComputedFrom(nameof(Key))] public string Upper => Key.ToUpperInvariant();` beside `[PartitionKey("A#{Key:raw}")]`, is reported when its getter hands the raw input on with every character: `=> Key`, `=> Key.ToUpperInvariant()`, `=> Key.ToLowerInvariant()`, `=> First ?? Second`, or a body whose every `return` is one of these, where a `throw` hands nothing on. That warning stands on the computed property, once for each strict template of it, and names the property it goes through. Any other getter is `DYNA121`.

`DYNA121` (info): the same through a getter that does anything else with the raw input, such as `Key.Replace('#', '-')`, a hash, or `Key + "x"`. A `Replace` or a hash takes the delimiter out, and then the strict template takes every value; a concatenation keeps it, and the strict template throws `KeyFormatException` for a value that the raw one took. Only the getter tells, so look at it. It is info because the getter may be right, and for that reason it is also reported for a getter that ends up not using the raw input at all.

`DYNA122` (warning): a constant for a placeholder of a generated key builder that no key can hold, because it is empty or holds a delimiter of the template, such as `Order.Keys.Of("a#1")` beside `[PartitionKey("ORDER#{Id}")]`, or `Order.Keys.Sk.BeginsWith("")`. Every run of the call throws `KeyFormatException`. Any constant counts, a `const`, a concatenation of constants and `string.Empty` too, in `Keys.Of` and in the builders of each template, such as `Format`, `Of`, `Eq`, `BeginsWith`, `PrefixPartial` and `Between`, of the table and of an index. Change the value, or make the placeholder `raw` where it ends the template: a raw placeholder takes a delimiter and is reported only for an empty value. `TryOf`, which answers false and does not throw, `Parse`, which takes a whole key, a plain key, which has no template, and a value that is not constant are not looked at, and of what a key refuses only an empty value and a delimiter are: a control character or U+10FFFF in a constant still throws at run time without this warning.

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
the default, so name one. Every read has `ItemsAsync`, which is `ValuesAsync` after `Select`. A query adds `ItemAsync`, which is `ValueAsync` after `Select`, and most reads add `PageAsync`, `PagesAsync` and `CountAsync`, but not all of them: a `Select` and a mixed read have no `CountAsync`, a fan-out has no `PagesAsync`, and a parallel scan has `ItemsAsync` alone. Without this rule the compiler answers with a constraint
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
`events.Update(key).Set(DeviceEvent.Attrs.At, now)`. DynamoDB cannot change a key, so the same call throws when the command ends, or DynamoDB rejects it for a plain key. Delete the item and put a new one. To store the inputs of the table key on an upsert, call `StoreKeyInputs()`.
It is about the property itself: a property that feeds only a key of an index stays writable, and a raw path is not looked at.

`DYNA219` (warning) reports the same calls by the handle of a property that a computed input of a key of an index is derived from, on a command of that entity: `visits.Update(key).Set(Visit.Attrs.Message, message)`, also for a path inside it, `Visit.Attrs.Message.At(Msg.Attrs.Arrival)`. An update has no instance to run the getter on, so the same call throws when the command ends. So does a write by the handle of such a computed input itself, `badges.Update(key).Set(Badge.Attrs.Label, label)`, where the key would follow the value of the update and not the getter. Put the changed entity instead. A property that a computed input of the key of the table is derived from is `DYNA205`, one step removed. A raw path is not looked at, and a command that cannot be followed to its type is not either.

`DYNA206` (warning) reports `IfNotExists()` on a `Put` whose item shows a version above 0 in the chain, in a transaction too: `orders.Put(new Order { Id = id, Revision = 1 }).IfNotExists()`, or
`orders.Put(order with { Revision = 1 })`. An item that does not exist has no version, so the two can never both hold, and the command throws `InvalidOperationException` when it ends. A first write leaves
the version at 0, which already means that no item has the key, so `order with { Revision = 0 }` and `IfNotExists` agree. To write without looking at versions, `IgnoreVersion()` gives the check up, and
then `IfNotExists` is yours to use. Only a constant in an object initializer or a `with` in the chain counts: a version in a variable or from a method is not known here, and it is 0 as often as not, so
it is not reported, and the run-time check answers for it.

`DYNA207` (warning) reports a constant name that the entity does not declare, in the `Index` that takes the keys from the entity: `events.Index("Alertz")`, or `shop.Index<Order>("Alertz")`. It would
throw `ArgumentException` when the code runs. Take the name from `T.Indexes`. A name that is not constant is not looked at, and neither is `Index(name, partitionKey, sortKey)`, which names its keys itself.

`DYNA208` (warning) reports a `Project` on a read of an entity that leaves out what the entity cannot be made without: `orders.Query(pk).Project("Total").ItemsAsync()`, where `Status` is `required`. The item comes back without it, and the read throws `DynamoDbMappingException` when it gets
there, for every item. Include the member, or use `Select(handle)`, which reads one attribute without the entity, on a `Get`, `Query` or `Scan`. It looks at what a read throws without, which are the non-nullable `required` members and the parameters of a constructor without a default. A `required string?` reads as null and a required set as empty, because that is what a write of them leaves, so they are not
reported. Neither is a key input that a key restores, because a read adds its keys, in a read of the table those of the table and in a read of an index those of the index too: an input that only a key of an index holds is reported on a read of the table, and one that a key cuts off, such as a timestamp in `{At:yyyy-MM-dd}`, on any read. A nested path such as `"ShipTo.City"` keeps its top-level member, whatever the nested
document needs. It looks at all `Project` calls of a chain from the table to its terminal, also on `BatchGet`, a fan-out and a mixed read, where every type counts. A value that is not constant, a chain that is kept in a variable, returned, or passes a method of your own, and a read of `Item` are not looked at, and neither is a `ToRequestJson()` or a `CountAsync()`, which read no item.

`DYNA209` (warning) reports `Select(handle)` of an attribute that an item can lack where a read of the entity goes on, because `Select` reads the attribute and nothing else, and throws `DynamoDbMappingException`. A converter of your own that answers `TryReadMissing` gives `Select` a value, so none of the cases below is reported for it.

The first such attribute is a stored property that a key of the table holds, such as `orders.Get(key).Select(Order.Attrs.UserId)` with `[PartitionKey("USER#{UserId}")]`. An update stores no input of the table key, so an item that an `Update` created has the key and not the attribute, and a read of the entity restores the value from the key. On items that a `Put` wrote `Select` reads the value. Read the entity for such a table, call `StoreKeyInputs()` on every update of the table, or suppress this where every item comes from a `Put`. A `[KeyOnly]` property has no handle at all, so that does not compile, a plain key is its own attribute, an input of a key of an index is set like any other attribute, and a nullable input is not reported, because a missing attribute reads as null. A timestamp counts only where the key holds all of it, which a bare `{PlacedAt}` does, since it is `ticks`: under `{PlacedAt:ms}` a read of the entity cannot restore it either and throws as `Select` does, so that is not reported.

Another is a property with `[OmitEmpty]` that is not nullable, such as `orders.Get(key).Select(Order.Attrs.Lines)` on a `List<OrderLine>`. An empty value is not stored, and a read of the entity keeps the initial value. Read the entity, or make the property nullable, which reads as null. A set is not reported, because a missing attribute reads as an empty set.

The last is a property whose converter implements `CountsAsMissing`, such as `ports.Get(key).Select(Port.Attrs.Position)` on a lenient enum. The attribute is there, but the converter counts its value as missing, and a read of the entity keeps the initial value. Read the entity, make the property nullable, which reads as null, or have the converter answer `TryReadMissing`. A `required` member is not reported, because a read of the entity throws as well, and neither is a plain key, which a read of the entity does not count as missing.

A `[Version]`, such as `orders.Get(key).Select(Order.Attrs.Revision)`, is not reported. Every write of the library stores it but an `Update` with `IgnoreVersion()` that creates the item, and on an item without one, from there or from other code, `Select` throws a `DynamoDbMappingException` that says the item was written without a version and points at `[Version(Missing = VersionMissing.Allow)]`, whose handle reads such an item as 0. That is louder than the 0 a read of the entity gives.

`DYNA217` (warning) reports a `RetryOnConflictAsync` inside the delegate of another one: `client.RetryOnConflictAsync(ct => new(client.RetryOnConflictAsync(Run, ct)))`. The runs multiply, 5 of 5 are 25, and an inner retry that used up its runs throws what it last caught, which the outer one counts as a conflict of its own, so one lost race on the inner item starts the whole run over. A run is one read-modify-write, and a follow-up write goes after the outer call. The delegate is a lambda, an anonymous method or a local function given by name, and the inner retry can stand in a lambda at any depth inside it, or in a method or local function of the same compilation that the delegate calls, one call deep, where it is reported at the retry in that method, also when the method has other callers. A delegate in a variable, a method given by name that is not a local function and a method of another assembly are not followed. A retry with a constant `maxAttempts: 1`, inside or outside, is no retry, so it multiplies nothing and is not reported.

`DYNA218` (warning) reports a version in the delegate of a `RetryOnConflictAsync` that stays the same from run to run: `var order = await orders.Get(key).ItemAsync(); await client.RetryOnConflictAsync(async ct => await orders.Put(order! with { Total = 2 }).SendAsync(ct));`. Every run sends that version and fails the same way, so read it inside the delegate. It looks at `IfVersion(x)`, and at `Put(item)` and `Delete(entity)` of an entity with a `[Version]` that take the version from the item, also a `Put` in a transaction, unless `IgnoreVersion()` stands on such a command or `IfVersion` replaces the version. It follows `order.Revision`, `order!`, `order with { ... }` and a conversion to a local or a parameter that the delegate captures and does not assign. A version on its own, in `IfVersion` or a `Delete`, needs no more than that: a parameter or a variable counts, because nothing but a read gives one, unless the variable holds a constant or an object made in the same member. A `Put` is different, because a new item built outside the delegate looks the same and is correct at version 0, for example beside a counter that the delegate reads: it is reported only when the member shows the item coming out of a call of the library, through one more local at most, in `var`, an assignment, `is { } order` or `await foreach`. So a `Put` of a parameter or of what a method of your own returned is not reported, whatever it holds. Neither is a `with` that sets the version to a value the delegate read, a variable a run can bring up to date, by a write to a member or by handing it to a method of your own, a variable that a lambda or local function assigns, fields and properties, `IfVersion(0)`, a version that is computed such as `IfVersion(x + 1)`, or a retry with a constant `maxAttempts: 1`. A `TransactionConflictException` can pass on a later run with the same version, so a retry over a version from outside is not useless for that alone, but a version that is stale still fails every run, and the rule stands for that. A retry whose constant `retryOn` leaves out `ConflictKind.ConditionalCheckFailed`, such as `ConflictKind.TransactionConflict`, is not reported: a stale version fails the first run and surfaces at once, which is what the version is for, and nothing runs again with it. A `retryOn` in a variable counts as one that repeats a failed condition.

`DYNA221` (warning) reports a `Filter` on a `Query` over a property that a key of what the query reads holds without loss, where the key condition decides it: `orders.Query(Order.Keys.Pk.Of(user)).Filter(Order.Attrs.UserId.Eq(user))` with `[PartitionKey("USER#{UserId}")]`. Every placeholder of the partition key is pinned by the query, so such a filter keeps the whole partition or nothing, and the first placeholder of the sort key is what `Order.Keys.Sk.Between(...)` and the other conditions of the sort key test, so put the condition there. On a key of the table the filter is also wrong, the way `DYNA209` is: an `Update` stores no input of the table key, so an item it created has the key and not the attribute, and the filter drops it. A plain key and the attribute of a key itself, `Attr("pk")`, are worse: DynamoDB rejects a filter on a key of the table or index that is read. It looks at handles and constant raw paths in the filter, and at what the query reads: a table of an entity, or an index from `Index(name)` or `Index(name, partitionKey, sortKey)` with constant names. A later placeholder of the sort key, such as the time a fact was recorded in `VALID#{ValidFrom}#TX#{RecordedAt:ticks}`, cannot be tested by a key condition alone and is not reported, and neither is an input of the table key in a query of an index, an index in a field or a variable, a `Scan`, a mixed read, or a bare name in the text of the filter.

`DYNA222` (info) reports the same where the key holds the property with loss, as `{IssuedOn:yyyy-MM}` holds a day: a filter to the day is finer than the key and correct, and the key condition can narrow the read to the month before it.

`DYNA223` (warning) reports two actions of one update on the same path, or on paths of which one holds the other: `Set(Order.Attrs.Total, 1m).Add(Order.Attrs.Total, 2m)`, or `Remove(Order.Attrs.ShipTo).Set(Order.Attrs.ShipTo.At(Address.Attrs.City), city)`. DynamoDB rejects the request, because the paths overlap. Paths that only share a parent, `ShipTo.City` and `ShipTo.Street`, or two items of a list, are fine. The update sends every action as it comes, so a `SetIfNotNull` counts as well, and only one of a constant `null`, or of an empty string on an `[OmitEmpty]` property, adds nothing and is not reported. A path is a handle, also through `At`, a constant string, or `Attr(...)` of one, and the actions are `Set`, `SetIfNotNull`, `Add`, `Remove`, `SetIfNotExists` and `Delete` from the verb to the end of the chain, or to the next step of a transaction. The text of `Set($"...")`, what the update writes by itself, such as a version, and a chain that goes through a variable are not looked at.

`DYNA224` (warning) reports a key that a builder of one attribute made, in a query that reads another: `orders.Index(Order.Indexes.ByStatus).Query(Order.Keys.Pk.Of(id))`. A partition key knows its attribute, so the query throws `ArgumentException`; use the builder of the index, `Order.Keys.ByStatusPk`. A condition on the sort key, `Order.Keys.Sk.Between(...)`, does not know it, and DynamoDB compares the sort key of the index with text made for another key, which finds the wrong items, unless both keys have the same template, where `{At}` and `{At:ticks}` count as one; on an index without a sort key the query throws. A key whose attribute is the one that is read is fine, as on a local index, which has the partition key of the table. It looks at what the query reads as `DYNA221` does, and at a key from `Of`, also as a method group in `Select`; a key from `TryOf` or in a variable, and `Keys.Pk.Format(...)`, which is text, are not looked at.

`DYNA225` (warning) reports `Including<T>()` of an entity whose key names are not those of the table: `orders.CreateTable().Including<Invoice>()`, where `Order` has `(pk, sk)` and `Invoice` `(Id)`. `Including` throws `ArgumentException`, because entities on one table share its key attributes. The table has the key of its entity, and a table of `Item` the key of the entity included first. Another type of a key attribute and an index that two entities declare differently throw there too, and are not reported.

`DYNA226` (warning) reports an update that sets by handle some inputs of a key of an index and not the others: `items.Update(key).Set(WorkItem.Attrs.Status, closed)` under `{Status}#{CreatedAt}`. It also reports one that sets a key of a sparse index without what the other key of that index is made of, as a restore of a soft delete that sets the time and not the owner. DynamoDB cannot build a key from what an item holds, so the update throws `InvalidOperationException` when it is sent, naming the `Set` to add, rather than leave the index with a key that no longer fits. Set the input that does not change as well, to the value the item holds, or put the changed entity. An input of the key of the table needs nothing, because the update has the key, so a design that keeps what does not change in the key of the table, or a [version-7 id](patterns-keys.md#newest-first-with-version-7-guid-sort-keys) that holds the time, needs no read before the write. An input that only a key holds, or that a getter computes, has no handle, and only a `Put` or a raw path writes such a key. It looks at a chain from `Update` to `SendAsync`, `ToRequestJson`, `Return`, an `await` or the next step of a transaction, and an input of the table key counts as known. An update in a variable or that a helper returns, a method of your own in the chain, a handle in a variable or of another type, and a raw path are not looked at. Neither is an index with an input that the update removes, or changes by `Add`, `SetIfNotExists` or `Delete`, or sets to a value that may be null, because those take the item out of a sparse index or throw something else, nor a write that `DYNA205` or `DYNA219` reports.

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
`orders.Update(key).Set("Totl", 1)`, and so does a bare name that a path starts with in the text of an expression, `orders.Scan().Filter($"Totl > {1}")`. Check the spelling. Under `Naming` of `[assembly: DynamoDbDefaults]` the attribute has the converted name, so `"Total"` is reported where the item holds `total`. An attribute outside the entity is legitimate, so suppress the warning there, or
pass the path in a variable, which the analyzer does not look at. In the text only the first name of a path counts, `ShipTo.Stret` is not looked at, and neither is the text of a step of a transaction, which has no type, or of a method of your own that takes the expression.

`DYNA214` (warning in 0.x): a constant path is not a path. A path is names joined by dots, with list indexes in brackets, such as
`a.b[2].c`, so `a..b` and `a[` are not. The same path throws `ArgumentException` when the command is built. An attribute whose name holds a
dot or a bracket cannot be addressed by a path at all.

`DYNA215` (warning): a handle of one mapped type on a command of another, as in `invoices.Query(key).Filter(Subscription.Attrs.Seats.Gt(1))`. A handle is a path and a converter, so this compiles, DynamoDB
looks for the attribute on items that do not have it, and the result is empty. Use a handle of the type of the command, or a raw path for an attribute outside it. A mixed read takes a handle of any of its
types, a type and its base types share their attributes, and a handle of a nested document belongs inside `.At(...)`. A command that cannot be followed back to its type, such as one in a generic helper, is
not looked at.

`DYNA216` (warning): `begins_with` or `contains` with a handle of a number or a boolean as its first operand, as in `$"contains({Order.Attrs.Lines}, {1})"`. It is valid grammar, DynamoDB finds no item, and the
result is empty. `Handle.BeginsWith(...)` on such a handle does not compile (`CS0619`), `Handle.Contains(...)` has no overload for it (`CS1929`), and this is the same mistake where a type cannot say so, because the text of an expression is not checked by the
compiler. A `[Flags]` enum is stored as a number too, and its handle has a generated converter that no overload can name, so `BeginsWith` compiles there and this rule reports it as well. Any other enum is stored by name, so it is
not reported. Compare the attribute with `Eq`, `Gt` or `Between`, or store text. `contains` on a set or a list wants a handle of that type, which `Handle.Contains(element)` takes, and a converter of your own is not looked at.

`DYNA220` (warning): a reserved word of DynamoDB as a bare name in the text of an expression, such as `orders.Scan().Filter($"Total > {100}")`, where `total` is reserved. DynamoDB rejects the request with a `ValidationException`, in any case of the word and at any depth of a path, so `info.year` fails too. The list has 573 words, among them `name`, `status`, `value`, `data`, `count`, `type`, `key`, `user`, `order` and `ttl`, and it is AWS's: [Reserved words in DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html). Write the name as a hole, `{Attr("total")}` or a handle such as `{Order.Attrs.Total}`, which DynaSharp sends as a `#name` placeholder; a `#name` of your own in the text is `DYNA211`. A reserved word as a function, `size(...)`, or as a keyword of the grammar is no name and is fine.
