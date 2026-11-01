> Historical record, frozen: this file is not updated, and its amendments override the sections below them.
> Replaced by [architecture.md](../architecture.md), which describes the code as it is today.

# DynaSharp - M2 source generator design

Implementation design for M2. `requirements.md` stays the source of truth for WHAT; this file is the HOW for the generator. Compiler behaviour here is unverified until the spikes in section 11 ran, and the allocation numbers in section 6 are estimates until G8 measures them.

## Amendments from the recipes (R2)

`api-design.md` section 9, last block, is the contract; this is how it landed. Decisions taken there:
- The action is `SET #_type = if_not_exists(#_type, :t)`, by decision of the lead, where section 9 says `#_type = :t`: an update through the wrong table at a key held by another type would otherwise rewrite that type without a word. A new item gets the type, and an existing one keeps it.
- `WriteRules.ExpectUpdate` marks `CommandState.OwesDiscriminator` when `TableModel.UpdateWritesDiscriminator` says so, and `Settle` writes it last, so the SET of an existing update keeps its placeholders and a transaction gets it per action. The flag is cleared on the way, which makes a second `Settle` a no-op. `Discriminator` is on the table model and not on `EntityWrites`, because an entity without a version or a template has no `EntityWrites`.
- `ExpressionWriter.SetMissingAttribute` walks the clauses to the root of each action's path, because DynamoDB rejects two actions on one path and a raw path is the escape hatch: `.SetIfNotExists("_type", ...)`, a `Remove`, or an action spelled out in an interpolated string stays the only action. Only the target counts: a condition or a right side that reads `_type` does not stop the action. It works on the bytes that are already there, so it allocates nothing, and the name goes through the map like any other.
- `PropertyModel.FallbackKey` is the first lossless key holding a stored input. A nullable input has none: null is omitted on write, so its attribute missing means null, and only a sparse key can hold one; a stale key must not bring a removed value back. `ReadEmitter` keeps `var __item = reader` and, after the loop, parses the key of what is still unset. That is one struct copy per item and no change in the loop, and the second walk happens only for an item that lacks something. `ConverterChecks.TryFindString` and `Keys.X.TryParse` fail quietly, so a key that is absent, of another type or of another shape leaves the property unset, and the `Missing` check or the initializer below it decides as before. A present attribute is never replaced.
- `KeyTemplateEmitter` adds a key of literals to `KeyTemplates` when a sparse index holds it, with no inputs and a `Format` that ignores its values. `KeyInputChanges.FollowsSibling` writes it when the update sets a key of the same index, and `LeavesEveryIndex` removes it as it removes any other. A literal key of an index that is not sparse stays out, so an entity with nothing else costs an update nothing. An attribute with no template in `Produces` is now only reachable with metadata written by hand.
- `Query(IEnumerable<string>, SortKey)` carries `[OverloadResolutionPriority(-1)]`: it leaves the candidates whenever another overload applies, so what bound before binds the same and what was `CS0121` (`default`, `null`, `[]`) stays that. Without it, `ArraySegment<string>`, which reaches `ReadOnlySpan<string>` by a conversion, is ambiguous. `string[]` and a collection expression stay on the span. `CompileGuardTests` pins the overload per call through the semantic model. A compiler below C# 13 ignores the attribute, so with `LangVersion` 12 a `string[]` is `CS0121`, which a guard pins as well.

## Amendments from the polish package (A5)

`api-design.md` section 9, last block, is the contract; this is how it landed in the generator. Decisions taken there:
- `IDynamoDbEntity<T>.ReadVersion(entity)` is a `static virtual` member, on the entity interface because `Delete(entity)` is constrained to it. It is emitted beside `WriteNextVersion`, and a static member is what keeps it free of a box for a struct. The condition waits in `CommandState.EntityVersion` for `WriteRules.Settle`, because `IgnoreVersion()` and `IfVersion(n)` can still follow and what stands in the expressions cannot be taken out.
- `Indexes` holds a `const string` per index, named like the builder prefix: `orders-by_date` gives `OrdersByDate`, `GSI1` gives `Gsi1`. A name a class cannot declare without a warning takes the prefix `Index`, as a digit does: the class itself (`indexes`, CS0542) and what it inherits from `object` (`to-string`, CS0108). Builders end in `Pk` or `Sk`, so a global index without a sort key and a local one can pass them and still want one constant, which is DYNA018. `Indexes` is reserved on every entity, also one without an index, so that adding the first index breaks nothing. Unlike `Keys`, a base entity has the class only if it declares an index, so `new` looks at that.
- DYNA024 rejects a non-nullable property of an enum without members, which is written by name and has none. `[Flags]` goes by value, and a converter on the property or the enum has its own way. A collection of such an enum stays legal, because an empty one writes.
- The sparse sibling rule runs in `KeyInputChanges` from the metadata that exists. An attribute of the index without a template is a key of literals, or a key of the table, which `EntityWrites.IsTableKey` tells apart: a key of the table is on every item, a key of literals only comes with a put. An input of the table key counts as not produced also when it is plain, which has no template that would reject it first.

## Amendments from the reads package (M3b)

- `IDynamoDbDiscriminated<TSelf> : IDynamoDbEntity<TSelf>` is the role of an entity with a value in `[Discriminator]`: `EntityEmitter` puts it in place of `IDynamoDbEntity<TSelf>` on the generated declaration. It is a marker with no members, and the mixed reads constrain their type arguments to it, so a type that opted out does not compile there. The discriminator itself stays in `DynamoDbEntityInfo`. A type written by hand implements the interface itself; one that claims it and has no discriminator fails at `Of<...>()`.

## Amendments from the version and template package (M3a)

`api-design.md` section 9 is the contract; this is how it landed in the generator. Decisions taken there:
- `IDynamoDbDocument<T>.WriteNextVersion(writer, entity)` is a `static virtual` member, on the document interface because `Table<T>.Put` is constrained to it. An entity with a `[Version]` gets `__WriteAttributes(writer, entity, version)`, which both write paths call, so `WriteAttributes` stays a plain copy. The next version goes through a `checked` cast, so a value the property cannot hold fails at the `Put`.
- `DYNA011` also rejects a `[Version]` with a custom converter, because the condition compares numbers. `DYNA023` rejects one that is part of a key.
- `DynamoDbEntityInfo.KeyTemplates` holds every key attribute that is made of properties, once: a template of the table without a delegate, a template of an index with `static values => Keys.X.Format(...)`, and a plain key of an index without one. A plain key of the table is left out, because DynamoDB rejects its update by itself, and a template of literals, which cannot go stale.
- A handle finds its template by its path. A handle is not tied to an entity, and a nested path never matches. The value is boxed until the terminal, for an update of a key input and nothing else.
- `KeyModel.Gates`: a key that several indexes share is written when any of their sparse gates is open, not under the gate of the index that claimed the attribute. A `Remove` keeps such an attribute while another index holds the item.
- `ConverterModel.WriteForwarder`: a converter with explicit members is called through `NullableConverter` or `NullableReferenceConverter`, which reach it through a type parameter. Reads were generic already.
- A set that the initializer leaves empty (`= []`, `= new()`) is `KeepCurrent`, not `EmptySet`. A filled initializer stays `EmptySet`, or a read would give back what was never written.
- `FindName` of a generated enum converter returns an empty span for an undefined value; `GetName` throws `DynamoDbMappingException`, and `KeyWriter.WriteEnumName` throws `KeyFormatException`.
- Hint names of types that clash ignoring case get a number by the order of their full names. The clashes are one pipeline value, so a new type does not touch the output of the others.
## Amendments from the key emitter (G6)

`KeysEmitter` as shipped wins over section 4. Decisions taken there:
- `Attrs` is emitted on documents too, because a nested path needs the handles of the document: `Order.Attrs.ShipTo.At(Address.Attrs.City)` has the path `ShipTo.City`. `At` is an extension on `Attr<T, TConverter>` constrained to documents. A member cannot be both a whole attribute and a holder of nested members, because every command takes `Attr<,>` by its exact type, so nesting is a call. `Attrs` is therefore reserved on documents as well (DYNA014), and `new` is emitted when a base type has one.
- The converter of an enum is a `file` type, which no public signature can name, so `Attrs` nests a public `__<ConverterName>` that forwards to it. It adds `IDynamoDbSetElementConverter<T>` when a handle uses it as a set element, and then needs `SetType`, `WriteElement` and `ReadElement` on the file type.
- A handle has the type without nullability and the converter for the value that is not null. It is `internal` unless the property, its type and its converters are public. An attribute name holding `.` or `[` gets no handle, because no path can address it; a `[KeyOnly]` property has no attribute and no handle.
- A prefix of a literal between placeholders takes the whole literal. A prefix of a literal after the last placeholder takes its first character, the delimiter. `UpperBound` exists at every depth of `Prefix`. `PrefixPartial` names its parameters after the placeholders, the last one included, so that a property named `Text` cannot collide.
- `TryParse(string?)` goes through `KeyWriter.TryWriteText`, which fails on ill-formed text: `Encoding.UTF8` would turn a lone surrogate into U+FFFD, `Parse` would succeed, and `Format(Parse(x)) == x` would not hold.
- A typed `Add` requires the marker `IAddableConverter`, on `NumberConverter<T>` and the three set converters; a custom converter of a number or set implements it.
- `Attr<T, TConverter>` converts to `Attr` and not to `string`: with a conversion to `string`, `Set(handle, wrongValue)` would compile through `Set(string, AttributeValue)`. `Project(params ReadOnlySpan<Attr>)` is the overload for handles on `Get`, `Query` and `Scan`, and `Remove` by a handle is an extension on `IUpdatable<T>`.

## Amendments from the model (G4)

The model and diagnostics as shipped win over sections 3 and 7. `Descriptors.verified.cs` and `AnalyzerReleases.Unshipped.md` hold the diagnostics; `Order.model.verified.cs` shows the model. Decisions taken there:
- `Keys` and `Attrs` are both reserved on entities (DYNA014).
- A type with the name of a class generated into it is DYNA025, not DYNA014 as the table in section 7 has it: no member conflicts, and the generated class would be CS0542. That is `Attrs` for every type, `Keys` for an entity, and `Indexes` for an entity with an index. A document named `Keys` is legal, because it gets `Attrs` alone.
- `{Id}{Unique}`, variable then fixed, is rejected too (DYNA106): the reader reads forward, so the string would swallow the Guid. `{Unique}{Id}` stays legal.
- A placeholder on a `[DynamoDbIgnore]` property is an error (DYNA102): a read would leave it default and a later put would write a different key. `[KeyOnly]` is the legal form.
- `required string?` missing on read keeps the current value, not a throw, because null is omitted on write and could not round trip.
- Plain keys keep the property's attribute name, not `pk`.
- Two keys may share an attribute when their template text is identical (`SharesAttribute`); different templates are DYNA004.
- `[Discriminator(OptOut = true)]` is the opt-out. `[LocalIndexKey("LSI1", "{Total:n10.2}")]` takes the sort key as a required argument.
- Formats are strict: `n<i>` only on integers, `n<i>.<f>` only on `decimal`, `plain` only on numbers.

## Amendments from the key encodings (G3)

`KeyWriter` and `KeyReader` shipped with a trailing `string placeholder` on every write that can throw, `ReadOnlySpan<byte> delimiters` on variable-width reads, and `TryReadParsable<T>` needing `IUtf8SpanFormattable`. `KeyFormatException` is in `DynaSharp`. The generated parser must range-check when reading into a type narrower than the reader's: `TryReadFixedPoint` returns `decimal` and `TryReadInt64` returns `long`, so an `int` property must fail `TryParse` for text above `int.MaxValue`, otherwise `Parse(x)` succeeds where `Format(Parse(x))` would throw.

## Amendments from the G0 spikes

`g0-spike-report.md` holds the evidence; its corrections apply here. In short:
- **Snapshots:** no Verify. Verify 33 fails the build without a sponsorship declaration, a legal statement only the owner can make. A local comparer in the test project replaces it: read `.verified.cs`, compare, write `.received.cs` on a mismatch, fail with a diff, about 40 lines. Keep `*.verified.* text eol=lf`, `*.received.*` in `.gitignore` and `<Compile Remove="**/*.verified.cs;**/*.received.cs" />`. No package, no `DeterministicSourcePaths` concern.
- Emit `!` on constructor arguments of non-nullable reference type after the `__set` checks (CS8604).
- `Keys.Of(...)` returns a nested `readonly struct Keys.PrimaryKey : IKey<Order>` holding the placeholder values; `IKey { void WriteKey(DynamoDbWriter); }`, `IKey<TEntity> : IKey`. `Table<T>.Get<TKey>(TKey) where TKey : IKey<T>` infers, and a key of another entity is a compile error.
- DYNA015 emits nothing for that type. Spans `KeyWriter` only reads are `scoped`.
- Diagnostics use tree locations in a separate output node over `CompilationProvider`, so `#pragma` and `.editorconfig` work. `DiagnosticInfo` keeps `FilePath` and `Span` only. The transform and the model stay free of `Location`.
- Emit `#pragma warning disable CS1591` in generated files: it does fire in `<auto-generated/>`.
- `InternalsVisibleTo` from the generator is safe, so model types stay internal.
- Lock files that change: the generator's, Benchmarks, UnitTests. Add a CI step failing on `git status --porcelain` after restore, because locked mode silently re-creates a missing lock file. Never pack with `-r`.
- DYNA201 is a warning implemented as its own syntax and symbol rule, restricted to DynaSharp ref structs; CS8345 is covered by docs only.

## Amendments from the API decision

`api-design.md` wins where it differs. Section 6 (typed request overloads, `EntityKey<T>`) is dropped: `Table<T>` is the typed path, and generated key structs implement `IKey` instead of `EntityKey<T>`. `Item` implements `IDynamoDbDocument<T>`. `AttributeValue.From<T, TConverter>` is dropped: values go straight through converters. `DynaSharp.Model` no longer exists. Generated `Attrs` handles feed conditions, updates and projections; see `api-design.md` section 1.3.

## Amendments from the key grammar review

`requirements.md` "Key grammar" was revised after this design was written and wins where they differ. Changes for the implementer:
- Delimiters: every character adjacent to a placeholder (I6 below is superseded), checked on every placeholder and on the formatted text.
- `KeyFormatException`, an `ArgumentException`, from `Format`, `Prefix`, `UpperBound` and `Parse` alike. Exact `Parse`: `Format(Parse(x)) == x`.
- New builders `UpperBound` and `PrefixPartial`. Timestamp format `yyyy-MM-ddTHH:mm`; `ticks` is seven fraction digits.
- `plain` replaces `g`. `decimal` plain text is normalized. Fraction check by `decimal.Round`, `i + f <= 28`, sign test `value < 0`.
- `[KeyOnly]` replaces `[DynamoDbIgnore]` for key-only storage, and `[DynamoDbIgnore]` keeps its plain meaning. I3 applies to it.
- `[Discriminator]` is required on an entity (I2 keeps `_type` on every entity); typed reads check `_type`; generated projections include `_type` and the keys.
- `[LocalIndexKey]` is in M2 (section 5.3 said LSIs are not).
- New diagnostics: `[Flags]` or aliased enums under the name format, computed property in an index template, entity outside a nullable context, string before a delimiter in a sort key (warning).
- Sparse: all key attributes of the index are omitted when any input is null, not per attribute as section 4 shows.
- Values reject U+0000-U+001F and ill-formed UTF-16.

## 0. Findings that shape the design

**Toolchain facts**

| Fact | Value | Consequence |
|---|---|---|
| SDK compiler | Roslyn 5.12.0-1.26522.148, in `D:\oss\.sdk\dotnet\sdk\11.0.100-rc.3.26522.148` | Default `LangVersion` for net11 is 15.0 |
| Stryker 5.0.0 compiler | Roslyn 5.9.0-1.26357.3, 3 minor versions older | It compiles `src/` only, so `src/` must stay parseable by 5.9. `src/` has no `[with(...)]`, `extension(...)` or `static (ref r)`; tests do (`CountingArrayPool.cs`, `TestJson.cs`). Keep that split. |
| Newest `Microsoft.CodeAnalysis.CSharp` on NuGet | 5.9.0; 5.12 is not published | The pin cannot follow the SDK |
| `Verify.SourceGenerators` 2.5.0 | Published 2024-09-18, built on Verify 26.5, no net10 or net11 asset | Stale: see section 8 |
| `Verify.TUnit` 33.3.4 | Published 2026-10-16, net11 asset, needs `TUnit.Core >= 1.75.0` | Compatible with the repo's TUnit 1.76.45 |

**Conflicts in the existing material**

- "2.5 KB per item" is out of date. It is P3's number (253 KB per page). After the dictionary pre-sizing decision, P6 measured 187 KB for 100 items, so 1.87 KB per item. Section 6 uses both.
- `spec/patterns.md` P3 still lists "escaping"; the requirements reject escaping.
- The `Parse` filter in `DynamoDbPipeline` lets `OverflowException` through. An entity number out of range would surface raw, and any other conversion failure as `UnrecognizedResponse`, which is the wrong message. Section 5.8 fixes this without touching the pipeline.
- The `IDynamoDbConverter<T>` docs say "implement it on a class". Built-ins must be structs, and the docs' example `EpochSecondsConverter` becomes a built-in.
- `AttributeValueConverter` is internal, but generated code lives in the consumer assembly.

**Decisions on spec interpretation**

| # | Decision | Why |
|---|---|---|
| I1 | New `[DynamoDbDocument]` for nested types, with `IDynamoDbEntity<T> : IDynamoDbDocument<T>` | A nested `Address` has no key, yet "missing partition key" must stay a diagnostic |
| I2 | `_type` is written on every `[DynamoDbEntity]`, never on documents | Spec says "default the type name", and a type can then join a mixed query without a migration. Cost is about 15 bytes per item. |
| I3 | `[DynamoDbIgnore]` on a timestamp input requires `ticks`; `ms`, `us` and `s` are all lossy | "Compile error if the format loses information", applied strictly |
| I4 | Null is omitted on write, never `{"NULL":true}`; reads accept both | Required for sparse keys. No `WriteNull` option in M2. |
| I5 | Default attribute name is the C# property name, unchanged. No naming policy. | Predictable. `[DynamoDbAttribute]` overrides it. |
| I6 | Delimiters are every non-alphanumeric character in any literal of the template | `USER#{Id}` has no literal after its placeholder, so with first-character-after-a-placeholder rules `1#ORDER` would pass and collide with `USER#{Id}#ORDER`. Stricter validation changes no encoded form |

## 1. Project layout

```
src/DynaSharp.Generators/           netstandard2.0, not packable on its own
  DynaSharpGenerator.cs             IIncrementalGenerator
  Parsing/ModelBuilder.cs           symbols -> value model
  Parsing/KeyTemplateParser.cs      pure text -> AST, no Roslyn types
  Parsing/KeyTemplateBinder.cs      AST + properties -> bound template
  Parsing/ConverterResolver.cs
  Model/*.cs                        records, EquatableArray<T>
  Emitting/*.cs                     text emitters
  Diagnostics/Descriptors.cs
  AnalyzerReleases.Shipped.md, AnalyzerReleases.Unshipped.md
  IsExternalInit.cs                 polyfill, internal
src/DynaSharp/Mapping/              attributes, interfaces, EntityKey, exception
src/DynaSharp/Keys/                 KeyWriter, KeyReader, KeyTimeFormat
src/DynaSharp/Serialization/Converters/
tests/DynaSharp.UnitTests/{Generators,Mapping,Keys}/
tests/DynaSharp.PackageSmoke/       outside the solution; consumes the real nupkg
```

**`DynaSharp.Generators.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="../BannedSymbols.txt" />
    <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
    <AdditionalFiles Include="AnalyzerReleases.Unshipped.md" />
  </ItemGroup>
</Project>
```

**Decisions**

- **`LangVersion` 12.0.** netstandard2.0 defaults to 7.3. Version 12 gives records, raw strings and collection expressions, and Stryker's 5.9 parses it. Never `preview`.
- **Roslyn pin: `Microsoft.CodeAnalysis.CSharp` 5.0.0**, one version in `Directory.Packages.props`. It is a floor, not "latest":
  - It only has to be at or below every compiler that loads the generator. Consumers target net11 and so have 5.12+.
  - It has a netstandard2.0 asset.
  - Add a Dependabot `ignore` for this package, because the `Microsoft.CodeAnalysis.*` group would otherwise raise the floor.
  - Test code also compiles against 5.0.0, so in-process compilation tops out at C# 14. That is enough, since generated code targets C# 11.
- **Attributes reach consumers as ordinary public types in `DynaSharp.dll`.** No `RegisterPostInitializationOutput`, so one definition exists and no `InternalsVisibleTo` conflicts arise. The generator finds them by metadata name.
- **Packing into `DynaSharp`:**

```xml
<!-- DynaSharp.csproj -->
<ItemGroup>
  <ProjectReference Include="../DynaSharp.Generators/DynaSharp.Generators.csproj"
                    ReferenceOutputAssembly="false" PrivateAssets="all" />
  <None Include="../DynaSharp.Generators/bin/$(Configuration)/netstandard2.0/DynaSharp.Generators.dll"
        Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
</ItemGroup>
```

- **In-repo consumers** need the analyzer explicitly, because it does not flow through `DynaSharp`'s project reference. AotSmoke, IntegrationTests and Benchmarks get:
  `<ProjectReference Include=".../DynaSharp.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
  UnitTests uses `OutputItemType="Analyzer"` with the assembly referenced as well, plus a direct `Microsoft.CodeAnalysis.CSharp` package reference for driver tests.
- **No `InternalsVisibleTo` from the generator.**
  - Records need an `IsExternalInit` polyfill on netstandard2.0.
  - An internal polyfill made visible to a net11 test project can clash with the BCL type.
  - So test-facing types (`KeyTemplateParser`, `EquatableArray<T>`, the model) are `public`. That is harmless because the dll only ever sits under `analyzers/` and never becomes a compile reference.
  - The generator class is public as well, which avoids CA1812.
- **Lock files.** The generator inherits `RuntimeIdentifiers` from `src/Directory.Build.props`. Keep that: finding M0 showed a RID publish restores the whole graph, so its lock file needs those RIDs. All project lock files get regenerated in WP-A.
- **Analyzers as errors.**
  - Everything in the root `Directory.Build.props` applies.
  - RS2008 is satisfied by the `AnalyzerReleases` files, which double as the DYNA table of record.
  - RS1035 bans `File` and `Environment`, so use `"\n"`, never `Environment.NewLine`. That also makes output identical on Windows and Linux.
  - Add a `[src/DynaSharp.Generators/**.cs]` section to `.editorconfig` only for rules that fail on netstandard2.0.
  - The same `BannedSymbols.txt` applies.
- **Solutions and config changes:**
  - Add the project to `DynaSharp.slnx` and `DynaSharp.Mutation.slnx`.
  - Add `DynaSharp\.Generators\.dll` to `coverage.config`.
  - Add `*.received.*` to `.gitignore`.
  - Add `*.verified.* text eol=lf` to `.gitattributes`.
  - The test project needs `<Compile Remove="**/*.verified.cs;**/*.received.cs" />`.
  - In CI, add a `tests/DynaSharp.PackageSmoke` step: restore from `artifacts/packages`, build an entity, run it. Analyzer packaging failures are otherwise silent.

## 2. Generator pipeline

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class DynaSharpGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var language = context.ParseOptionsProvider
            .Select(static (options, _) => ((CSharpParseOptions)options).LanguageVersion)
            .WithTrackingName(TrackingNames.Language);

        Register(context, language, WellKnownNames.EntityAttribute, MappedKind.Entity, TrackingNames.Entities);
        Register(context, language, WellKnownNames.DocumentAttribute, MappedKind.Document, TrackingNames.Documents);
    }

    private static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<LanguageVersion> language,
        string attributeName, MappedKind kind, string trackingName)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                (syntax, token) => ModelBuilder.Build(syntax, kind, token))
            .WithTrackingName(trackingName)
            .Combine(language)
            .WithTrackingName(TrackingNames.WithLanguage);

        context.RegisterSourceOutput(results, static (output, input) => SourceEmitter.Emit(output, input.Left, input.Right));
    }
}
```

**Rules**

- `ModelBuilder.Build` returns `BuildResult(TypeModel? Type, EquatableArray<DiagnosticInfo> Diagnostics)`. No `ISymbol`, `SyntaxNode`, `Compilation`, `SemanticModel` or `Location` passes that boundary.
- **Diagnostics come from the generator, not a second `DiagnosticAnalyzer`.** There is one rule implementation.
  - They carry `DiagnosticInfo(string Id, EquatableArray<string> Arguments, string FilePath, TextSpan Span, LinePositionSpan LineSpan)`.
  - They are rebuilt with `Location.Create(path, span, lineSpan)`, the same trimming System.Text.Json uses.
  - Consequence: `#pragma warning disable` does not reach these diagnostics, because they are external-file locations. That fits the spec, where `{Id:g}` is the documented way to silence DYNA108, and `NoWarn` still works.
- **Emitters write text, never `SyntaxFactory` or `NormalizeWhitespace`.** Snapshots then do not move when the SDK's Roslyn changes.
- **One generated file per type:** hint name `Shop.Order.g.cs`, with nested types as `Shop.Outer.Order.g.cs`.
- **Enum converters are `file`-scoped inside that same file.** So no `Collect()` is needed and each entity stays independent. An enum used by two entities is emitted twice; each is a few lines.

**What is cached**

`ForAttributeWithMetadataName` caches the search per syntax tree, but the transform runs again for every matching tree on every compilation change. So:

- The transform must stay cheap: no walking BCL members, and only bounded `GetTypeByMetadataName` lookups.
- The cost is bounded by value equality: an equal `BuildResult` stops everything downstream. That is why `EquatableArray<T>` is required.
- A record with a plain `ImmutableArray<T>` member compares by reference and silently defeats the cache.
- Because the transform reruns, an enum member added in another file correctly reaches the model, and enum members must therefore be part of the model.

**Incrementality tests** are in section 8.

## 3. The value model

```csharp
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;
    public EquatableArray(T[] items) => _items = items;
    public int Count => _items?.Length ?? 0;
    public bool Equals(EquatableArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());
    // Hand-written FNV over element hashes: HashCode does not exist on netstandard2.0.
}

public sealed record BuildResult(TypeModel? Type, EquatableArray<DiagnosticInfo> Diagnostics);

public sealed record TypeModel(
    string? Namespace,
    string Name,                                  // Order
    string FullName,                              // global::Shop.Order
    string Keyword,                               // record | record struct | class | struct
    string HintName,                              // Shop.Order.g.cs
    EquatableArray<ContainingType> ContainingTypes,
    ConstructorModel Constructor,
    EquatableArray<PropertyModel> Properties,
    EntityModel? Entity,                          // null for [DynamoDbDocument]
    EquatableArray<EnumModel> Enums,
    bool HidesInheritedKeys);                     // emit `new` on the Keys class

public sealed record ContainingType(string Keyword, string Name);

public sealed record ConstructorModel(
    EquatableArray<ConstructorParameter> Parameters,
    bool DeclareDefaultConstructor,               // the type had only the implicit one
    bool NeedsFieldsConstructor);                 // any mapped property outside the parameters

public sealed record ConstructorParameter(string PropertyName, string? DefaultLiteral);

public enum WriteCondition { Always, WhenNotNull, WhenNotEmptyCount, WhenNotEmptyLength, Never }
public enum MissingBehavior { KeepCurrent, Throw, EmptySet }

public sealed record PropertyModel(
    string Name,                                  // Total
    string Identifier,                            // Total, or @class
    string ValueType,                             // global::System.Decimal
    string AttributeName,                         // Total
    int AttributeNameUtf8Length,                  // the switch key
    ConverterModel Converter,
    WriteCondition Write,
    MissingBehavior Missing,
    bool IsConstructorParameter,
    bool StoredOnlyInKey);                        // [DynamoDbIgnore] template input

public sealed record ConverterModel(
    string WriteConverter,                        // for the non-null value
    string ReadConverter,                         // includes Nullable wrappers
    string? EmptySetType);                        // the set type to create when the attribute is missing

public sealed record EntityModel(
    string Discriminator,
    KeyModel PartitionKey,
    KeyModel? SortKey,
    EquatableArray<IndexModel> Indexes,
    string? VersionProperty);

public sealed record IndexModel(
    string IndexName, KeyModel PartitionKey, KeyModel? SortKey, bool Sparse);

public sealed record KeyModel(
    string AttributeName,                         // pk, gsi1sk
    string BuilderName,                           // Pk, Gsi1Sk
    KeyTemplate? Template,                        // null for a plain key
    string? PlainProperty);                       // set for a plain key

public sealed record KeyTemplate(
    EquatableArray<TemplateSegment> Segments,
    string Delimiters,                            // for example "#"
    int StackBufferLength);                       // exact when all widths are fixed, else capped at 256

public abstract record TemplateSegment;
public sealed record LiteralSegment(string Text) : TemplateSegment;
public sealed record PlaceholderSegment(
    string Property, string ParameterName, string ValueType,
    KeyEncoding Encoding, bool InputIsNullable) : TemplateSegment;

public abstract record KeyEncoding(int FixedWidth)   // 0 means variable
{
    public abstract string WriteCall(string key, string value, string delimiters);
    public abstract string TryReadCall(string key, string target, char? delimiter);
}
// GuidEncoding, StringEncoding, EnumNameEncoding, DigitsEncoding, FixedPointEncoding,
// PlainIntegerEncoding, PlainDecimalEncoding, TimestampEncoding, DateOnlyEncoding, FormattableEncoding

public sealed record EnumModel(
    string FullName, string ConverterName, bool StoreAsNumber, string UnderlyingType,
    EquatableArray<EnumMember> Members);          // distinct by value for writing, all names for reading
public sealed record EnumMember(string Name, string Identifier, int Utf8Length, bool IsCanonical);
```

`KeyEncoding` is polymorphic rather than an enum plus `switch`. In this repo a `switch` with a `_ => throw` arm costs an uncoverable branch; see the finding about 7+ case string switches. The same applies to all emitters: prefer records with virtual methods or dictionaries.

## 4. Generated code

Input:

```csharp
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace Shop;

public enum OrderStatus { Pending, Paid, Shipped }

[DynamoDbDocument]
public partial record Address(string Street, string City, string? Zip);

[DynamoDbDocument]
public partial record LineItem
{
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
}

[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{CreatedAt}#{OrderId}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
[GlobalIndexKey("GSI2", PartitionKey = "TRACK#{TrackingId}", SortKey = "{ShippedAt}", Sparse = true)]
public partial record Order
{
    public required Guid UserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Guid OrderId { get; init; }
    public required OrderStatus Status { get; init; }
    public required decimal Total { get; init; }
    public Guid? TrackingId { get; init; }
    public DateTimeOffset? ShippedAt { get; init; }
    public Address? ShipTo { get; init; }
    public IReadOnlyList<LineItem> Lines { get; init; } = [];
    public HashSet<string> Tags { get; init; } = [];

    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    [Version]
    public long Revision { get; init; }
}
```

**Output `Shop.Order.g.cs`**

```csharp
// <auto-generated/>
#nullable enable
#pragma warning disable CS0612, CS0618

using __Key = global::DynaSharp.Keys;
using __Map = global::DynaSharp.Mapping;
using __Ser = global::DynaSharp.Serialization;

namespace Shop;

partial record Order : __Map.IDynamoDbEntity<global::Shop.Order>
{
    private static readonly __Map.DynamoDbEntityInfo __Info = new(
        discriminator: "ORDER",
        partitionKeyName: "pk",
        sortKeyName: "sk",
        indexes: new[]
        {
            new __Map.DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false),
            new __Map.DynamoDbIndexInfo("GSI2", "gsi2pk", "gsi2sk", true),
        },
        versionAttributeName: "Revision");

    /// <summary>Creates an instance. Declared because the generated constructor would remove the implicit one.</summary>
    public Order()
    {
    }

    [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    private Order(in __Fields fields)
        : this()
    {
        this.UserId = fields.UserId;
        this.CreatedAt = fields.CreatedAt;
        this.OrderId = fields.OrderId;
        this.Status = fields.Status;
        this.Total = fields.Total;
        if (fields.TrackingId__set) { this.TrackingId = fields.TrackingId; }
        if (fields.ShippedAt__set) { this.ShippedAt = fields.ShippedAt; }
        if (fields.ShipTo__set) { this.ShipTo = fields.ShipTo; }
        if (fields.Lines__set) { this.Lines = fields.Lines; }
        this.Tags = fields.Tags__set ? fields.Tags : new global::System.Collections.Generic.HashSet<string>();
        if (fields.ExpiresAt__set) { this.ExpiresAt = fields.ExpiresAt; }
        if (fields.Revision__set) { this.Revision = fields.Revision; }
    }

    static __Map.DynamoDbEntityInfo __Map.IDynamoDbEntity<global::Shop.Order>.Info => __Info;

    static void __Map.IDynamoDbEntity<global::Shop.Order>.WriteKey(__Ser.DynamoDbWriter writer, global::Shop.Order entity)
        => __WriteKey(writer, entity);

    static void __Map.IDynamoDbDocument<global::Shop.Order>.WriteAttributes(__Ser.DynamoDbWriter writer, global::Shop.Order entity)
    {
        __WriteKey(writer, entity);

        writer.WriteAttributeName("gsi1pk"u8);
        Keys.Gsi1Pk.Write(writer, entity.Status);
        writer.WriteAttributeName("gsi1sk"u8);
        Keys.Gsi1Sk.Write(writer, entity.Total);

        // Sparse: each attribute only when all its inputs are present.
        if (entity.TrackingId is { } __k0)
        {
            writer.WriteAttributeName("gsi2pk"u8);
            Keys.Gsi2Pk.Write(writer, __k0);
        }

        if (entity.ShippedAt is { } __k1)
        {
            writer.WriteAttributeName("gsi2sk"u8);
            Keys.Gsi2Sk.Write(writer, __k1);
        }

        writer.WriteAttributeName("_type"u8);
        writer.WriteString("ORDER"u8);

        writer.WriteAttributeName("UserId"u8);
        __Ser.GuidConverter.Write(writer, entity.UserId);
        writer.WriteAttributeName("CreatedAt"u8);
        __Ser.DateTimeOffsetConverter.Write(writer, entity.CreatedAt);
        writer.WriteAttributeName("OrderId"u8);
        __Ser.GuidConverter.Write(writer, entity.OrderId);
        writer.WriteAttributeName("Status"u8);
        Shop_OrderStatusConverter.Write(writer, entity.Status);
        writer.WriteAttributeName("Total"u8);
        __Ser.NumberConverter<decimal>.Write(writer, entity.Total);

        if (entity.TrackingId is { } __v0)
        {
            writer.WriteAttributeName("TrackingId"u8);
            __Ser.GuidConverter.Write(writer, __v0);
        }

        if (entity.ShippedAt is { } __v1)
        {
            writer.WriteAttributeName("ShippedAt"u8);
            __Ser.DateTimeOffsetConverter.Write(writer, __v1);
        }

        if (entity.ShipTo is { } __v2)
        {
            writer.WriteAttributeName("ShipTo"u8);
            __Ser.DocumentConverter<global::Shop.Address>.Write(writer, __v2);
        }

        writer.WriteAttributeName("Lines"u8);
        __Ser.ReadOnlyListConverter<global::Shop.LineItem, __Ser.DocumentConverter<global::Shop.LineItem>>.Write(writer, entity.Lines);

        // DynamoDB rejects empty sets, so an empty set is left out.
        if (entity.Tags is { Count: > 0 } __v3)
        {
            writer.WriteAttributeName("Tags"u8);
            __Ser.HashSetConverter<string, __Ser.StringSetElement>.Write(writer, __v3);
        }

        if (entity.ExpiresAt is { } __v4)
        {
            writer.WriteAttributeName("ttl"u8);
            global::DynaSharp.Serialization.EpochSecondsConverter.Write(writer, __v4);
        }

        writer.WriteAttributeName("Revision"u8);
        __Ser.NumberConverter<long>.Write(writer, entity.Revision);
    }

    static global::Shop.Order __Map.IDynamoDbDocument<global::Shop.Order>.ReadAttributes(ref __Ser.DynamoDbReader reader)
    {
        __Fields fields = default;
        string? attribute = null;
        try
        {
            while (reader.TryReadAttributeName())
            {
                var name = reader.GetAttributeNameUtf8();
                switch (name.Length)
                {
                    case 3:
                        if (global::System.MemoryExtensions.SequenceEqual(name, "ttl"u8))
                        {
                            attribute = "ttl";
                            fields.ExpiresAt = reader.ReadAttribute<global::System.DateTimeOffset?, __Ser.NullableConverter<global::System.DateTimeOffset, global::DynaSharp.Serialization.EpochSecondsConverter>>();
                            fields.ExpiresAt__set = true;
                            continue;
                        }

                        break;
                    case 4:
                        if (global::System.MemoryExtensions.SequenceEqual(name, "Tags"u8))
                        {
                            attribute = "Tags";
                            fields.Tags = reader.ReadAttribute<global::System.Collections.Generic.HashSet<string>, __Ser.HashSetConverter<string, __Ser.StringSetElement>>();
                            fields.Tags__set = true;
                            continue;
                        }

                        break;
                    case 5:
                        if (global::System.MemoryExtensions.SequenceEqual(name, "Lines"u8))
                        {
                            attribute = "Lines";
                            fields.Lines = reader.ReadAttribute<global::System.Collections.Generic.IReadOnlyList<global::Shop.LineItem>, __Ser.ReadOnlyListConverter<global::Shop.LineItem, __Ser.DocumentConverter<global::Shop.LineItem>>>();
                            fields.Lines__set = true;
                            continue;
                        }

                        if (global::System.MemoryExtensions.SequenceEqual(name, "Total"u8))
                        {
                            attribute = "Total";
                            fields.Total = reader.ReadAttribute<decimal, __Ser.NumberConverter<decimal>>();
                            fields.Total__set = true;
                            continue;
                        }

                        break;
                    case 6:
                        if (global::System.MemoryExtensions.SequenceEqual(name, "ShipTo"u8))
                        {
                            attribute = "ShipTo";
                            fields.ShipTo = reader.ReadAttribute<global::Shop.Address?, __Ser.NullableReferenceConverter<global::Shop.Address, __Ser.DocumentConverter<global::Shop.Address>>>();
                            fields.ShipTo__set = true;
                            continue;
                        }

                        if (global::System.MemoryExtensions.SequenceEqual(name, "Status"u8))
                        {
                            attribute = "Status";
                            fields.Status = reader.ReadAttribute<global::Shop.OrderStatus, Shop_OrderStatusConverter>();
                            fields.Status__set = true;
                            continue;
                        }

                        if (global::System.MemoryExtensions.SequenceEqual(name, "UserId"u8))
                        {
                            attribute = "UserId";
                            fields.UserId = reader.ReadAttribute<global::System.Guid, __Ser.GuidConverter>();
                            fields.UserId__set = true;
                            continue;
                        }

                        break;
                    // case 7 OrderId, case 8 Revision, case 9 CreatedAt and ShippedAt, case 10 TrackingId: same shape.
                }

                // pk, sk, the index keys, _type and unknown attributes: no string, no value parsed.
                reader.SkipAttributeValue();
            }
        }
        catch (global::System.Exception exception) when (__Map.DynamoDbMappingException.IsConversionFailure(exception))
        {
            throw new __Map.DynamoDbMappingException("Order", attribute, exception);
        }

        if (!fields.UserId__set) { throw __Map.DynamoDbMappingException.Missing("Order", "UserId"); }
        if (!fields.CreatedAt__set) { throw __Map.DynamoDbMappingException.Missing("Order", "CreatedAt"); }
        if (!fields.OrderId__set) { throw __Map.DynamoDbMappingException.Missing("Order", "OrderId"); }
        if (!fields.Status__set) { throw __Map.DynamoDbMappingException.Missing("Order", "Status"); }
        if (!fields.Total__set) { throw __Map.DynamoDbMappingException.Missing("Order", "Total"); }
        return new global::Shop.Order(in fields);
    }

    private static void __WriteKey(__Ser.DynamoDbWriter writer, global::Shop.Order entity)
    {
        writer.WriteAttributeName("pk"u8);
        Keys.Pk.Write(writer, entity.UserId);
        writer.WriteAttributeName("sk"u8);
        Keys.Sk.Write(writer, entity.CreatedAt, entity.OrderId);
    }

    private struct __Fields
    {
        public global::System.Guid UserId; public bool UserId__set;
        public global::System.DateTimeOffset CreatedAt; public bool CreatedAt__set;
        public global::System.Guid OrderId; public bool OrderId__set;
        public global::Shop.OrderStatus Status; public bool Status__set;
        public decimal Total; public bool Total__set;
        public global::System.Guid? TrackingId; public bool TrackingId__set;
        public global::System.DateTimeOffset? ShippedAt; public bool ShippedAt__set;
        public global::Shop.Address? ShipTo; public bool ShipTo__set;
        public global::System.Collections.Generic.IReadOnlyList<global::Shop.LineItem> Lines; public bool Lines__set;
        public global::System.Collections.Generic.HashSet<string> Tags; public bool Tags__set;
        public global::System.DateTimeOffset? ExpiresAt; public bool ExpiresAt__set;
        public long Revision; public bool Revision__set;
    }

    /// <summary>The key builders of <see cref="Order"/>.</summary>
    public static class Keys
    {
        /// <summary>Creates the primary key of an order.</summary>
        public static __Map.EntityKey<global::Shop.Order> Of(global::System.Guid userId, global::System.DateTimeOffset createdAt, global::System.Guid orderId) =>
            new(global::DynaSharp.Model.AttributeValue.FromString(Pk.Format(userId)),
                global::DynaSharp.Model.AttributeValue.FromString(Sk.Format(createdAt, orderId)));

        /// <summary>The placeholders of a parsed <c>sk</c>.</summary>
        public readonly record struct SkParts(global::System.DateTimeOffset CreatedAt, global::System.Guid OrderId);

        /// <summary>Builds and parses the sort key template <c>ORDER#{CreatedAt}#{OrderId}</c>.</summary>
        public static class Sk
        {
            private const int BufferLength = 67; // "ORDER#" 6, ms 24, "#" 1, Guid 36

            /// <summary>Formats the complete key.</summary>
            public static string Format(global::System.DateTimeOffset createdAt, global::System.Guid orderId)
            {
                var __key = new __Key.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, createdAt, orderId);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>The prefix up to the first placeholder, for <c>begins_with</c>.</summary>
            public static string Prefix() => "ORDER#";

            /// <summary>The prefix through <c>CreatedAt</c>, for <c>begins_with</c>. Ends on the delimiter.</summary>
            public static string Prefix(global::System.DateTimeOffset createdAt)
            {
                var __key = new __Key.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    __key.WriteLiteral("ORDER#"u8);
                    __key.WriteTimestamp(createdAt, __Key.KeyTimeFormat.Milliseconds);
                    __key.WriteLiteral("#"u8);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Parses a key.</summary>
            /// <exception cref="global::System.FormatException">The text does not match the template.</exception>
            public static SkParts Parse(string text) =>
                TryParse(text, out var parts) ? parts : throw new global::System.FormatException("The text is not a sort key of Order.");

            /// <summary>Tries to parse a key.</summary>
            public static bool TryParse(string text, out SkParts parts) =>
                TryParse(global::System.Text.Encoding.UTF8.GetBytes(text), out parts);

            /// <summary>Tries to parse a key given as UTF-8.</summary>
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out SkParts parts)
            {
                parts = default;
                var __key = new __Key.KeyReader(utf8Text);
                if (__key.TryReadLiteral("ORDER#"u8)
                    && __key.TryReadTimestamp(__Key.KeyTimeFormat.Milliseconds, out global::System.DateTimeOffset createdAt)
                    && __key.TryReadLiteral("#"u8)
                    && __key.TryReadGuid(out var orderId)
                    && __key.IsAtEnd)
                {
                    parts = new SkParts(createdAt, orderId);
                    return true;
                }

                return false;
            }

            internal static void Write(__Ser.DynamoDbWriter writer, global::System.DateTimeOffset createdAt, global::System.Guid orderId)
            {
                var __key = new __Key.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, createdAt, orderId);
                    writer.WriteString(__key.WrittenSpan);
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static void Encode(ref __Key.KeyWriter __key, global::System.DateTimeOffset createdAt, global::System.Guid orderId)
            {
                __key.WriteLiteral("ORDER#"u8);
                __key.WriteTimestamp(createdAt, __Key.KeyTimeFormat.Milliseconds);
                __key.WriteLiteral("#"u8);
                __key.WriteGuid(orderId);
            }
        }

        // Pk, Gsi1Pk, Gsi1Sk, Gsi2Pk, Gsi2Sk: same shape. Prefix builders on sort keys only (begins_with exists only there).
        // Gsi1Pk.Encode: WriteLiteral("STATUS#"u8); WriteUtf8Value(Shop_OrderStatusConverter.GetName(status), default);
        // Gsi1Sk.Encode: WriteFixedPoint(total, 10, 2);
    }
}

file readonly struct Shop_OrderStatusConverter : __Ser.IDynamoDbConverter<global::Shop.OrderStatus>
{
    public static global::System.ReadOnlySpan<byte> GetName(global::Shop.OrderStatus value) => value switch
    {
        global::Shop.OrderStatus.Pending => "Pending"u8,
        global::Shop.OrderStatus.Paid => "Paid"u8,
        global::Shop.OrderStatus.Shipped => "Shipped"u8,
        _ => throw __Map.DynamoDbMappingException.UndefinedEnumValue("OrderStatus"),
    };

    public static void Write(__Ser.DynamoDbWriter writer, global::Shop.OrderStatus value) => writer.WriteString(GetName(value));

    public static global::Shop.OrderStatus Read(ref __Ser.DynamoDbReader reader, global::DynaSharp.Model.AttributeType type)
    {
        __Ser.ConverterChecks.Expect(type, global::DynaSharp.Model.AttributeType.String);
        var name = reader.GetStringUtf8();
        switch (name.Length)
        {
            case 4:
                if (global::System.MemoryExtensions.SequenceEqual(name, "Paid"u8)) { return global::Shop.OrderStatus.Paid; }
                break;
            case 7:
                if (global::System.MemoryExtensions.SequenceEqual(name, "Pending"u8)) { return global::Shop.OrderStatus.Pending; }
                if (global::System.MemoryExtensions.SequenceEqual(name, "Shipped"u8)) { return global::Shop.OrderStatus.Shipped; }
                break;
        }

        throw __Map.DynamoDbMappingException.UnknownEnumName("OrderStatus");
    }
}
```

**`Shop.Address.g.cs`:** when every property is bound to the constructor there is no `__Fields` constructor; `ReadAttributes` ends with this:

```csharp
if (!fields.Street__set) { throw __Map.DynamoDbMappingException.Missing("Address", "Street"); }
if (!fields.City__set) { throw __Map.DynamoDbMappingException.Missing("Address", "City"); }
return new global::Shop.Address(fields.Street, fields.City, fields.Zip);
```

**Why it has this shape**

- **Private `__Fields` constructor.** This is the central decision. A constructor in the type may assign `init` properties conditionally, so one mechanism covers `init`, `required` (through `[SetsRequiredMembers]`), private setters and property initializers (`= []` stays when the attribute is missing). An object initializer would have to assign every `init` property unconditionally and would lose those defaults.
- **`public Order() { }` is emitted only when the type had nothing but the implicit constructor.** Otherwise the generated constructor would remove it.
- **`global::System.MemoryExtensions.SequenceEqual` is fully qualified.** Consumers may have implicit usings off, and an extension-method lookup could collide.
- **Pattern variables in `if` conditions leak into the enclosing scope,** so local names are indexed (`__v0`).
- **`this.X =` is always qualified,** so a property named `fields` cannot collide. The local `__key` cannot collide with a parameter for a property `Key`.
- **`stackalloc` lives only in small leaf methods.** It never sits in a loop, and those methods are not inlined.
- **Length group ordering is ordinal,** for deterministic snapshots.

## 5. Runtime surface in `DynaSharp`

### 5.1 Namespaces

Add to the Architecture table in `requirements.md`:

- `DynaSharp.Mapping`: attributes, `IDynamoDbDocument<T>`, `IDynamoDbEntity<T>`, `EntityKey<T>`, `DynamoDbEntityInfo`, `DynamoDbIndexInfo`, `DynamoDbMappingException`.
- `DynaSharp.Keys`: `KeyWriter`, `KeyReader`, `KeyTimeFormat`.

Converters stay in `DynaSharp.Serialization`.

### 5.2 Interfaces

```csharp
namespace DynaSharp.Mapping;

public interface IDynamoDbDocument<TSelf> where TSelf : IDynamoDbDocument<TSelf>
{
    /// Writes attribute names and values, without the surrounding braces, so the same code serves an item and a map.
    public static abstract void WriteAttributes(DynamoDbWriter writer, TSelf entity);

    /// Reads attributes after the opening brace, up to and including the closing one.
    public static abstract TSelf ReadAttributes(ref DynamoDbReader reader);
}

public interface IDynamoDbEntity<TSelf> : IDynamoDbDocument<TSelf> where TSelf : IDynamoDbEntity<TSelf>
{
    public static abstract DynamoDbEntityInfo Info { get; }

    /// Writes the primary key attributes, without the surrounding braces.
    public static abstract void WriteKey(DynamoDbWriter writer, TSelf entity);
}

public sealed class DynamoDbEntityInfo(string discriminator, string partitionKeyName, string? sortKeyName,
    IReadOnlyList<DynamoDbIndexInfo> indexes, string? versionAttributeName) { /* get-only properties */ }

public sealed record DynamoDbIndexInfo(string IndexName, string PartitionKeyName, string? SortKeyName, bool Sparse);

public readonly struct EntityKey<T>(AttributeValue partitionKey, AttributeValue sortKey = default)
    where T : IDynamoDbEntity<T>
{
    public AttributeValue PartitionKey { get; } = partitionKey;
    public AttributeValue SortKey { get; } = sortKey;   // AttributeType.None when the table has no sort key
}
```

Implementations are explicit interface members, so nothing lands on the user's public type surface. Interface additions are not a compatibility issue, because the generator and the interface ship together in one package. M3 adds version access and handles then.

The read and write methods work without braces because of the existing reader contract: `ReadStartItem`, `TryReadStartItem` and `ReadStartAttribute` on a map all stop just after `{`, and `TryReadAttributeName` consumes the closing `}`. One method therefore serves top-level items, array elements and nested maps.

### 5.3 Attributes (all public, in `DynaSharp.Mapping`)

| Attribute | Targets | Notes |
|---|---|---|
| `DynamoDbEntityAttribute` | class, struct | |
| `DynamoDbDocumentAttribute` | class, struct | Nested types, no keys |
| `PartitionKeyAttribute`, `SortKeyAttribute` | class, struct, property | With template on type, plain on property. `AttributeName` property. |
| `GlobalIndexKeyAttribute(string indexName)` | class, struct, `AllowMultiple` | `PartitionKey`, `SortKey`, `Sparse`, `PartitionKeyAttributeName`, `SortKeyAttributeName` |
| `DynamoDbAttributeAttribute(string name)` | property | Also forces a computed or non-public property to be stored |
| `DynamoDbIgnoreAttribute` | property | |
| `DynamoDbConverterAttribute(Type)` | property, class, struct, enum | On a type it applies everywhere, including collections |
| `DynamoDbConstructorAttribute` | constructor | Resolves ambiguity |
| `DiscriminatorAttribute(string)` | class, struct | |
| `VersionAttribute` | property | Metadata in M2, enforced in M3 |

A GSI key over an existing attribute needs no new attribute. With a template of exactly one bare placeholder and `PartitionKeyAttributeName` equal to that property's attribute name, nothing extra is written, the property's own attribute is the index key, and an N-typed key works.

LSIs are not in M2.

### 5.4 Reader additions, and the name matching contract

```csharp
// DynamoDbReader
public readonly ReadOnlySpan<byte> GetAttributeNameUtf8() { /* throw InvalidOperationException unless on a PropertyName */ return ValueUtf8(); }
public readonly ReadOnlySpan<byte> GetStringUtf8()        { /* throw unless on a String */ return ValueUtf8(); }

public T ReadAttribute<T, TConverter>() where TConverter : IDynamoDbConverter<T>
{
    var type = ReadStartAttribute();
    var value = TConverter.Read(ref this, type);
    ReadEndAttribute();
    return value;
}

private readonly ReadOnlySpan<byte> ValueUtf8() => _json.ValueIsEscaped ? Unescape() : _json.ValueSpan;

// Escaped names and strings are rare, so one array beats a scratch buffer that every caller would have to provide.
private readonly byte[] Unescape()
{
    var bytes = new byte[_json.ValueSpan.Length];
    return bytes[.._json.CopyString(bytes)];
}
```

No stackalloc is needed in generated code, and known names cost no string in the normal case. Attribute names can reach 64 KB, so a fixed scratch buffer would need a fallback anyway.

Duplicate names in generated reads: last wins. `AttributeValueConverter` throws here; a check would cost tracking per attribute, and DynamoDB never sends duplicates.

### 5.5 Built-in converters

All are `public readonly struct` in `DynaSharp.Serialization`, not classes. A struct type argument gives every instantiation its own code, so `TConverter.Write` is a direct, inlinable call. With a class, the instantiation would be shared and go through a runtime lookup. Custom converters may still be classes.

| Type | Converter | Wire | Notes |
|---|---|---|---|
| `string` | `StringConverter` | S | |
| `bool` | `BoolConverter` | BOOL | |
| all `INumberBase<T>`: integers, `decimal`, `double`, `float`, `Half`, `Int128`, `UInt128`, `BigInteger` | `NumberConverter<T>` | N | Parses from `ValueSpan`, allocates nothing |
| `Guid` | `GuidConverter` | S | Lowercase D through a stack buffer |
| `DateTimeOffset` | `DateTimeOffsetConverter` | S | Converted to UTC, fixed `yyyy-MM-ddTHH:mm:ss.fffffffZ` (format `O` on the UTC value). The offset is not preserved, which is what makes stored values compare chronologically. Document it. |
| `DateTime` | `DateTimeConverter` | S | `Utc` as is, `Local` converted, `Unspecified` throws, matching the key grammar. Reads return `Utc`. |
| `DateOnly`, `TimeOnly` | `DateOnlyConverter`, `TimeOnlyConverter` | S | |
| `TimeSpan` | `TimeSpanConverter` | S | `c` format; not order-preserving |
| `byte[]`, `ReadOnlyMemory<byte>` | `BinaryConverter`, `ReadOnlyMemoryConverter` | B | |
| `T?` (value) | `NullableConverter<T, TC>` | inner or NULL | |
| `T?` (reference) | `NullableReferenceConverter<T, TC>` | inner or NULL | |
| `T[]`, `List<T>`, `IReadOnlyList<T>`, `IList<T>` | `ArrayConverter`, `ListConverter`, `ReadOnlyListConverter`, `MutableListConverter` (each `<T, TC>`) | L | Read through a shared internal `ElementReader<T, TC>` over `ArrayBuilder<T>`. `IEnumerable<T>`, `ICollection<T>` and `IReadOnlyCollection<T>` get DYNA003, suggesting `IReadOnlyList<T>`. |
| `HashSet<T>`, `IReadOnlySet<T>` | `HashSetConverter<T, TE>`, `ReadOnlySetConverter<T, TE>` | SS, NS, BS | |
| `T[]` as a set, opt-in | `SetArrayConverter<T, TE>` through `[DynamoDbConverter]` | SS, NS, BS | Lighter: `string[]` of 3 is 48 B against about 176 B for a `HashSet` |
| `Dictionary<string, T>`, `IReadOnlyDictionary<string, T>` | `DictionaryConverter<T, TC>`, `ReadOnlyDictionaryConverter<T, TC>` | M | String keys only |
| `AttributeValue` | `AttributeValueConverter` (made public) | any | |
| document or entity | `DocumentConverter<T>` | M | |
| TTL | `EpochSecondsConverter` | N | `DateTimeOffset`, by attribute |
| enum | generated `file` converter | S; N for `[Flags]` | Number storage for `[Flags]`, since combinations have no name |

```csharp
public interface IDynamoDbSetElementConverter<T>
{
    public static abstract AttributeType SetType { get; }      // StringSet, NumberSet or BinarySet
    public static abstract void WriteElement(DynamoDbWriter writer, T value);
    public static abstract T ReadElement(ref DynamoDbReader reader);
}
// StringSetElement, NumberSetElement<T>, BinarySetElement, GuidSetElement.
// Generated enum converters implement it too, so HashSet<Role> works.
```

**Representative code**

```csharp
public readonly struct DocumentConverter<T> : IDynamoDbConverter<T>
    where T : IDynamoDbDocument<T>
{
    public static void Write(DynamoDbWriter writer, T value)
    {
        writer.WriteStartMap();
        T.WriteAttributes(writer, value);
        writer.WriteEndMap();
    }

    public static T Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Map);
        return T.ReadAttributes(ref reader);
    }
}

public readonly struct ListConverter<T, TConverter> : IDynamoDbConverter<List<T>>
    where TConverter : IDynamoDbConverter<T>
{
    public static void Write(DynamoDbWriter writer, List<T> value)
    {
        writer.WriteStartList();
        foreach (var element in value)
        {
            TConverter.Write(writer, element);
        }

        writer.WriteEndList();
    }

    public static List<T> Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    // The pool is a parameter so a counting pool can prove every rented array comes back, as in AttributeValueConverter.
    internal static List<T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool)
    {
        ConverterChecks.Expect(type, AttributeType.List);
        var elements = new ArrayBuilder<T>(pool);
        try
        {
            while (reader.TryReadStartAttribute(out var elementType))
            {
                elements.Add(TConverter.Read(ref reader, elementType));
                reader.ReadEndAttribute();
            }

            var list = new List<T>(elements.Count);
            list.AddRange(elements.WrittenSpan);
            return list;
        }
        finally
        {
            elements.Dispose();
        }
    }
}
```

**AOT and allocations**

- Every instantiation is closed and visible in generated source, with no `MakeGenericType`.
- `ListConverter<Tree, DocumentConverter<Tree>>` is finite, because recursion goes through the entity's non-generic method rather than a growing generic type.
- `ReadOnlyListConverter.Write` tests for `T[]` and `List<T>`, then falls back to an indexed loop, so no boxed enumerator.
- Reads allocate exactly-sized results.

### 5.6 Converter selection (first match wins)

1. `[DynamoDbConverter]` on the property.
2. `[DynamoDbConverter]` on the property's type.
3. `AttributeValue`.
4. Exact built-in scalar.
5. Enum: generated converter.
6. `Nullable<T>` or a nullable reference: resolve the inner type, then wrap for reading.
7. A type with `[DynamoDbEntity]` or `[DynamoDbDocument]` in this compilation, or implementing `IDynamoDbDocument<T>` from a reference: `DocumentConverter<T>`. The attribute check is needed because the generated interface is not visible in the semantic model of the same compilation.
8. Collections, recursing on the element.
9. Otherwise DYNA003.

A custom converter on `DateTimeOffset?` must implement `IDynamoDbConverter<DateTimeOffset>`; the generator adds the null handling.

### 5.7 Type shapes and member rules

| Topic | Rule |
|---|---|
| Declaration | `partial` required on the type and every containing type |
| Kinds | `class`, `record`, `struct`, `record struct`, `readonly` variants. Rejected: abstract, generic, `ref struct`, interface. |
| Inheritance | Base properties are included. Each `[DynamoDbEntity]` type implements its own interface. Polymorphic reads are M3. |
| Constructor choice | `[DynamoDbConstructor]`, else parameterless of any accessibility, else the single constructor excluding the record copy constructor, else DYNA005. Private is fine: the generated code is inside the type. |
| Parameter binding | By property name, case-insensitive. A parameter with no property is DYNA006. |
| Mapped properties | Public, instance, readable, and either settable (`set`, `init`, also private) or bound to a constructor parameter. |
| Get-only computed | Not stored unless `[DynamoDbAttribute]`, then write-only. Still usable as a template input. |
| Excluded | Fields, indexers, statics, `[DynamoDbIgnore]`, `EqualityContract` |

**Missing and unknown attributes on read**

| Situation | Result |
|---|---|
| Unknown attribute (including `pk`, `sk`, GSI keys, `_type`) | Skipped, no allocation, no value parse |
| Missing, `required` or non-nullable constructor parameter without default | `DynamoDbMappingException` |
| Missing, constructor parameter with default | Default literal, from `ExplicitDefaultValue` |
| Missing, non-nullable set | Empty set, even if `required`. DynamoDB cannot store an empty set, so missing means empty and round trips hold. |
| Missing, anything else | Property keeps what the constructor or initializer left, for `set` and `init` alike |
| `{"NULL":true}` on a nullable | `null` |
| `{"NULL":true}` on a non-nullable | `DynamoDbMappingException` naming the attribute |
| Wrong DynamoDB type | `DynamoDbMappingException` naming the attribute |

A `required` member under a `ProjectionExpression` or a `KEYS_ONLY` index throws. The rule is to use a projection type; document it.

### 5.8 Mapping failures

```csharp
public sealed class DynamoDbMappingException : Exception
{
    public DynamoDbMappingException(string entityType, string? attributeName, Exception? innerException = null);
    public string EntityType { get; }
    public string? AttributeName { get; }

    public static bool IsConversionFailure(Exception exception) =>
        exception is JsonException or FormatException or OverflowException or InvalidOperationException;

    public static DynamoDbMappingException Missing(string entityType, string attributeName);
    // UnknownEnumName, UndefinedEnumValue
}
```

- It derives from `Exception`, so the `Parse` filter in the pipeline leaves it alone, and `FailureClassifier` maps it to `Permanent` through its `_` arm. No pipeline change is needed; add tests that pin both.
- Its message has no values.
- A `FormatException` inner exception can quote the value. Telemetry reads only the exception type, so the telemetry rule holds, but applications that log `exception.ToString()` could see it. Note that in the docs.
- Nested documents report the innermost failure; the outer `catch` passes it through.

### 5.9 Key helper

```csharp
namespace DynaSharp.Keys;

public enum KeyTimeFormat { Year, Month, Day, Hour, Second, Millisecond, Microsecond, Ticks }

public ref struct KeyWriter
{
    private Span<byte> _buffer;
    private byte[]? _rented;
    private int _length;

    public KeyWriter(Span<byte> buffer) { _buffer = buffer; _rented = null; _length = 0; }

    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[.._length];

    public void WriteLiteral(scoped ReadOnlySpan<byte> utf8Literal);
    public void WriteString(string value, scoped ReadOnlySpan<byte> delimiters);
    public void WriteUtf8Value(scoped ReadOnlySpan<byte> utf8Value, scoped ReadOnlySpan<byte> delimiters);
    public void WriteGuid(Guid value);
    public void WriteInteger(long value, scoped ReadOnlySpan<byte> delimiters);
    public void WriteInteger(ulong value, scoped ReadOnlySpan<byte> delimiters);
    public void WriteDigits(long value, int width);                 // d<w>
    public void WriteDigits(ulong value, int width);
    public void WriteDecimal(decimal value, scoped ReadOnlySpan<byte> delimiters);
    public void WriteFixedPoint(decimal value, int integerDigits, int fractionDigits); // n<i>.<f>, n<i> with 0
    public void WriteTimestamp(DateTimeOffset value, KeyTimeFormat format);
    public void WriteTimestamp(DateTime value, KeyTimeFormat format);
    public void WriteDate(DateOnly value);
    public void WriteFormattable<T>(T value, scoped ReadOnlySpan<byte> delimiters) where T : IUtf8SpanFormattable;
    public override readonly string ToString();
    public void Dispose();
}

public ref struct KeyReader   // over ReadOnlySpan<byte>
{
    public readonly bool IsAtEnd { get; }
    public bool TryReadLiteral(scoped ReadOnlySpan<byte> utf8Literal);
    public bool TryReadString(byte? delimiter, out string value);
    public bool TryReadUtf8Value(byte? delimiter, out ReadOnlySpan<byte> value);
    public bool TryReadGuid(out Guid value);
    public bool TryReadInt64(byte? delimiter, out long value);
    public bool TryReadUInt64(byte? delimiter, out ulong value);
    public bool TryReadDigits(int width, out ulong value);
    public bool TryReadFixedPoint(int integerDigits, int fractionDigits, out decimal value);
    public bool TryReadDecimal(byte? delimiter, out decimal value);
    public bool TryReadTimestamp(KeyTimeFormat format, out DateTimeOffset value);
    public bool TryReadTimestamp(KeyTimeFormat format, out DateTime value);
    public bool TryReadDate(out DateOnly value);
    public bool TryReadParsable<T>(byte? delimiter, out T value) where T : IUtf8SpanParsable<T>;
}
```

| Input | Default call | Formats |
|---|---|---|
| `string` | `WriteString` | none |
| `Guid` | `WriteGuid`, 36 | none |
| enum | `WriteUtf8Value(GetName(v), ...)` | `d<w>`: `WriteDigits` |
| integers up to `long`/`ulong` | `WriteInteger`, also `g` | `d<w>`: `WriteDigits`; `n<i>`: `WriteFixedPoint(v, i, 0)`, the implicit conversion to `decimal` being exact |
| `decimal` | `WriteDecimal`, also `g` | `n<i>.<f>`: `WriteFixedPoint` |
| `DateTimeOffset`, `DateTime` | `WriteTimestamp(ms)` | all `KeyTimeFormat` values |
| `DateOnly` | `WriteDate`, 10 | none |
| `IUtf8SpanFormattable` + `IUtf8SpanParsable<T>` | `WriteFormattable` | none |

`Int128`, `UInt128` and `BigInteger` are rejected as key inputs by DYNA103: the table in `requirements.md` says "integers", and `long`/`ulong` keeps the encoders non-generic. Say so there.

**The two encodings that need care**

```csharp
public void WriteFixedPoint(decimal value, int integerDigits, int fractionDigits)
{
    // -0.00m compares equal to 0m, so negative zero encodes as P.
    var negative = value < 0m;
    var magnitude = Math.Abs(value);
    if (decimal.Round(magnitude, fractionDigits) != magnitude)
    {
        throw new ArgumentOutOfRangeException(nameof(value), "A key value has more fraction digits than its format.");
    }

    Span<char> format = ['F', (char)('0' + (fractionDigits / 10)), (char)('0' + (fractionDigits % 10))];
    Span<byte> text = stackalloc byte[MaxDecimalText];
    magnitude.TryFormat(text, out var textLength, format, CultureInfo.InvariantCulture);

    var written = fractionDigits == 0 ? textLength : textLength - fractionDigits - 1;
    if (written > integerDigits)
    {
        throw new ArgumentOutOfRangeException(nameof(value), "A key value is out of the range of its format.");
    }

    var total = 1 + integerDigits + (fractionDigits == 0 ? 0 : 1 + fractionDigits);
    var destination = Reserve(total)[..total];
    destination[0] = negative ? (byte)'N' : (byte)'P';
    var padding = integerDigits - written;
    destination.Slice(1, padding).Fill((byte)'0');
    text[..textLength].CopyTo(destination[(1 + padding)..]);
    if (negative)
    {
        foreach (ref var digit in destination[1..])
        {
            // Nine's complement: larger magnitudes sort first.
            if (digit != (byte)'.')
            {
                digit = (byte)('9' - (digit - '0'));
            }
        }
    }

    _length += total;
}

private void WriteUtc(DateTime utc, KeyTimeFormat format)
{
    // "O" on a UTC value is yyyy-MM-ddTHH:mm:ss.fffffffZ. Slicing it truncates, which is what makes {At:yyyy-MM} a bucket.
    Span<byte> text = stackalloc byte[RoundTripLength];
    utc.TryFormat(text, out _, "O", CultureInfo.InvariantCulture);
    var (prefix, zulu) = Layouts[(int)format];
    WriteLiteral(text[..prefix]);
    if (zulu)
    {
        WriteLiteral("Z"u8);
    }
}
```

Widths: `yyyy` 4, `yyyy-MM` 7, `yyyy-MM-dd` 10, `yyyy-MM-ddTHH` 13, `s` 20, `ms` 24, `us` 27, `ticks` 28. `Z` only on `s` and finer, as the format names say.

**Rules for the implementer**

- `WriteString` uses `Utf8.FromUtf16(..., replaceInvalidSequences: false)` and throws on `InvalidData`. Otherwise a lone surrogate silently becomes U+FFFD and two different keys collide.
- Empty value and delimiter checks run after encoding. Delimiters are ASCII and UTF-8 continuation bytes never equal ASCII, so `IndexOfAny` on bytes is correct.
- Exception messages must not contain the value.
- Timestamp parsing transcodes with `Ascii.ToUtf16` on the stack and uses `TryParseExact`; `DateOnly` has no UTF-8 `TryParse` in net11's reference assembly. `Guid.TryParse(ReadOnlySpan<byte>)` exists.

### 5.10 Other existing-contract changes

Beyond the reader additions in 5.4:

- `AttributeValueConverter` becomes `public`. `WriteItem`, `ReadItem` and `ReadAttributes` change from `public` to `internal`.
- `AttributeValue.From<T, TConverter>(T value)`, new and public: serializes through the converter and reads back. `Keys.Of` uses it for plain keys that are neither `string` nor a number, and M3 needs the same thing for condition values.
- `ArrayBuilder<T>` gets `WrittenSpan`.
- The `IDynamoDbConverter<T>` docs: allow structs, and replace the example, since `EpochSecondsConverter` is now built in. The test double of that name in `tests/.../Serialization` stays valid because its namespace wins.

## 6. Connecting entities to the client without the fluent API

Typed overloads on `DynamoDbClient`, public, as the layer M3 builds on. Operation names and telemetry are identical to the low-level calls, so spans and metrics stay one series.

```csharp
public Task<GetItemResponse<T>> GetItemAsync<T>(GetItemRequest<T> request, CancellationToken cancellationToken = default)
    where T : IDynamoDbEntity<T>;
public Task<PutItemResponse<T>> PutItemAsync<T>(PutItemRequest<T> request, CancellationToken cancellationToken = default)
    where T : IDynamoDbEntity<T>;
public Task<QueryResponse<T>> QueryAsync<T>(QueryRequest request, CancellationToken cancellationToken = default)
    where T : IDynamoDbDocument<T>;
public Task<ScanResponse<T>> ScanAsync<T>(ScanRequest request, CancellationToken cancellationToken = default)
    where T : IDynamoDbDocument<T>;
```

```csharp
var key = Order.Keys.Of(userId, createdAt, orderId);
var got = await client.GetItemAsync(new GetItemRequest<Order> { TableName = "app", Key = key });   // T is inferred
await client.PutItemAsync(new PutItemRequest<Order> { TableName = "app", Item = order });
var page = await client.QueryAsync<Order>(queryRequest);                                          // existing QueryRequest
```

- `Query` and `Scan` reuse the existing request types, including `ExclusiveStartKey`. Only the response changes.
- `GetItemResponse<T>` and `PutItemResponse<T>` carry `T? Item` or `T? Attributes` plus a `bool Found`, because `T?` on a struct entity cannot express "no item". Use `[MemberNotNullWhen]`.
- Why an `EntityKey<T>` holding two `AttributeValue`s, rather than a generated key struct: C# cannot infer `T` from a constraint on `TKey`, so a key struct would force `GetItemAsync<Order, Order.Key>`. Cost is two strings, about 200 B. Validation also happens at the call site, before any request exists.

```csharp
internal readonly struct GetItemOperation<T> : IOperation<GetItemRequest<T>, GetItemResponse<T>>
    where T : IDynamoDbEntity<T>
{
    public static string Name => "GetItem";

    public static string GetTableName(GetItemRequest<T> request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, GetItemRequest<T> request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        json.WritePropertyName("Key"u8);
        EntityKeyWriter.Write(writer, request.Key);
        GetItemJson.WriteOptions(writer, request.ConsistentRead, request.ProjectionExpression,
            request.ExpressionAttributeNames, request.ReturnConsumedCapacity);
        json.WriteEndObject();
    }

    public static GetItemResponse<T> ReadResponse(ref DynamoDbReader reader)
    {
        T? item = default;
        var found = false;
        ConsumedCapacity? consumedCapacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                reader.ReadStartItem();
                item = T.ReadAttributes(ref reader);
                found = true;
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new GetItemResponse<T> { Item = item, Found = found, ConsumedCapacity = consumedCapacity };
    }

    // SetActivityTags, GetConsumedCapacity: as in GetItemOperation.
}
```

- `PutItemOperation<T>.WriteRequest` does `writer.WriteStartItem(); T.WriteAttributes(writer, request.Item); writer.WriteEndItem();` and shares an option writer with `PutItemOperation`.
- `ItemPage<T>` and `ItemWriteResult<T>` sit beside the existing types.
- `OperationJson.ReadItems<T>(ref reader, ArrayPool<T> pool)` uses `ArrayBuilder<T>` and returns the exact `T[]` as `IReadOnlyList<T>`, without `Array.AsReadOnly`.
- `DynamoDbPipeline` and `IOperation` do not change.

**Allocation estimates**

These are calculated, not measured. The basis is the benchmark item's 10 attributes with 64-bit object sizes, reference `pk` and `sk` as strings on the entity.

| Part | Bytes |
|---|---|
| Entity object | 104 |
| `pk`, `sk`, `type`, `currency` strings | 256 |
| `tags` as `string[]` with 3 strings | 168 |
| Nested `address` with 3 strings | 168 |
| **Total per item** | **about 700** |
| Total with `HashSet<string>` tags | about 825 |
| With template keys, where `pk` and `sk` are skipped rather than read | minus 192 |

| Case | Today (measured, P6) | Generated (estimate) | Note |
|---|---|---|---|
| Per item in a page | 1.87 KB (2.5 KB before the P6 sizing) | 0.5-0.85 KB | About 2.2-3.7 times less |
| Query, 100 items | 187 KB | 60-90 KB | Includes about 6 KB fixed cost and the `T[]` |
| GetItem | 4.98 KB | 3.9-4.1 KB | About 3.1 KB is the HTTP call and does not move |
| PutItem | 2.49 KB | about 2.45 KB | Write path allocates nothing; the gain is hidden, because the benchmark reuses a prebuilt dictionary that costs real callers about 1.7 KB per call |

So Query is where the goal is won. Single-item calls are dominated by the HTTP and signing stack.

## 7. Diagnostics

Prefix `DYNA`. All are errors unless marked. Location is the type identifier unless stated. No code fixes in M2: a fix needs a Workspaces reference in a separate assembly, and diagnostics at external-file locations are not attached to a document. The last column says which are worth adding later.

| Id | Severity | Message | Location | Fix later |
|---|---|---|---|---|
| DYNA001 | Error | Type '{0}' must be partial, as must its containing types | Type | Yes |
| DYNA002 | Error | Entity '{0}' has no partition key. Add [PartitionKey], or use [DynamoDbDocument] for a nested type. | Type | No |
| DYNA003 | Error | Property '{0}' has unsupported type '{1}'. Add a [DynamoDbConverter] or use a supported type. | Property | No |
| DYNA004 | Error | Attribute name '{0}' is used by both '{1}' and '{2}' | Second property or key | No |
| DYNA005 | Error | Type '{0}' has no single usable constructor. Mark one with [DynamoDbConstructor]. | Type | No |
| DYNA006 | Error | Constructor parameter '{0}' has no matching mapped property | Parameter | No |
| DYNA007 | Error | Type '{0}' cannot be mapped: {1} (abstract, generic, ref struct) | Type | No |
| DYNA008 | Error | Converter '{0}' does not implement IDynamoDbConverter<{1}> or has no accessible definition | Attribute | No |
| DYNA009 | Error | Key '{0}' is declared more than once | Second declaration | No |
| DYNA010 | Error | Attribute name '{0}' is invalid: {1} (empty, too long, invalid UTF-16, reserved `_type`) | Attribute argument | No |
| DYNA011 | Error | [Version] requires a single integer property; '{0}' is not valid | Property | No |
| DYNA012 | Error | [{0}] is not valid on a document | Attribute | No |
| DYNA013 | Error | Type '{0}' cannot be both an entity and a document | Type | No |
| DYNA014 | Error | Generated member '{0}' conflicts with a member of '{1}' (`Keys`, or a type named `Keys`) | Member | No |
| DYNA015 | Error | DynaSharp generated code needs C# 11 or later; this project uses {0} | Type | No |
| DYNA016 | Error | Type '{0}' used by '{1}' is not accessible to generated code | Property | No |
| DYNA017 | Error | Property '{0}' of type '{1}' cannot be a DynamoDB key, which must be a string, number or binary | Property | No |
| DYNA018 | Error | Index '{0}' is invalid: {1} (no partition key, duplicate name, colliding builder name) | Attribute | No |
| DYNA100 | Error | Key template '{0}' has a syntax error at position {1}: {2} | Attribute argument | No |
| DYNA101 | Error | Key template literal must be printable ASCII | Attribute argument | No |
| DYNA102 | Error | Key template references unknown property '{0}' | Attribute argument | No |
| DYNA103 | Error | Property '{0}' of type '{1}' cannot be used in a key (float, double, bool, TimeSpan, wide integers) | Attribute argument | No |
| DYNA104 | Error | Format '{0}' does not fit type '{1}' | Attribute argument | No |
| DYNA105 | Error | Format '{0}' is out of range | Attribute argument | No |
| DYNA106 | Error | '{0}' and '{1}' are variable-width with no literal between them, so the key cannot be parsed | Attribute argument | No |
| DYNA107 | Error | Nullable '{0}' is used in a key that is not Sparse | Attribute argument | Yes |
| DYNA108 | Warning | Number '{0}' has no fixed format in a sort key, so 10 sorts before 9. Use d<w> or n<i>, or {{0}:g}. | Attribute argument | Yes |
| DYNA109 | Error | [DynamoDbIgnore] on '{0}' is invalid: {1} (lossy format, or computed) | Property | No |
| DYNA110 | Warning | Index '{0}' is Sparse, but all its inputs are non-nullable, so every item is indexed | Attribute | No |
| DYNA111 | Error | Property '{0}' appears twice in a key template | Attribute argument | No |
| DYNA112 | Error | A template belongs on the type, a plain key on a property | Attribute | No |

Warnings are only reachable through `NoWarn` for silencing, apart from `{Id:g}`. A duplicate-discriminator check across types is M3, where mixed result sets are declared.

## 8. Test strategy

**Dependency to propose:** `Verify.TUnit` 33.3.4, test only.

**Not `Verify.SourceGenerators`,** despite what `tasks.md` says. It dates from 2024, is built on Verify 26.5, and Verify is now at 33. Binary compatibility across 7 majors is unknown. What it adds is a small converter from `GeneratorDriverRunResult` to files, which a 20-line local helper replaces with one `Target` per generated tree. Spike S1 may reverse this if it loads cleanly.

**All tests stay in `tests/DynaSharp.UnitTests`.** One project keeps the single coverage file, the coverage gate, `dotnet test --project` and the Stryker setup unchanged. Merging two coverage reports needs tooling the repo does not have.

| Layer | Where | What |
|---|---|---|
| Snapshot | `Generators/Snapshots` | One input per shape: the section 4 entity, class, struct, record struct, positional, nested containing types, global namespace, inheritance, `[Flags]`, keyword identifiers, 2 enums with one simple name |
| Compile check | Same | Every snapshot input compiled in-process at `LanguageVersion.CSharp11` and at latest, asserting zero warnings. Consumers have warnings as errors, so a warning in generated code breaks them. |
| Diagnostics | `Generators/Diagnostics` | One test per id, positive and negative, asserting id, severity, full message and span. A descriptor snapshot covers titles and categories. |
| Template parser | `Generators/KeyTemplateParserTests` | Pure text tests, including `{{`, `}}`, every error position |
| Incrementality | `Generators/IncrementalityTests` | See below |
| Compile and run | `Mapping/` | Real entities built by the real generator in the test project. Round trip through `DynamoDbWriter`, `DynamoDbReader`; expected JSON written by hand, never derived from the writer. Also reads `real_aws_responses.json` where shapes fit. |
| Converters | `Serialization/Converters` | Each converter, all wrong-type paths, counting pool for every rented array |
| Key golden vectors | `Keys/key_vectors.json` | From a Python script with `decimal`, in the style of the botocore scripts, so the oracle is independent. Includes the spec's `P0012.50`, `N9987.49`, `P0000.00`. |
| Key properties | `Keys/KeyPropertyTests` | Seeded `Random`, boundary values plus 10,000 samples: `a < b` implies ordinal `enc(a) < enc(b)`, and `Parse(Format(x)) == x` for lossless formats. No FsCheck: it has no TUnit integration, and a fixed seed satisfies FIRST. |
| Pipeline | `DynamoDbClient*Tests` | Typed operations through `StubHandler`; mapping failure is not wrapped and not retried |
| AOT | `tests/DynaSharp.AotSmoke` | Entity with composite keys, sparse GSI, nested document, recursion, list, set, dictionary, enum, custom converter; typed Get, Put, Query; `Format`, `Parse`, `Prefix`. Record binary size before and after. |
| Container | `tests/DynaSharp.IntegrationTests/Mapping` | Parametrized by backend: round trip; `BETWEEN` and `begins_with` over encoded keys including negative numbers, the ordering claim's external check; sparse index membership; empty set omission; TTL attribute type; nesting at depth. `TableSpec` needs configurable GSI attributes. |
| Benchmarks | `EntityBenchmarks` | Same canned responses as `ClientComparisonBenchmarks`, against EfficientDynamoDb's mapped API. Check `executed benchmarks: N`. |

**Incrementality tests**

```csharp
var driver = CSharpGeneratorDriver.Create(
    [new DynaSharpGenerator().AsSourceGenerator()],
    driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
driver = driver.RunGenerators(compilation);
driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("// unrelated")));
var run = driver.GetRunResult().Results[0];
// Tracked steps must report Cached or Unchanged; run.TrackedOutputSteps must report Cached.
```

| Change | Expected |
|---|---|
| Unrelated file added or edited | Model `Unchanged`, output `Cached` |
| Method body edited in the entity's file | Model `Unchanged`, output `Cached` |
| Property added to entity A | A `Modified`, B `Cached` |
| Enum member added in another file | Entity using it `Modified` |
| Unrelated member added to a custom converter | Model `Unchanged` |

Plus a reflection walk over every tracked step output asserting no `ISymbol`, `Compilation`, `SyntaxNode`, `SemanticModel` or tree-bound `Location` is reachable, and that any array inside is wrapped in `EquatableArray<T>`. Reflection is fine here: the ban applies to `src/`.

**100 % coverage**

- Add the generator dll to `coverage.config`. Generated code lives in the test assembly and is not measured, which is right: its behaviour is covered through snapshots and compile-and-run tests.
- Driver tests run the generator in-process, so it is measured there.
- No `[ExcludeFromCodeCoverage]`; the repo has none.
- Consequence for code style: no `default: throw` on closed sets, use polymorphism or dictionaries.
- Keep `CancellationToken` use to `ThrowIfCancellationRequested()`.

**Stryker**

- Add the generator to `DynaSharp.Mutation.slnx`. In solution mode it mutates each source project and swaps the dll in the test output. Driver and snapshot tests then run the mutant, while real entities in the test assembly were built once by the clean generator.
- Text emitters suit this: nearly any string or logic mutant changes output and fails a snapshot.
- Equivalent mutants to accept and mark with `// Stryker disable`:
  - `EquatableArray.GetHashCode` internals, since any consistent hash passes.
  - `ThrowIfCancellationRequested`, through `ignore-methods`.
- Verify writes `*.received.*` beside sources, and parallel Stryker workers can race on those. A racing failure only occurs on a real mismatch, so the mutant is still legitimately killed. Set `CI=true` for the run so diff tools stay off, and rely on the gitignore entry.
- Stryker runs only before the milestone closes, per the decision of 2026-10-31.

## 9. Risks and sharp edges

**Stryker and language versions**

- Nothing in `src/`, including `DynaSharp.Generators`, may use syntax Roslyn 5.9 cannot parse. Generators are pinned to C# 12; `DynaSharp` must avoid anything newer than what 5.9 parses even though its default is 15. Tests are free, since Stryker does not compile them. Add this as an explicit rule to `AGENTS.md`.
- Never reference a Roslyn newer than 5.9 from the generator, in case a Stryker-compiled project ever loads it as an analyzer. `DynaSharp.csproj` itself uses `ReferenceOutputAssembly="false"` without `OutputItemType="Analyzer"`, so it does not load it.

**C# features to avoid in generated code**

Generated code targets C# 11 and uses `u8`, `file`, static abstract, `required`, raw records. It must not use:

- Collection expressions, primary constructors (both C# 12).
- `field`, `extension` blocks, first-class span reliance (C# 14).
- `allows ref struct`, `params` collections, `[with(...)]`.
- Any C# 15 feature.
- Extension-method syntax on spans.
- Unqualified type names.

C# 14 span conversions change overload resolution on arrays, another reason to call `MemoryExtensions` by full name and never call `.Contains` or `.Reverse` on arrays.

**Roslyn**

- Value equality: an `ImmutableArray` or plain array in a record silently destroys caching. The reflection walk catches it.
- `TypedConstant` can be an error constant when `typeof(X)` does not resolve. Handle it rather than throwing: an exception in a generator becomes CS8785 and removes all output.
- Attributes on other partial parts are visible through the symbol, but only a declaration carrying the marker attribute triggers the pipeline. Report on the correct part.
- `ForAttributeWithMetadataName` fires once per attributed part. `AllowMultiple = false` prevents duplicates.
- Base classes from other assemblies: a private setter there is inaccessible; treat as get-only.
- Aliased enum members would produce CS8510 in a switch, so write by canonical member, read by all names.
- A property called `Keys` or a nested type conflict is DYNA014, not a compile error in generated code.
- `<auto-generated/>` silences analyzers but not compiler warnings such as CS1591, so public generated members need XML docs; the `#pragma` covers CS0612 and CS0618 for `[Obsolete]` properties.
- `VBCSCompiler` can keep an old generator loaded. If a build disagrees with a driver test, use `dotnet build-server shutdown`.
- `global.json` bump to GA: emitter output does not depend on Roslyn formatting, so snapshots should not move. Rerun everything anyway.

**.NET and SDK**

- `dotnet publish -o` in RC 3 already has a known doc-file bug; keep `PublishDocumentationFile=false` on AotSmoke.
- With a RID-specific publish, the generator may build under a RID subfolder. Only `bin/$(Configuration)/netstandard2.0/` is used by pack, which has no RID. Verify in WP-A.
- `ContinuousIntegrationBuild=true` maps source paths, and Verify relies on `CallerFilePath`. The test project probably needs `<DeterministicSourcePaths>false</DeterministicSourcePaths>`. Must be proven with `CI=true`.
- `*.verified.cs` under the test project gets globbed into `Compile` unless removed.
- `.gitattributes` forces CRLF globally, so verified files need their own LF rule.
- Native AOT: many struct generics grow the binary, usually slightly. Measure it.
- Decimal trailing zeros: DynamoDB normalizes `12.50` to `12.5`, equal under `decimal.Equals`. Tests compare numerically, as the P7 finding requires.
- `double` values can fall outside DynamoDB's range; DynamoDB rejects them, and M2 does not validate.

**Design risks**

- `file` types cannot appear in signatures of non-file members. Enum converters appear only inside method bodies, which is legal. Keep them out of `__Fields` and any signature.
- Struct entities are copied through `T` parameters. Document this.
- Cyclic object graphs: `Utf8JsonWriter`'s depth check stops them with `InvalidOperationException` at depth 1000 before the stack does.
- Writing attribute names with `"x"u8` goes through the `Utf8JsonWriter` escape scan, with a relaxed encoder. I chose this over a `JsonEncodedText` field per attribute, to avoid per-type static setup. If `EntityBenchmarks` shows name writing at the top of a profile, add `WriteAttributeName(JsonEncodedText)`.

## 10. Work packages

Same working mode as M1b: the lead owns contracts and merges; implementers work in separate worktrees. Standard gates per package: locked restore, `dotnet format --verify-no-changes`, build, tests, 100 % coverage. The lead runs the Linux AOT gate at merge.

| WP | Content | Depends on | Parallel with | Extra gates |
|---|---|---|---|---|
| **G0 Contracts** (lead) | Spikes S1, S2 (section 11). Public interfaces, attributes, `EntityKey`, `KeyWriter`/`KeyReader` signatures, `DynamoDbMappingException`, reader additions, `AttributeValueConverter` public, `AttributeValue.From`. Update `requirements.md`, `patterns.md`, `tasks.md`, `AGENTS.md`. | none | none | Your decisions I1-I6 recorded |
| **G1 Project and packaging** | Generator project, no-op generator, wiring in 4 consumers, lock files, slnx, coverage, Stryker, Dependabot, CI pack check, `PackageSmoke`, Verify helper, one trivial snapshot | G0 | G2, G3, G7 | `CI=true` locked restore; nupkg has `analyzers/dotnet/cs/DynaSharp.Generators.dll` and no dependency; `PackageSmoke` runs; Linux AOT publish |
| **G2 Converters** | Everything in 5.5, counting pool tests | G0 | G1, G3, G4, G7 | Allocation assertion for known-name reads |
| **G3 Key encodings** | `KeyWriter`, `KeyReader`, Python vectors, property tests | G0 | G1, G2, G4, G7 | Vectors from the independent script; on-disk contract reviewed by lead |
| **G4 Model and diagnostics** | Model records, `EquatableArray`, `ModelBuilder`, parser, binder, converter resolver, all DYNA ids, incremental pipeline | G1 | G2, G3, G7 | Every id tested; incrementality tests pass |
| **G5 Entity emitter** | Read, write, `__Fields`, constructors, enums, `Info`, `WriteKey`, no-key parts | G2 contract, G4 | G6 | All shape snapshots; zero warnings at C# 11 and latest; compile-and-run round trips |
| **G6 Key emitter** | `Keys` class, format, parse, prefix, sparse, `[DynamoDbIgnore]` restore, `Of` | G3 contract, G4 | G5 | Snapshots; generated output matches key vectors |
| **G7 Typed operations** | Operations, responses, client overloads, shared option writers, `ItemPage<T>` | G0 | G1-G4 | Hand-written `IDynamoDbEntity<T>` in tests until G5 lands; existing vectors still pass; XML docs complete |
| **G8 End to end** | AOT smoke entity, container scenarios, `EntityBenchmarks`, record numbers in Findings | G5, G6, G7 | none | Linux AOT exit 0 with no warnings; both emulators pass; allocations recorded against section 6; `DYNASHARP_BACKENDS=dynoxide,dynamodb-local` |
| **G9 Close** | Stryker, refactoring pass, independent review by a second reviewer, fix findings | G8 | none | Stryker >= 90 % across all projects, review findings closed |

Merge overlaps to expect: G1 and G4 on the generator project, G5 and G6 on the emitter entry point. Have the lead commit a stub `KeysEmitter` and `EntityEmitter` split in G4 so they do not collide.

## 11. Spikes and things I did not verify

Run these in G0 before G1 starts. Each is under an hour.

- **S1:** With `CI=true`, does `Verify.TUnit` on TUnit 1.76.45 find `.verified.cs` files, or does it need `DeterministicSourcePaths=false`? Also try `Verify.SourceGenerators` under Verify 33; if it loads, you may prefer it, otherwise the local helper stands.
- **S2:** A hand-written generated file with the section 4 shape, compiled on the pinned SDK and on Roslyn 5.0.0 at C# 11. It must prove:
  - Explicit static interface implementation through a namespace alias.
  - `new KeyWriter(stackalloc byte[n])` plus `ref` passing compiles without escape errors.
  - `[SetsRequiredMembers]` with `: this()`.
  - A `file` struct as a generic argument in a method body.
- **Compiler behaviour to confirm during G1 or G4:**
  - Whether `IsExternalInit` truly conflicts under `InternalsVisibleTo`. If not, the public test surface could return to internal.
  - That diagnostics at external-file locations display correctly in the IDE.
  - RID flow through the generator on Linux publish.
  - Whether Stryker's solution mode links a test project's `OutputItemType="Analyzer"` reference that also references the assembly.
  - That CS1591 fires in `<auto-generated/>` files. If not, the XML docs are still wanted.

The allocation numbers in section 6 are estimates; G8 replaces them with measurements.

