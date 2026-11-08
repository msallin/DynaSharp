# Diagnostics

What DynaSharp's generator and analyzers report, and how to fix it. Every diagnostic's help link points at a section of this page, one per family of rules. The examples use [the shop](getting-started.md#the-shop) (`orders`, `customers`, `carts`, `shop`) where it fits.

## Entities

The source generator reports these on `[DynamoDbEntity]` and `[DynamoDbDocument]` types; `DYNA026`, `DYNA032` and `DYNA033` on an enum come from analyzers. A type with an error generates no code, so the `CS0311` on its table that the build shows beside it goes away with the fix. The exceptions are `DYNA020`, where the type is generated as if it opted out, and `DYNA104` and `DYNA105` on a key template, where the placeholder takes the default format of its type: the uses of the type compile, and the build fails on that error alone.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA001` | The type or a containing type is not `partial` | Make them all `partial`. The code fix adds it to each declaration that lacks it |
| `DYNA002` | An entity has no partition key | Add `[PartitionKey]`, or use `[DynamoDbDocument]` for a nested type |
| `DYNA003` | A property has a type that cannot be stored | Add a `[DynamoDbConverter]`, or `[DynamoDbIgnore]`, which the code fix adds to a property (not to a parameter of a positional record, which would have no property left to bind) |
| `DYNA004` | Two members want one attribute, also after `Naming` converted their names | Rename one with `[DynamoDbAttribute]` or `AttributeName` |
| `DYNA005` | No single constructor can be used | Mark one with `[DynamoDbConstructor]`. The code fix offers each constructor, and takes the attribute off the others |
| `DYNA006` | A constructor parameter has no property | Name it after a mapped property |
| `DYNA007` | The type cannot be mapped, for the reason the message gives: abstract, static, generic, a ref struct, an interface, or file-local, which generated code cannot merge with | Map another type, or change what the message names |
| `DYNA008` | A converter does not implement `IDynamoDbConverter<T>` for the property, or for the type `[assembly: DynamoDbDefaultConverter]` names | Fix its interface, or make it accessible |
| `DYNA009` | A key is declared twice | Keep one declaration |
| `DYNA010` | An attribute name DynamoDB would refuse | Pick another |
| `DYNA011` | `[Version]` on something that is no single integer property | Put it on one such property, without a converter |
| `DYNA012` | An attribute only an entity can have, on a document | Remove it, or make the type an entity |
| `DYNA013` | A type is an entity and a document | Keep one |
| `DYNA014` | A member clashes with generated code, such as `Keys`, or `TableName` in an entity that declares a table; a type outside it named so is `DYNA043` | Rename the member |
| `DYNA015` | The project is below C# 11 | Raise `LangVersion` |
| `DYNA016` | A type a member needs is less visible than the generated code | Make it as visible as the entity. The code fix does, on each type the property's type is made of that is less visible, where all of them are in source |
| `DYNA017` | A key whose type DynamoDB cannot hold | Use a string, number or binary, or a converter |
| `DYNA018` | An index that cannot exist, as the message says | Fix its name or keys |
| `DYNA019` | The type is outside a nullable context; a property outside one in a type that is inside is `DYNA041` | Enable nullable reference types. The code fix adds `#nullable enable` at the top of the file, or turns the `#nullable disable` above the type into `#nullable enable` |
| `DYNA020` | An entity has no discriminator, the value stored in each item that tells which entity the item is, so that several entities can share a table | Add the suggested `[Discriminator("EMAIL_CLAIM")]` (for `EmailClaim`: its name in upper snake case), or `[Discriminator(OptOut = true)]` for a table other tools write. `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]`, also named in the message, does that for every entity of a project. The code fix offers the three; the last sets `DiscriminatorOptOut = true` on the `[assembly: DynamoDbDefaults]` the project has, or adds one beside its other attributes of the assembly or in the entity's file |
| `DYNA021` | A plain key is `[DynamoDbIgnore]` | Remove one of the two |
| `DYNA022` | `[Discriminator]` has an empty value, both a value and `OptOut`, or neither | Set one |
| `DYNA023` | A `[Version]` property feeds a key | Take it out of the key: a write changes it |
| `DYNA024` | An enum without members | Add a member, make the property nullable, or add a converter |
| `DYNA025` | A type is named like a member generated into it, such as `Keys`, or `TableName` for an entity that declares a table | Rename the type |

`DYNA026` (warning): two entities of a compilation have the same discriminator in the same attribute. Typed and mixed reads tell types apart by that value, so on one table the items of one read as the other, or fail to.

- **Fix.** Give one another value. Entities that never share a table are fine, hence a warning: suppress it there.
- **Not reported.** A type in another assembly. The rule needs every type of the compilation, so an IDE shows it only with full solution analysis; a build always does.

`DYNA029` (error): an `[assembly: DynamoDbDefaultConverter]` that cannot work: a second default for a type, a nullable type (a default for the type covers its nullable form), or an open generic type (a default is for one closed type). Reported once, on the attribute, whether a type uses it or not.

- **Fix.** Keep one, or name the closed type without its `?`.
- **Not reported.** Nothing else fails with it: a type that would use it takes the first default or the built-in.

`DYNA030` (error): `[OmitEmpty]` on a property that is no string, list, set, dictionary or array, that feeds a key, or that a read cannot do without. An empty value counts as null only where null can be left out, which a key of the table and a key of an index that is not sparse cannot be. A `required` property that is neither nullable nor a set, or such a constructor parameter without a default, could never be read back: the read throws for the attribute the empty value left out.

- **Fix.** Remove the attribute, move the property out of the keys, make the index `Sparse = true`, make the property nullable, or give it a default.
- **Not reported.** A stored string that feeds nothing but keys of sparse indexes: there an empty value leaves out the attribute and the keys of its indexes, see [Sparse indexes](entities.md#sparse-indexes). `[OmitEmpty]` on a type skips the inputs of keys by itself, sparse or not, and they stay written; `[OmitEmpty(false)]` is not reported either.

`DYNA031` (warning): an entity property whose type is an entity too, or a list, set, dictionary or array of one, such as `public Customer Buyer { get; init; }` on an `Order`. It is stored as a map carrying the customer's keys, index keys and discriminator, which no read of an order takes and no index sees.

- **Fix.** Give the property a `[DynamoDbDocument]` type holding what the order needs. For an intended snapshot of an entity, suppress the warning.
- **Not reported.** A property that is `[DynamoDbIgnore]`, computed, or has its own converter, which is not stored as a map, and an entity inside a document.

`DYNA032` (warning): the getter of a `[ComputedFrom]` property reads a stored property of the same instance that the attribute does not name, such as `[ComputedFrom(nameof(First))] public string Full => First + Last;` beside `[GlobalIndexKey("ByName", PartitionKey = "NAME#{Full}")]`. An update knows what a key is computed from only by the attribute, so `people.Update(key).Set(Person.Attrs.Last, "Doe")` goes through, and the index keeps the key with the old last name. Reported once per such property, where the getter first reads it.

- **Fix.** Name every stored property the getter reads. A read that does not change the result is reported too: suppress the warning there.
- **Not reported.** The getter is read as written: what a method or property it calls reads, such as `Format()`, is not followed, nor is a property of another instance. The version, a `[KeyOnly]` property and a computed one cannot be named, so they are not reported.

`DYNA033` (warning): `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` on an enum whose generated converter does not read it by name, so the attribute changes nothing: a `[Flags]` enum is stored as a number, which has no name to miss, and an enum with its own `[DynamoDbConverter]` is read by that converter. Reported once, on the attribute, whether a type uses the enum or not. Also reported on a property of such an enum where an unknown name still throws: a non-nullable `required` property, a property set by a constructor parameter without a default, and a plain key of the table or an index, which a read takes as stored.

- **Fix.** Remove the attribute, or answer `CountsAsMissing` in the converter. On a property, make it nullable, or not required with a safe initial value, or give the parameter such a default. A key that must hold names the enum does not know is a string.
- **Not reported.** `UnknownEnumName.Throw`, the behaviour without the attribute. A nullable property, one that is not required, and a parameter with a default, which keep their value. A required input of a key template that holds it without loss, which a read restores from the key first. An element of a list, set or map, which throws for an unknown name, as the attribute says.

`DYNA034` (warning): a property of a base type whose setter the derived entity or document cannot reach, such as `public string Status { get; private set; }` on the base. The base stores it, but the generated code of the derived type cannot assign it, so the derived type leaves it out of its items, and what the base type keeps is lost for the derived one.

- **Fix.** Make the setter `protected`, or `internal` within one assembly, or set the property through a constructor parameter. Where no type is to store it, mark it `[DynamoDbIgnore]`.
- **Not reported.** A property without a setter, which is computed, and one with `[DynamoDbAttribute]` or a key attribute, which is written and never read. A base type in a compiled assembly, which shows the compiler no setter that is not public, so the property looks computed.

`DYNA035` (warning): a nullable property that starts with a value, such as `public string? Note { get; set; } = "n/a";`, a `required` one too, or a constructor parameter of one with a default, such as `string? Note = "n/a"` in a positional record. A write leaves null out of the item, and a read of an item without the attribute keeps the initial value, so `Note = null` reads back as `"n/a"`.

- **Fix.** Remove the initializer, or make the default of the parameter null, so that a missing attribute reads as null. Where the value is what a missing attribute should mean, make the property non-nullable.
- **Not reported.** `= null`, `= default` and `= null!`, a property that is not nullable, not stored or computed, and a value that a constructor body assigns. A base type in a compiled assembly, which shows no initializer.

`DYNA036` (error): `[TimeToLive]` on a property that DynamoDB cannot read as an expiry, or on a second property of the type. DynamoDB deletes by a number of seconds since 1970 and ignores an item that holds anything else, so `[TimeToLive] public DateTimeOffset ExpiresAt { get; init; }`, which is stored as an ISO 8601 string, would never expire. A property that is not stored, such as one with `[DynamoDbIgnore]` or a getter alone, holds no expiry, and a table has time to live on one attribute.

- **Fix.** Add `[DynamoDbConverter(typeof(EpochSecondsConverter))]` to a `DateTimeOffset`, or store the seconds in an integer. Keep one `[TimeToLive]` per type.
- **Not reported.** A custom converter that implements `IAddableConverter`, which says that it writes a number, whatever it holds. Two entities of one table that name different attributes, which `CreateTable().Including<T>()` finds at run time and throws `ArgumentException` for. On a document it is `DYNA012`.

`DYNA037` (info): the generator writes no [`IncludingAll()`](tables.md#every-entity-of-an-assembly) for the entities of this assembly, because they declare the keys of more than one table, such as `pk` and `sk` on `Order` and `id` on `Session`, or an entity is nested in a type that hides it, such as a `private` record inside a test class; the message says which. Reported once per compilation, on no file.

- **Fix.** Nothing has to change: name the entities of each table with `Including<T>()`. For `IncludingAll()`, keep the entities of one table in an assembly of their own, or make a hidden one `internal`.
- **Not reported.** An assembly without entities. A suppression goes into `.editorconfig` (`dotnet_diagnostic.DYNA037.severity = none`), since there is no line for a `#pragma`.

`DYNA038` (error): a type of the assembly has the name the generated class needs, `DynaSharp.DynamoDbEntities`. C# allows no two types of one name in an assembly, so the class is not generated.

- **Fix.** Rename the type.
- **Not reported.** An assembly for which no `IncludingAll()` would be generated, see `DYNA037`.

`DYNA039` (error): a table name that DynamoDB would reject, in `[DynamoDbEntity(Table = "...")]` or `[assembly: DynamoDbDefaults(Table = "...")]`: an empty name, one shorter than 3 or longer than 255 characters, or one with a character other than `a-z`, `A-Z`, `0-9`, `_`, `-` and `.`, such as `"app data"`. The entity generates no code; a bad default is reported once, on the attribute of the assembly.

- **Fix.** Write the name as the table has it. A prefix for an environment, such as `dev-`, belongs in [`TableNaming`](tables.md#a-table-per-environment), not in the declaration.
- **Not reported.** `Table = null`, which declares nothing, and a name passed to `client.Table<T>(name)`, which can be an ARN and which DynamoDB checks.

`DYNA040` (warning): two entities of the compilation are in one table, by their own `Table` or by the default of the assembly, and name their keys differently, such as `pk` and `sk` on `Cart` and `id` on `Session` under `[assembly: DynamoDbDefaults(Table = "appdata")]`. A table has one key schema, so the commands of one of them fail on DynamoDB with a `ValidationException`. Reported on the `[DynamoDbEntity]` of each entity whose keys differ from those most entities of the table have (on a tie, those of the entity first by name), naming one that has them.

- **Fix.** Name their key attributes the same, with `AttributeName`, or put one of them in a table of its own: `[DynamoDbEntity(Table = "sessions")]`.
- **Not reported.** An entity without a declared table, an entity of another assembly, and the types of a key, which the names alone do not show.

`DYNA041` (error): a mapped property whose type was written where nullable annotations are disabled, though the type itself is declared in an enabled context, which `DYNA019` checks: a `#nullable disable` region inside the type, a part of a partial type in a file with nullable disabled, or a base type compiled with nullable disabled. There `string` says nothing about null, so the generated code takes it as never null and throws `ArgumentNullException` on a null. Reported on the property, or on the type for a base type from a compiled assembly.

- **Fix.** Enable nullable reference types where the property is declared, and mark with `?` what can be null. For a base type from a library compiled without them, map a type of your own.
- **Not reported.** A value type such as `int`, which needs no annotation, a property that is not stored, such as one with `[DynamoDbIgnore]`, and a type that `DYNA019` already reports.

`DYNA042` (error): a parameter of the constructor a read uses that is `ref`, `out` or `ref readonly`, such as `public Counter(ref int count)`. A read passes the value it read, which only a parameter by value or `in` takes.

- **Fix.** Take the parameter by value or as `in`, or mark another constructor with `[DynamoDbConstructor]`.
- **Not reported.** An `in` parameter, and a constructor the read does not use.

`DYNA043` (error): a name inside a mapped type that names a type, a namespace, a field or a property outside it by the name of a member generated into the type: `Attrs` in any mapped type, `Keys` in an entity, `Indexes` in an entity with an index, and `TableName` in an entity that declares a table. With `public enum Keys { None, Enter }`, `public Keys Key { get; set; }` inside an entity would name the generated `Shortcut.Keys` instead of the enum, and the build would fail with `CS0722` and other errors in your file and in the generated one. With `using static Names;`, a `TableName` inside the entity would give the table of the entity instead of `Names.TableName`. Reported on each such name; the type generates no code.

- **Fix.** Write the name in full, `public Shop.Keys Key { get; set; }`, `global::Shop.Keys` or `Names.TableName`, or give a type another name with a using alias, `using KeyCode = Shop.Keys;`. The code fix writes the name in full, as the message gives it.
- **Not reported.** A name after a dot, such as `Shop.Keys` or `dictionary.Keys`, a local or a parameter, which C# finds first, and a name of a member the type does not get: `Keys` in a document, `Indexes` in an entity without an index, `TableName` in an entity without a table. `[DynamoDbEntity(Table = TableName)]`, whose value is the constant's anyway. A member of the type with such a name is `DYNA014`.

## Key templates

The source generator reports these on key templates. The grammar is under [Key grammar](https://github.com/msallin/DynaSharp/blob/main/spec/requirements.md#key-grammar) in the repository's requirements, each type's formats are in the row of `DYNA104`, and each message says which part of a template it is about. Analyzers report what code shows of a template: `DYNA120` and `DYNA121` through the getter of a computed input, and `DYNA122` on a call of a key builder.

| Rule | What is wrong | What to do |
|---|---|---|
| `DYNA100` | A template does not parse | Fix it at the position the message gives |
| `DYNA101` | A literal holds something other than printable ASCII | Keep literals to that |
| `DYNA102` | A placeholder names no property | Check the spelling |
| `DYNA103` | A property of a type a key cannot hold | Use a type of the format table, or a plain key with a converter |
| `DYNA104` | A format that does not fit the type | Use one the message lists: `plain`, `d<w>` or `n<i>` for integers, `plain` or `n<i>.<f>` for `decimal`, `yyyy` to `ticks` for timestamps, `d<w>` or none for enums, `raw` or none for `string`, and none for `Guid`, `DateOnly` and custom types |
| `DYNA105` | A format width out of range | Use a smaller one |
| `DYNA106` | Two adjacent placeholders with nothing to tell them apart | Put a literal between them, or give the first a fixed width |
| `DYNA107` | A nullable property in a key that is not sparse | Make the index `Sparse`, or the property required |
| `DYNA108` (warning) | A number without a fixed width in a sort key, so `10` sorts before `9` | Use `d<w>` or `n<i>`, or `plain` if the order does not matter |
| `DYNA109` | `[KeyOnly]` that cannot work, as the message says | Hold the value in a key without loss, or store it |
| `DYNA110` (warning) | A sparse index whose inputs can never be absent, so it holds every item | Remove `Sparse`, or make an input nullable or a string with an `[OmitEmpty]` of its own |
| `DYNA111` | A property twice in one template | Use it once |
| `DYNA112` | A template on a property, or a plain key on a type | Move it |
| `DYNA113` | An enum with `[Flags]` or aliases, whose names are not one-to-one | Use `d<w>` |
| `DYNA114` | A computed property in an index template, which no update can follow | Add `[ComputedFrom]` naming the stored properties it reads, or use a stored property |
| `DYNA115` (info) | A string before a delimiter in a sort key, so values with a smaller character sort out of order | Use a delimiter below the values, or accept the rare caveat |

`DYNA116` (warning): a `DateTime` or `DateTimeOffset` in `s`, `ms` or `us` is the last placeholder of the table key. `[SortKey("{At:ms}")]` keeps milliseconds, so two puts 0.5 ms apart write one key, and the second silently replaces the first. The end of the whole key counts: the sort key, or the partition key of a table without one.

- **Fix.** Add a tie-breaking placeholder after the timestamp, such as `{At:ms}#{Id}`, or drop the format: a bare `{At}` is `ticks`, all seven digits.
- **Not reported.** A partition key ending in a day beside a sort key. An index, which is not unique. A minute or coarser format, such as `{Window:yyyy-MM-ddTHH:mm}` or `{Month:yyyy-MM}`, which means one item per period, as for a counter or summary. A plain `[SortKey]` on the property, which goes through its converter and keeps every digit.

`DYNA117` (warning): a key property starts empty and nothing has to set it, such as `public string EntityPartition { get; set; } = string.Empty;` set later with a `with`. Forgetting it compiles, and the write throws `KeyFormatException` for a template or is rejected by DynamoDB for a plain key. Every key of the entity counts, since a put writes the index keys too.

- **Fix.** Make it `required`, or set it in a constructor. The code fix makes it `required` and removes the initializer, where its setter is as visible as the type.
- **Not reported.** Only a default that cannot be a key counts: `""`, `string.Empty`, `null!`, `default!`, and for a binary key `[]` and `Array.Empty<byte>()`. A default such as `"main"` or `Guid.NewGuid().ToString()` is a key, and a property that is `required`, computed, nullable, without initializer, or mentioned by a constructor is not reported.

`DYNA118` (error): a `raw` placeholder that does not end the template, such as `{Key:raw}#{Id}` or `{Key:raw}#x`. A raw value keeps its delimiters, so `Parse` and a read take everything to the end of the key, and anything behind it could not be read back.

- **Fix.** Move the placeholder to the end, or remove `raw`, which makes a value with a delimiter throw `KeyFormatException`.

`DYNA119` (error): a `[ComputedFrom]` that cannot work, as the message says. It belongs on a computed property a key template uses, and names at least one property of the same type, each once.

- **Fix.** Name properties with their own attribute: not computed, `[DynamoDbIgnore]` or `[KeyOnly]`, and not the `[Version]`, which an update moves without a handle, so nothing would see the change.
- **Not reported.** A `[ComputedFrom]` on a document, which is `DYNA012`.

`DYNA120` (warning): a property `raw` in one template and strict in another, such as `[PartitionKey("A#{Key:raw}")]` beside `[GlobalIndexKey("ByKey", PartitionKey = "B#{Key}")]`. A format belongs to its template, so this compiles, and a `Put` of `a#b` passes the first key and throws `KeyFormatException` at the second. Reported on each strict template.

- **Fix.** Use `raw` in both, at the end of each template, or in neither. Where text follows the placeholder, only neither works; anything else is `DYNA118`. A property that really takes fewer values in one key can keep this with `#pragma warning disable DYNA120`.
- **Through a getter.** It follows `[ComputedFrom]` too: a strict `B#{Upper}` over `[ComputedFrom(nameof(Key))] public string Upper => Key.ToUpperInvariant();` beside `A#{Key:raw}` is reported when the getter passes the raw input on with every character: `=> Key`, `=> Key.ToUpperInvariant()`, `=> Key.ToLowerInvariant()`, `=> First ?? Second`, or a body whose every `return` is one of these (a `throw` passes nothing). The warning stands on the computed property, once per strict template, and names the property it goes through. Any other getter is `DYNA121`.
- **Not reported.** Only a template with a delimiter refuses anything, so a strict `{Key}` alone and a plain key are not reported.

`DYNA121` (info): the same through a getter that does anything else with the raw input, such as `Key.Replace('#', '-')`, a hash, or `Key + "x"`. A `Replace` or a hash removes the delimiter, and the strict template takes every value; a concatenation keeps it, and the strict template throws `KeyFormatException` for a value the raw one took.

- **Fix.** Check the getter; it may be right. It is also reported for a getter that ends up not using the raw input.

`DYNA122` (warning): a constant for a placeholder of a generated key builder that no key can hold, because it is empty or holds a delimiter of the template, such as `Order.Keys.Pk.Of("a#1")` beside `[PartitionKey("CUSTOMER#{CustomerId}")]`, or `Product.Keys.Pk.Of("")`. Every run of the call throws `KeyFormatException`. Any constant counts, including a `const`, a concatenation of constants and `string.Empty`, in `Keys.Of` and in the builders of every table and index template, such as `Format`, `Of`, `Eq`, `BeginsWith`, `PrefixPartial` and `Between`.

- **Fix.** Change the value, or make the placeholder `raw` where it ends the template: a raw placeholder takes a delimiter and is reported only for an empty value.
- **Not reported.** `TryOf`, which answers null, `Parse`, which takes a whole key, a plain key, which has no template, and a non-constant value. Only an empty value and a delimiter are checked: a control character or U+10FFFF in a constant still throws at run time without this warning.

`DYNA123` (info) reports a property that a `raw` placeholder of the table key holds and that has no `[KeyOnly]`, such as `Key` under `[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]`. A `Put` stores an input of a template as an attribute of its own as well, and an `Update` does not write it, so a table that both write ends up with the attribute on some items and not on others, and a later `Put` adds it to an item an `Update` created. A raw input is mostly an id that holds the delimiter and that only the key needs, which is what `[KeyOnly]` says: the item holds the key alone, and a read restores the property from it.

- **Fix.** Mark the property `[KeyOnly]`, as in [Keys that hold your delimiter](adopting.md#keys-that-hold-your-delimiter). It then has no handle, so a condition or a filter cannot name it.
- **Not reported.** A raw placeholder of an index, which the table key does not hold, and a computed input, which `[KeyOnly]` does not take. Information only, because an adopted table whose items hold the attribute needs it: there `[KeyOnly]` would make a `Put` drop it, so suppress the message instead.

## Commands

A command such as `PutCommand<T>` is a `ref struct` over pooled state, so build and send it in one expression.

`DYNA201` (warning) stands beside the compiler error when a command outlives its expression, and says what to write instead.

| What the code does | The compiler says | Write instead |
|---|---|---|
| Stores a command in a field or property | CS8345 (no `DYNA201`: the compiler stops before analyzers run) | A method that builds it: `UpdateCommand<Customer> IncrementOrders(string id) => customers.Update(Customer.Keys.Of(id)).Add(Customer.Attrs.OrderCount, 1);` |
| Keeps one across an `await` or a `yield return` | CS4007 | Compute every value first, then build and send in one synchronous stretch. `if (x) { command = command.Set(...); }` between the steps is fine: each call returns a new handle to the same local. |
| Takes one as a parameter of an `async` method, or as an `async` local before C# 13 | CS4012, CS9202 | A synchronous helper that takes and returns the command, and an `await` in the caller. |
| Captures one in a lambda or a local function | CS8175, CS9108 | Build inside the lambda: `pipeline.ExecuteAsync(ct => customers.Update(key).Add(attr, 1).SendAsync(ct))`. A retry from outside must rebuild anyway, since the buffers are gone after the send. |

Sending a command twice is no compile error. It throws `InvalidOperationException`, as does a second use of a handle after a fluent call: build the command again. `DYNA204` reports what the build can see of that. A synchronous helper that returns a command for the caller to finish and send is supported; what the rules below then cannot see is under [Helpers that return a command](commands.md#helpers-that-return-a-command).

`DYNA202` (warning) reports `await shop.Query("a")` and `shop.Query("a").SendAsync()`. A read can end in several ways and none is the default. Without it the compiler reports only a type parameter constraint (CS0315).

- **Fix.** Name the call that sends it. Every read has `ItemsAsync` (`ValuesAsync` after `Select`). A query adds `ItemAsync` (`ValueAsync` after `Select`), and most reads add `PageAsync`, `PagesAsync` and `CountAsync`, but not all: a `Select` and a mixed read have no `CountAsync`, a fan-out has no `PagesAsync`, and a parallel scan has `ItemsAsync` alone. The code fix offers each terminal of the read that fits where it stands: under `await` one that gives a value, in `await foreach` one that streams.
- **Not reported.** An extension method of your own that makes a read awaitable.

`DYNA203` (error) reports a statement whose value is a command or transaction step: `orders.Put(order);`, `query.Descending();`, or a helper of your own that returns one. Nothing sends it, and a call on a handle uses that handle up, so a chain with such a statement in the middle throws at its next use. The body of a lambda that returns nothing counts as such a statement: without this rule, `Task.Run(() => orders.Put(order))` is a write that never happens.

- **Fix.** Send it, or keep what the call returns: `query = query.Descending();`. `_ = command;` says dropping it is intended. The code fix offers `await command.SendAsync()` in an `async` member, `query = query.Descending();` for a command in a local, and `_ =`.
- **Not reported.** An assignment, a `return` and an `await`, which use the value.

`DYNA204` (warning) reports a call on a command in a variable that an earlier call used up on some path: `var a = q.Limit(1); var b = q.Limit(2);`, or `_ = command.ToRequestJson(); await command.SendAsync();`. It follows branches and loops, so `if (debug) log(command.ToRequestJson());` before the send is reported: that path throws when debug is on. The analysis does not know that two conditions go together, so two separate `if`s on one flag count as a path through both.

- **Fix.** Every call returns the next handle, so assign it: `q = q.Limit(1);`. For a log, build a separate command. Where the earlier call is a statement of its own, `q.Limit(1);` or `_ = q.Limit(1);`, in a block the reuse is in, the code fix assigns its result back to the variable.
- **Not reported.** A call on each side of an `if`, a reassignment in a loop and an early `return`. A handle copied to a second variable is not followed.

`DYNA205` (warning) reports `Set`, `SetIfNotNull`, `Add`, `Remove`, `SetIfNotExists` or `Delete` by the handle of a property the table key is made of, on a command of that entity: `orders.Update(key).Set(Order.Attrs.Number, 2)`. DynamoDB cannot change a key, so the call throws when the command ends, or DynamoDB rejects it for a plain key.

- **Fix.** Delete the item and put a new one. To store the table key inputs on an upsert, call `StoreKeyInputs()`.
- **Not reported.** A property that feeds only an index key stays writable. A raw path is not checked.

`DYNA219` (warning) reports the same calls by the handle of a property from which a computed input of an index key is derived, on a command of that entity: `visits.Update(key).Set(Visit.Attrs.Arrival, arrival)` under the `ByDay` index of [Computed index keys](entities.md#computed-index-keys), also for a path inside it through `.At(...)`. An update has no instance to run the getter on, so the call throws when the command ends. So does a write by the handle of a computed input with its own attribute, `badges.Update(key).Set(Badge.Attrs.Label, label)`, where the key would follow the update's value and not the getter.

- **Fix.** Put the changed entity instead.
- **Not reported.** A property from which a computed input of the table key is derived, which is `DYNA205`, one step removed. A raw path, and a command that cannot be traced to its type.

`DYNA206` (warning) reports `IfNotExists()` on a `Put` whose item shows a version above 0 in the chain, also in a transaction: `carts.Put(new Cart { CustomerId = id, Revision = 1 }).IfNotExists()`, or `carts.Put(cart with { Revision = 1 }).IfNotExists()`. An item that does not exist has no version, so the two never both hold, and the command throws `InvalidOperationException` when it ends.

- **Fix.** Leave a first write's version at 0, which already means no item has the key, so `cart with { Revision = 0 }` and `IfNotExists` agree. To write without versions, `IgnoreVersion()` drops the check, and `IfNotExists` is then yours to use. The code fix offers both: the version set to 0, or `IgnoreVersion()` after `IfNotExists()`.
- **Not reported.** Only a constant in an object initializer or a `with` in the chain counts. A version in a variable or from a method is unknown here, and often 0, so the run-time check answers for it.

`DYNA207` (warning) reports a constant name the entity does not declare, in the `Index` that takes the keys from the entity: `orders.Index("ByStatuz")`, or `shop.Index<Order>("ByStatuz")`. It would throw `ArgumentException` at run time.

- **Fix.** Take the name from `T.Indexes`, such as `Order.Indexes.ByStatus`. The code fix offers each index the entity declares, the closest spelling first.
- **Not reported.** A non-constant name, and `Index(name, partitionKey, sortKey)`, which names its keys itself.

`DYNA208` (warning) reports a `Project` on a read of an entity that leaves out what the entity cannot be built without: `orders.Query(pk).Project("Total").ItemsAsync()`, where `Status` is `required`. The item comes back without it, and the read throws `DynamoDbMappingException` for every item. That holds for non-nullable `required` members and constructor parameters without a default. All `Project` calls of a chain count, also on `BatchGet`, a fan-out and a mixed read, where every type counts.

- **Fix.** Include the member, or use `Select(handle)` on a `Get`, `Query` or `Scan`, which reads one attribute without the entity. The code fix adds every member left out to the `Project`, as strings or as handles, the way the call names the others.
- **Not reported.** A `required string?`, which reads as null, and a required set, which reads as empty, as a write of them leaves. A key input a key of the read restores: a read adds the table keys, and a read of an index the index keys too. So an input only an index key holds is reported on a read of the table, and one a key truncates, such as a timestamp in `{At:yyyy-MM-dd}`, on any read. A nested path such as `"ShipTo.City"`, which keeps its top-level member, whatever the nested document needs. A non-constant value, a chain kept in a variable, returned, or passed through a method of your own, a read of `Item`, and `ToRequestJson()` or `CountAsync()`, which read no item.

`DYNA209` (warning) reports `Select(handle)` of an attribute that an item can lack where a read of the entity goes on. `Select` reads the attribute alone and throws `DynamoDbMappingException`. Three kinds are reported, each with its fix:

- **A table key input**, such as `orders.Get(key).Select(Order.Attrs.CustomerId)` under `[PartitionKey("CUSTOMER#{CustomerId}")]`. An `Update` stores no table key input, so an item it created has the key without the attribute, which a read of the entity restores from the key; on items a `Put` wrote, `Select` reads the value. Read the entity, call `StoreKeyInputs()` on every update of the table, or suppress this where every item comes from a `Put`.
- **A non-nullable property with `[OmitEmpty]`**, such as `orders.Get(key).Select(Order.Attrs.Lines)` with `[OmitEmpty]` on `Lines`. An empty value is not stored, and a read of the entity keeps the initial value. Read the entity, or make the property nullable, which reads as null.
- **A property whose converter implements `CountsAsMissing`**, such as one of an enum with `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]`. The attribute is there but counts as missing, and a read of the entity keeps the initial value. Read the entity, make the property nullable, or have the converter answer `TryReadMissing`.

Not reported:

- Any of the three whose converter of your own answers `TryReadMissing`, which gives `Select` a value.
- Of the key inputs: a `[KeyOnly]` property, which has no handle, so it does not compile; a plain key, which is its own attribute; an index key input, set like any attribute; and a nullable input, since a missing attribute reads as null. A timestamp counts only where the key holds all of it, as a bare `{PlacedAt}` (`ticks`) does: under `{PlacedAt:ms}` a read of the entity cannot restore it either and throws as `Select` does.
- A set with `[OmitEmpty]`, since a missing attribute reads as an empty set.
- Under `CountsAsMissing`, a `required` member, since a read of the entity throws as well, and a plain key, which a read of the entity does not count as missing.
- A `[Version]`, such as `carts.Get(key).Select(Cart.Attrs.Revision)`. Every write of the library stores it, also one with `IgnoreVersion()`. On an item without one, written by other code, `Select` throws a `DynamoDbMappingException` that says so and points at `[Version(Missing = VersionMissing.Allow)]`, whose handle reads such an item as 0.

`DYNA217` (warning) reports a `RetryOnConflictAsync` inside the delegate of another: `carts.RetryOnConflictAsync(ct => new(carts.RetryOnConflictAsync(Run, ct)))`. The runs multiply (5 by 5 is 25), and an inner retry that used up its runs throws a conflict the outer one counts as its own, so one lost race restarts the whole run. The inner retry can be in a lambda at any depth inside the delegate, or in a method or local function of the same compilation the delegate calls, one call deep, where it is reported at the retry in that method, also when the method has other callers.

- **Fix.** Make a run one read-modify-write, and put a follow-up write after the outer call.
- **Not reported.** The delegate is followed when it is a lambda, an anonymous method or a local function given by name; not a delegate in a variable, a method given by name that is no local function, or a method of another assembly. A retry with a constant `maxAttempts: 1`, inside or outside, is no retry and multiplies nothing.

`DYNA218` (warning) reports a version in the delegate of a `RetryOnConflictAsync` that stays the same from run to run: `var cart = await carts.Get(key).ItemAsync(); await carts.RetryOnConflictAsync(async ct => await carts.Put(cart! with { Lines = lines }).SendAsync(ct));`. Every run sends that version and fails the same way. A `TransactionConflictException` can pass on a later run with the same version, but a stale version fails every run.

- **Fix.** Read the item inside the delegate. Where the value read before serves the first run, take the delegate with the number of the run and read again after it: `async (run, ct) => { var current = run == 1 ? cart : await carts.Get(key).Consistent().ItemAsync(ct); ... }`, which is not reported, because `current` is a local of the run.
- **What it follows.** `IfVersion(x)`, and `Put(item)` and `Delete(entity)` of an entity with a `[Version]`, also a `Put` in a transaction, unless `IgnoreVersion()` is on the command or `IfVersion` replaces the version. It follows `cart.Revision`, `cart!`, `cart with { ... }` and a conversion, to a local or parameter the delegate captures and does not assign. A version in `IfVersion` or a `Delete` counts in any parameter or variable, since only a read gives one, unless the variable holds a constant or an object made in the same member. A `Put` counts only when the member shows the item coming from a library call, through one more local at most, in `var`, an assignment, `is { } cart` or `await foreach`, since a new item built outside the delegate is correct at version 0.
- **Not reported.** A `Put` of a parameter or of what your own method returned, whatever it holds. A `with` that sets the version to a value the delegate read. A variable a run can update, by writing a member or passing it to your own method, or that a lambda or local function assigns. Fields and properties, `IfVersion(0)`, and a computed version such as `IfVersion(x + 1)`. A retry with a constant `maxAttempts: 1`, and one whose constant `retryOn` leaves out `ConflictKind.ConditionalCheckFailed`, such as `ConflictKind.TransactionConflict`, where a stale version fails the first run and surfaces at once, as it should. A `retryOn` in a variable counts as one that repeats a failed condition.

`DYNA221` (warning) reports a `Filter` on a `Query` over a property that a key of what the query reads holds without loss, where the key condition decides it: `orders.Query(Order.Keys.Pk.Of(id)).Filter(Order.Attrs.CustomerId.Eq(id))` under `[PartitionKey("CUSTOMER#{CustomerId}")]`. The query pins every partition key placeholder, so such a filter keeps the whole partition or nothing; the first sort key placeholder is what the sort key conditions test. On a table key the filter is also wrong, as in `DYNA209`: an item an `Update` created has the key without the attribute, and the filter drops it. A plain key and the key attribute itself, `Attr("pk")`, are worse: DynamoDB rejects a filter on a key of the table or index read.

- **Fix.** Put the condition into the key condition.
- **Not reported.** It checks handles and constant raw paths in the filter, and knows what the query reads only from its chain: an entity's table, or `Index(name)` or `Index(name, partitionKey, sortKey)` with constant names. An index in a field or variable, such as `byStatus` of [Tables](tables.md#opening-a-table), a `Scan`, a mixed read and a bare name in the filter text are not checked. Neither is a table key input in a query of an index, nor a later sort key placeholder, such as the recording time in `VALID#{ValidFrom}#TX#{RecordedAt:ticks}`, which a key condition alone cannot test.

`DYNA222` (info) reports the same where the key holds the property with loss, as `{IssuedOn:yyyy-MM}` holds a day: the filter is correct, and a key condition can first narrow the read to the month.

`DYNA223` (warning) reports two actions of one update on the same path, or on paths of which one contains the other: `Set(Order.Attrs.Total, 1m).Add(Order.Attrs.Total, 2m)`, or `Remove(Customer.Attrs.ShipTo).Set(Customer.Attrs.ShipTo.At(Address.Attrs.City), city)`. DynamoDB rejects overlapping paths. The update sends every action as it comes, so a `SetIfNotNull` counts too. Paths are handles (also through `At`), constant strings and `Attr(...)` of one; actions are `Set`, `SetIfNotNull`, `Add`, `Remove`, `SetIfNotExists` and `Delete` up to the end of the chain or the next transaction step.

- **Fix.** Keep one action per path.
- **Not reported.** Paths that only share a parent, `ShipTo.City` and `ShipTo.Street`, or two list items. An action that adds nothing, which only one of a constant `null`, or of an empty string on an `[OmitEmpty]` property, does. The text of `Set($"...")`, what the update writes by itself, such as a version, and a chain through a variable.

`DYNA224` (warning) reports a key built for one attribute in a query that reads another: `orders.Index(Order.Indexes.ByStatus).Query(Order.Keys.Pk.Of(id))`. A partition key knows its attribute, so the query throws `ArgumentException`. A sort key condition, `Order.Keys.Sk.Between(...)`, does not, and DynamoDB compares the index sort key with text made for another key, which finds the wrong items; on an index without a sort key the query throws.

- **Fix.** Use the index's builder, `Order.Keys.ByStatusPk`. The code fix writes it where its method takes the same parameters, so the arguments still fit.
- **Not reported.** A key whose attribute is the one read, as on a local index, which has the table's partition key, and a sort key condition where both keys have the same template (`{At}` and `{At:ticks}` count as one). It determines what the query reads as `DYNA221` does, so an index in a field or variable is not checked, and checks only a key from `Of`, also as a method group in `Select`: not a key from `TryOf` or in a variable, nor `Keys.Pk.Format(...)`, which is text.

`DYNA225` (warning) reports `Including<T>()` of an entity whose key names differ from the table's: `orders.CreateTable().Including<Invoice>()`, where `Order` has `(pk, sk)` and the `Invoice` of [Names of your own](adopting.md#names-of-your-own) `(customer_id, invoice_no)`. `Including` throws `ArgumentException`, since entities on one table share its key attributes. The table has its entity's key; a table of `Item` the key of the first entity included.

- **Fix.** Give the entities the table's key names, or separate tables.
- **Not reported.** A key attribute of another type, and an index two entities declare differently, which throw there too.

`DYNA226` (warning) reports an update that sets by handle some inputs of an index key but not the others: `items.Update(key).Set(WorkItem.Attrs.Status, closed)` under `{Status}#{CreatedAt}`, as under [Status-keyed index](patterns-indexes.md#status-keyed-index). It also reports one that sets a key of a sparse index without the inputs of that index's other key, such as a soft-delete restore that sets the time but not the owner. An update does not read the item, and DynamoDB cannot build a key of several values or with literals from what the item holds; the other key of a sparse index, such as `OWNER#{Owner}`, may be missing, and the item silently stays out of the index. So the update throws `InvalidOperationException` when sent, naming the `Set` to add.

- **Fix.** Also set the unchanged input to the value the item holds, or put the changed entity. A table key input needs nothing, since the update has the key, so a design that keeps what does not change in the table key, or a [version-7 id](patterns-keys.md#newest-first-with-version-7-guid-sort-keys) holding the time, needs no read before the write. An input only a key holds, or that a getter computes, has no handle; only a `Put` or a raw path writes such a key.
- **Not reported.** It checks a chain from `Update` to `SendAsync`, `ToRequestJson`, `Return`, an `await` or the next transaction step, and a table key input counts as known. Not checked: an update in a variable or returned by a helper, your own method in the chain, a handle in a variable or of another type, and a raw path. Nor an index with an input the update removes, changes by `Add`, `SetIfNotExists` or `Delete`, or sets to a possibly null value, since those take the item out of a sparse index or throw something else, nor a write `DYNA205` or `DYNA219` reports. Nor the other key of a sparse index that is an attribute's stored text: one bare placeholder over a string (also `:raw`), `Guid`, `DateOnly`, `ticks` timestamp or enum by name, each with its built-in converter, such as `{Owner}`. The update copies it, `SET gsi2pk = Owner`, and an item without `Owner` fails the update with `ValidationException`. A number, a padded or truncated format, a literal, a second placeholder and your own converter are no such text and are reported.

`DYNA227` (info) reports `Value` of a `SelectResult` in a member that never checks `Found`: `(await orders.Get(key).Select(Order.Attrs.Total)).Value`. `Value` throws `InvalidOperationException` when the read found no item, which DynamoDB answers without an error. Info, since code that knows the item exists, as right after writing it, is correct and looks the same.

- **Fix.** Test `Found` first, `result.Found ? result.Value : 0m`, or write `result.GetValueOrDefault(0m)`, or `GetValueOrDefault()` for the type's default. The code fix offers `GetValueOrDefault()`, and `result.Found ? result.Value : default` where the result is a variable.
- **Not reported.** A member that reads `Found` of any `SelectResult` anywhere: in a condition, a guard clause, a property pattern such as `{ Found: true, Value: var total }` or a lambda. The rule goes by member, not by flow, so a check of another result or on another path silences it too. `nameof(result.Value)`.

`DYNA230` to `DYNA234` stand beside a compiler error on a call that does not compile. Where the compiler names a constraint, such as `IKey<Order>` or a nullable value type, these say what is wrong and what to write. They are errors, but only on code that already fails to compile.

`DYNA230` (error) reports a key the table does not take, where the compiler says `CS0315`: another entity's key, `orders.Get(Customer.Keys.Of(id))`, a partition key where the call needs the whole key, `orders.Get(Order.Keys.Pk.Of(id))`, a value that is no key, `orders.Get(id)` with a `Guid`, or an entity of another type, `orders.BatchWrite().DeleteAll(customers)`. It covers every verb that takes a key: `Get`, `Update`, `Delete` and `Query` of a table and of an index, the steps of `TransactWrite` and `TransactGet`, and `BatchGet` and `BatchWrite`.

It also reports a key given as the values it is stored as on a table or an index of an entity: `customers.Get("1")`, `orders.Query("alice")`, `byStatus.Query("open")`, `orders.Query(["s0", "s1"])`, `Update(orders, pk, sk)` in a transaction. Such a key skips the key template, so `customers.Get("1")` would ask for the item `1` where the entity stores `CUSTOMER#1`, and find nothing. Only a table without a model takes it, so on a table of an entity the call does not compile, and the compiler says `CS0311`, `CS1501`, `CS1503` or `CS1929`, after the overload it measured the call against. The message names the builder with the values: `Use Customer.Keys.Of("1")`, `Use Order.Keys.Pk.Of("alice")`, `Build each partition with Order.Keys.Pk.Of(customerId)`, and `Order.Keys.Pk.Of("a")` for `Order.Keys.Pk.Format("a")`.

- **Fix.** Build the key with the builders of the table's entity, `Order.Keys.Of(...)`, `Order.Keys.Pk.Of(...)` or the builder of the index, such as `Order.Keys.ByStatusPk.Of(...)`; a sequence of them is a query of several partitions. An index keyed by the discriminator has `QueryByDiscriminator()`, and an entity written by hand passes a key of its own that implements `IKey<T>`, or `new PartitionKey<T>(value)` for a query. A table without an entity, `client.Table(name, pk, sk)`, takes key values, and `shop.Of<Customer>()` opens it as a table that takes `Customer.Keys`.
- **Not reported.** A table of your own type parameter, and a value that is no key on a table without an entity, which takes key values as `AttributeValue`.

`DYNA231` (error) reports a type argument that is no entity where a table, a multi-type read or `Is<T>()` takes one, with `CS0311` or `CS0315` beside it: `client.Table<Customer>("Shop")` for a `Customer` without `[DynamoDbEntity]`, `orders.CreateTable().Including<Address>()` of a document, and `shop.Query(pk).Of<Order, Legacy>()` of an entity with `[Discriminator(OptOut = true)]`. A type that names it, `Table<Customer>` in a field, is reported the same way.

- **Fix.** What the message says: `[DynamoDbEntity]` with a `[PartitionKey]` on a partial type, an entity in place of a document, or a `[Discriminator]` in place of the opt-out.
- **Not reported.** A type with `[DynamoDbEntity]` or `[DynamoDbDocument]` the generator could not map, which has its own error under [Entities](#entities), and a DynaSharp type, such as `Item` in `shop.Of<Item>()`. `client.Table<Item>("Shop")` is reported: a table without a schema is opened with `client.Table("Shop", "pk", "sk")`, which reads and writes `Item`.

`DYNA232` (error) reports a value of another type than its handle holds, `customers.Update(key).Set(Customer.Attrs.Name, 5)`, also in `SetIfNotNull` and on transaction steps. The compiler reports the overload for a nullable value type and says `CS0453`, that `string` must be a non-nullable value type.

- **Fix.** Pass a value of the handle's type, or the handle you meant.
- **Not reported.** A handle of a value type, such as `Set(Customer.Attrs.OrderCount, "x")`, where the compiler says `CS1503` and names both types.

`DYNA233` (error) reports `Add` by a handle that is no number or set, `customers.Update(key).Add(Customer.Attrs.Name, "x")` or `orders.Update(key).Add(Order.Attrs.Lines, lines)`, where the compiler says `CS0315` about `IAddableConverter`. DynamoDB's `ADD` adds only to a number or a set.

- **Fix.** `Set` writes the value. `Add` is for a counter, `Add(Customer.Attrs.OrderCount, 1)`, and a set, `Add(Customer.Attrs.Interests, ["books"])`. The code fix turns `Add` into `Set`.

`DYNA234` (error) reports a LINQ method called on a read instead of its items, `orders.Query(pk).Descending().ToListAsync()`, where the compiler says `CS1061`, that the read has no `ToListAsync`, or `CS0411` about `AsyncEnumerable.ToListAsync`. A read is a command; only `ItemsAsync`, the call that sends it, streams items. It covers every `AsyncEnumerable` and `Enumerable` method the read does not have itself, such as `Where`, `FirstAsync`, `ToListAsync`, and `Take` after `Select` or on a fan-out, which have no `Take` of their own.

- **Fix.** Call `ItemsAsync()` first, `orders.Query(pk).Descending().ItemsAsync().ToListAsync()`, or `ValuesAsync()` after `Select`. A query and a scan have a `Take(n)` of their own, a clause before the call that sends it, which caps the items of the read and sizes its pages. `Limit(n)` differs: it sets how many items DynamoDB reads per page, not how many the read returns. The code fix inserts `ItemsAsync()`, or `ValuesAsync()`, before the method.
- **Not reported.** A method the read has, such as `Limit`, `Take` or `Select`, and your own extension method that binds.

`DYNA235` (error) reports `IfVersion` on a `Put`, `carts.Put(cart).IfVersion(3)`, in a transaction too, where the compiler says `CS1061`, that the command has no `IfVersion`, or `CS0411` about the `IfVersion` of an update. A put takes the version from the entity: it writes only where the stored item holds the version of the entity, and stores the next one. It is an error for the reason `DYNA230` to `DYNA234` are: it reports only code that does not compile.

- **Fix.** Put the entity as it was read, with the version it came with, or call `IgnoreVersion()` to write without the check. An entity without a `[Version]` has no version to check: mark a `long` property with `[Version]`, or state the condition with `If(...)`. The code fix removes `IfVersion(...)`, or, for an entity with a `[Version]`, replaces it with `IgnoreVersion()`.
- **Not reported.** An extension method of your own named `IfVersion`, a put of `Item` or of a document, and a step of a transaction in a variable.

`DYNA236` (warning) reports a write of a batch to a table of an entity with a `[Version]`: `carts.BatchWrite().PutAll(items)`, also `Put`, `Delete` and `DeleteAll`, by key or by entity, and a `Put` or a `Delete` on another table. A batch cannot carry a condition, so it cannot check a version, and it throws `InvalidOperationException` when it is sent. The first such write of a chain is reported.

- **Fix.** Call `IgnoreVersion()` anywhere in the batch, which writes each item without the check and with the next version, as a `Put` stores it. To keep the check, write the items one by one with `Put`, or in a `TransactWrite`. The code fix adds `IgnoreVersion()` right after the write it reports.
- **Not reported.** A member with `IgnoreVersion()` on a batch of that type anywhere in it, and a batch that the member returns, passes to a method or stores outside a local, where the code that gets it can still call `IgnoreVersion()`. A write of a type parameter.

`DYNA237` (warning) reports a `DateTime` from a constructor without a `DateTimeKind`, `new DateTime(2026, 1, 15)`, whose kind is `Unspecified`, where DynaSharp writes it as an instant: in an object initializer or a `with` of a mapped type, to a property that `DateTimeConverter` stores or a key template holds as a timestamp, `new Shift { Start = new DateTime(2026, 1, 15) }`, and as an argument of a generated key builder, `Shift.Keys.Sk.Between(new DateTime(2026, 1, 1), new DateTime(2026, 2, 1))`. Such a time names no instant, so the write of the entity throws, and the key builder throws `KeyFormatException`.

- **Fix.** Pass `DateTimeKind.Utc`, as in `new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)`, or use a `DateOnly` for a date, which has no kind and is written as `yyyy-MM-dd`. The code fix passes `DateTimeKind.Utc`, with `0, 0, 0` before it for a date; a `DateOnly` changes the type of the property, so it is yours to make.
- **Not reported.** The rule looks at the constructor where it stands: a time in a variable, `default`, `DateTime.MinValue` and `DateTime.Parse(...)` are not followed, and neither is a time given to a handle, in `Set` or a condition. A property with a converter of your own, which decides for itself, a `DateTimeOffset`, which takes such a time for local time, and `TryOf` of a key builder, which gives null for it instead of throwing, are correct.

`DYNA238` (error) reports `client.Table<Order>()` for an entity that declares no table, neither with `[DynamoDbEntity(Table = "...")]` nor through `[assembly: DynamoDbDefaults(Table = "...")]` of its assembly. The call compiles and throws `InvalidOperationException`. The same goes for the members of `DynaSharp.Testing` that take no table name: `CreateTable<T>()`, `DescribeTable<T>()`, `EnableStream<T>(viewType)`, `DisableStream<T>()`, `Put(entity)`, `Get<T>(key)`, `Get(key)`, `Delete(key)`, `Delete(entity)`, `Items<T>()`, `TableItems<T>()`, `GetItem(key)` and `PutItem<T>(item)` of the fake, and `StubResponse.Table<T>()`.

- **Fix.** Declare the table on the entity or for its assembly, see [Declaring the table](tables.md#declaring-the-table), or pass the name: `client.Table<Order>("orders")`.
- **Not reported.** A type parameter, and a type without `[DynamoDbEntity]`, such as one written by hand, whose `Info` says its table in code.

`DYNA239` (error) reports an `Update` or a `Delete` by key of an entity with a `[Version]` that calls neither `IfVersion(n)` nor `IgnoreVersion()`: `carts.Update(Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines)`, `carts.Delete(Cart.Keys.Of("bob"))`, and the `Update` and `Delete` steps of a transaction. A key carries no version, so the command throws `InvalidOperationException` when it is sent. A put and a delete of the entity itself take the version from it.

- **Fix.** `IfVersion(n)`, with the version you read, when the write depends on that copy: an edit of what the user saw. `IgnoreVersion()` when it does not: a counter, an import. `IgnoreVersion()` still advances the version, so a holder of an older copy then fails its checked write; keep a hot counter on an item of its own (see [Optimistic locking](commands.md#optimistic-locking)). The code fix offers either right after the verb; `IfVersion(version)` puts a name there that nothing in scope has (`expectedVersion` where `version` is taken), so the code compiles only once you put the version you read in its place.
- **Not reported.** Where the build cannot see the whole write, and the command still throws when it is sent: a command that the member returns, passes to a method or stores outside a local, and a step that goes into a local, a conversion or a method of your own before its next verb. For a command, `IfVersion` or `IgnoreVersion` on a command of that type anywhere in the member keeps it quiet. A write of a type parameter.

`DYNA240` (warning) reports `CreateTable()` on the table of an entity whose chain leaves out another entity that declares the same table, by its own `Table` or by the default of its assembly, in this project or in a project or package it references: `await orders.CreateTable().Including<Customer>().IfNotExists().SendAsync(ct)` while `Product` declares `Shop` too. The table is created without what the entity left out declares: its indexes, of which a local one cannot be added later, and its [time to live](entities.md#time-to-live). With `IfNotExists()`, [the drift report](tables.md#a-table-that-exists) cannot show such an index either, since it compares the table with the entities the chain names. The message names the entities left out, in the order of their full names.

- **Fix.** Name each entity left out with `Including<T>()`. The code fix adds them right after `CreateTable()`, one per line in a chain written so.
- **Not reported.** It checks the chain as written, from `CreateTable()` to `SendAsync`, `ToRequest`, `ToRequestJson`, an `await`, or the `return` or expression body of a method, local function, property or lambda that returns the command as it is: `public static CreateTableCommand Command(DynamoDbClient client) => client.Table<Order>().CreateTable().Including<Customer>().IfNotExists();`, which a test can pass to `fake.CreateTable(command)`, is checked as written. Not checked: a chain with `IncludingAll()` anywhere, a command that goes into a variable, a field, an argument or a method of your own, whose code may still add an entity, and a table opened with a name of its own, `client.Table<Order>("orders-test")`, which can be another table. A table in a field or a variable counts as the declared one. An entity the code cannot name, such as an `internal` one of another assembly, is left out of the message, as is an entity of a table without a model and one whose code was not generated.

## Expressions

In an interpolated expression such as `$"{Attr("status")} = {"open"}"`, the text is DynamoDB's grammar, an attribute in a hole stands for its name, and anything else in a hole is a value. DynaSharp generates every placeholder.

`DYNA210` (warning): a string where the grammar wants a name, as in `$"{"status"} = {"open"}"`. It is a value there, so the expression compares two values, which DynamoDB accepts as a constant.

- **Fix.** Wrap the name in `Attr(...)`, from `using static DynaSharp.Expr;`. The IDE offers this fix.
- **Not reported.** A non-constant string. An expression that names no attribute at all throws `ArgumentException` when the command is built.

`DYNA211` (error): the text holds its own `#name` or `:value` placeholders, which would clash with the generated ones. Write the name in `Attr(...)` and the value in a hole.

`DYNA212` (warning): the expression breaks DynamoDB's grammar: an unclosed parenthesis, an unknown function or one with the wrong operand count, `BETWEEN` without `AND`, more than 100 operands of `IN`. Function names are case-sensitive, and condition and update functions are separate groups. Only the first error is reported; what follows is noise.

- **Fix.** Fix the expression, or if DynamoDB accepts it, suppress the warning and report the case.

`DYNA213` (warning): a constant path on a command of a mapped type starts with a name the type does not have, such as `orders.Update(key).Set("Totl", 1)`, or a bare name starts a path in an expression's text, `orders.Scan().Filter($"Totl > {1}")`. Under `Naming` of `[assembly: DynamoDbDefaults]` the attribute has the converted name, so `"Total"` is reported where the item holds `total`.

- **Fix.** Check the spelling. An attribute outside the entity is legitimate: suppress the warning there, or pass the path in a variable. The code fix offers each attribute of the type one or two letters off, in a string literal, and in the text where the name can stand bare.
- **Not reported.** A path in a variable. In the text only a path's first name counts, so `ShipTo.Stret` is not checked, nor is the text of a transaction step, which has no type, or of your own method that takes the expression.

`DYNA214` (warning): a constant path is not a path. A path is names joined by dots, with list indexes in brackets, such as `a.b[2].c`, so `a..b` and `a[` are not. The same path throws `ArgumentException` when the command is built. An attribute whose name holds a dot or a bracket cannot be addressed by a path at all.

`DYNA215` (warning): a handle of one mapped type on a command of another, as in `orders.Query(key).Filter(Product.Attrs.Stock.Gt(1))`. A handle is a path and a converter, so this compiles, DynamoDB looks for the attribute on items that lack it, and the result is empty.

- **Fix.** Use a handle of the command's type, or a raw path for an attribute outside it.
- **Not reported.** A mixed read takes a handle of any of its types, a type and its base types share their attributes, and a nested document's handle belongs inside `.At(...)`. A command that cannot be traced to its type, such as one in a generic helper, is not checked.

`DYNA216` (warning): `begins_with` or `contains` with a number or boolean handle as its first operand, as in `$"contains({Order.Attrs.Total}, {1})"`. The grammar is valid, DynamoDB finds no item, and the result is empty. `Handle.BeginsWith(...)` on such a handle does not compile (`CS0619`), nor does `Handle.Contains(...)` (`CS1929`); this rule catches the same mistake in expression text. On a `[Flags]` enum, stored as a number, `BeginsWith` compiles, and this rule reports it.

- **Fix.** Compare with `Eq`, `Gt` or `Between`, or store text. `contains` on a set or list wants a handle of that type, which `Handle.Contains(element)` takes.
- **Not reported.** Any other enum, which is stored by name, and your own converter.

`DYNA220` (warning): a DynamoDB reserved word as a bare name in an expression's text, such as `orders.Scan().Filter($"Total > {100}")`, where `total` is reserved. DynamoDB rejects the request with a `ValidationException`, in any case of the word and at any depth of a path, so `info.year` fails too. AWS's list has 573 words, among them `name`, `status`, `value`, `data`, `count`, `type`, `key`, `user`, `order` and `ttl`: [Reserved words in DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ReservedWords.html).

- **Fix.** Write the name as a hole, `{Attr("total")}` or a handle such as `{Order.Attrs.Total}`, which DynaSharp sends as a `#name` placeholder. Your own `#name` in the text is `DYNA211`. The code fix writes `{Attr("total")}` where the word stands, and the `using static DynaSharp.Expr;` the file lacks.
- **Not reported.** A reserved word as a function, `size(...)`, or as a grammar keyword, which is no name. A bare `size` in any case, which DynamoDB takes as a name (`size = {1}`, `Size = {1}`, `REMOVE size`).
