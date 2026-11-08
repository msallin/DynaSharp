# DynaSharp - working agreements

## Where things live
- `spec/` holds every spec, plan, design and decision, and nothing of it goes elsewhere:
  - `requirements.md`: WHY, WHAT and HOW. Read it before starting work.
  - `api.md`: the binding contract of the public API. `architecture.md`: the code as it is. A change that makes either wrong corrects it in the same commit.
  - `tasks.md`: the blackboard. Claim a task before starting it, keep its status current, record decisions (with their why) and findings there.
- `.claude/skills/`: procedures for one kind of task (releasing, mutation testing, the fidelity of the fake).
- `docs/`: user documentation, published with DocFX (`docs/docfx.json`, `docs.yml`) at https://msallin.github.io/DynaSharp/, home page the README. Links between pages are relative (`tables.md#adding-an-index`). `docs/diagnostics.md` is the target of the analyzers' help links, so its headings are stable. `docs/design.md` holds the rationale a user sees.
- `README.md`: the overview for users (intro, installation, getting started, a few examples, API levels, design principles, benchmarks). No plans or status; its numbers come from a benchmark run. It ships in the package, so it links to the site with absolute URLs.
- The C# blocks of the README and `docs/` compile.

## Principles
- Below 1.0, breaking changes are allowed in every release and preferred over technical debt: choose the right design, not the one that keeps a shipped signature. Mark a break `feat!:`/`fix!:` and say in the commit body what to change.
- Keep it simple: no speculative abstractions, no duplicated logic. `internal` by default; public API only on purpose.
- Small, cohesive modules (signing, HTTP pipeline, serialization, mapping, fluent API) with narrow interfaces.
- A change of the public surface of `DynaSharp`, `DynaSharp.Testing` or `DynaSharp.Coordination` updates that project's `PublicAPI.Unshipped.txt` in the same commit (`RS0016`/`RS0017` fail the build otherwise). `PublicAPI.Shipped.txt` holds what a release shipped: before a release, `build/ship-public-api.sh` moves the lines there and the analyzer rules into `AnalyzerReleases.Shipped.md` (a `chore` commit); the release workflow refuses to run while an Unshipped file holds a line (`releasing` skill).
- Comments only for the non-obvious (protocol quirks, performance tricks, why not the obvious way).
- Performance matters: pooled buffers, `Span<T>`, UTF-8 end to end, no LINQ or closures on hot paths. Measure with the benchmarks before and after.
- No unsafe code: no `unsafe`, `Unsafe`, `MemoryMarshal`, `CollectionsMarshal` (`src/BannedSymbols.txt`).
- No reflection or runtime codegen in `src/` (Native AOT); mapping code comes from the source generator.
- `src/` uses no syntax Roslyn 5.9 cannot parse (Stryker compiles with it), so no preview features. `<LangVersion>` enforces it: C# 14 for `DynaSharp`, `DynaSharp.Testing`, `DynaSharp.Coordination`; C# 12 for `DynaSharp.Generators`. Tests are free.
- No runtime package dependencies in `src/` (logging abstractions and `[LoggerMessage]` ship with .NET 11). Propose any dependency first.
- Observability is part of every feature, in the same change: traces (`ActivitySource`) and metrics (`Meter`), both named `DynaSharp` and following the OpenTelemetry semantic conventions, and source-generated `[LoggerMessage]` logs only for events outside a single request (credential resolution, refresh failures, retry exhaustion). Never put keys, item data or attribute values into telemetry.

## XML docs of the public API
Complete, written for the hover in the IDE. `build/DocCheck.cs` refuses implementation words (allocation, pool, box, heap, "no string", hot path); `build/ReadmeCheck.cs` compiles the `<example>` blocks.
- `<summary>`: one sentence, two at most, readable alone in a tooltip: what the member does in DynamoDB terms, then the one thing the caller must know (missing item, capacity cost, consistency, retry risk). No cross-reference but the next step, no "how it works".
- `<remarks>`: the rest, in `<para>`s with a bold lead-in (`<b>Not atomic.</b>`): edge cases, retries and idempotency, what DynamoDB rejects and with which exception. One canonical place per topic; others link to it or use `<inheritdoc cref="..." path="/remarks"/>`.
- `<param>`: what to pass, constraints, what the default or `null` means. `<returns>`: the shape and the absent case. `<exception>`: one line each, argument checks first, the `DynamoDbException` catch-all last, and the state the operation left.
- `<example>`: 3 to 8 lines on every type a newcomer meets, against the shop of `samples/DynaSharp.Sample` and the fields of `build/ReadmeCheck.cs`.
- Vocabulary: a command is sent (not "ended at a terminal"); `Order.Attrs.Total` is a handle; an interpolated value stands in a hole. Link a diagnostic with the anchor of its section: `<see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA209</see>`.
- An implementation fact stays only where the user must act on it, phrased as a rule (a command cannot cross an `await`; a handler reads the body before it returns); the rationale goes to `docs/design.md`. A DynamoDB cost is behaviour and stays ("costs twice the capacity").

## Tests (TUnit)
- Class `{TypeUnderTest}Tests`, method `Method_Scenario_ExpectedResult`; Arrange / Act / Assert sections; one logical assertion; FIRST (no shared mutable state, no ordering, no sleeps).
- A test that reads or changes the process environment (`new DynamoDbClient()`, `new EnvironmentCredentialsProvider()`) carries `[NotInParallel(TestClient.ProcessEnvironment)]`; every other test sets all options so it never reads it.
- Gates: 100 % line and branch coverage on `src/`; Stryker mutation score >= 90 %, run once before a milestone closes (`mutation-testing` skill).

## Commits
Conventional Commits (`feat:`, `fix:`, `perf:`, `refactor:`, `test:`, `docs:`, `build:`, `ci:`, `chore:`); they drive the version.

## Commands
- `dotnet build DynaSharp.slnx` (warnings are errors, formatting included); `dotnet format whitespace DynaSharp.slnx --verify-no-changes` (sees line endings too).
- `dotnet run build/ReadmeCheck.cs -- README.md src/DynaSharp.Testing/README.md src/DynaSharp.Coordination/README.md docs/*.md src/DynaSharp src/DynaSharp.Testing src/DynaSharp.Coordination` (the C# blocks of the pages and the `<example>` blocks, in one compilation).
- `dotnet run build/DocCheck.cs -- src`.
- Site: `dotnet build src/DynaSharp.Testing -c Release` and `dotnet build src/DynaSharp.Coordination -c Release`, then `dotnet docfx docs/docfx.json --serve` (into `artifacts/site`).
- `dotnet test --project tests/DynaSharp.UnitTests`; `dotnet stryker`.
- Fast local loop, never the gate: `dotnet build tests/DynaSharp.UnitTests -c Release -p:RunAnalyzers=false` (generators run, analyzers and formatting do not), then `dotnet test --project tests/DynaSharp.UnitTests -c Release --no-build --treenode-filter "/*/*/DynamoDbWriterTests/*"` for a class, `"/*/DynaSharp.UnitTests.Fluent/*/*"` for a namespace, `"/*/(!DynaSharp.UnitTests.Generators*)/*/*"` for all but the generator's Roslyn tests (half the CPU).
- `dotnet test --project tests/DynaSharp.IntegrationTests`: `DYNASHARP_BACKENDS` is `fake` (default, in-memory, no Docker), `dynoxide`, `dynamodb-local`, comma separated, or `all` for the three (emulators need Docker). `aws` is real DynamoDB, costs money, is not in `all`, and runs in `real-aws.yml`.
- `dotnet publish tests/DynaSharp.AotSmoke -c Release -r linux-x64` (fails on any AOT/trim warning).
