# DynaSharp - working agreements

## Specs and planning
- `spec/requirements.md` is the source of truth for WHY, WHAT and HOW. Read it before starting work.
- `spec/api.md` is the binding contract of the public API, and `spec/architecture.md` describes the code as it is today. A change that makes either wrong corrects it in the same commit.
- `spec/diagnostics.md` is the page behind the help links of the analyzers, so its headings are stable. `spec/patterns.md` is the catalog of design patterns.
- `spec/tasks.md` is the shared blackboard: claim a task before starting it, keep its status current, record decisions and findings there.
- `spec/history/` holds the design documents as they were written. It is not updated, and it describes designs that were dropped.
- Specs, plans, designs and decisions go into `spec/`. Nowhere else.
- `README.md` is the page for users: intro, getting started, examples, API levels, design principles, benchmarks. Plans and status of work stay out of it, its examples must compile, and its numbers come from a benchmark run.

## Principles
- Keep it simple. No speculative abstractions, no duplicated logic. Hide internals: `internal` by default, public API only on purpose.
- Small, cohesive modules (signing, HTTP pipeline, serialization, mapping, fluent API) with narrow interfaces between them.
- Public API: complete XML docs that explain behavior, edge cases and DynamoDB semantics, not restate the signature.
- A change of the public surface of `DynaSharp` or `DynaSharp.Testing` updates `PublicAPI.Unshipped.txt` in that project, in the same commit. `RS0016` and `RS0017` fail the build otherwise. `PublicAPI.Shipped.txt` holds what a release has shipped: a release moves the lines there.
- Comments only for non-obvious things (protocol quirks, performance tricks, why something is not done the obvious way).
- Performance matters: pooled buffers, `Span<T>`, UTF-8 end to end, no LINQ or closures on hot paths. Measure with the benchmarks before and after.
- No unsafe code: no `unsafe`, `Unsafe`, `MemoryMarshal`, `CollectionsMarshal` (enforced by `src/BannedSymbols.txt`).
- No reflection or runtime codegen in `src/` (Native AOT). Mapping code comes from the source generator.
- Nothing in `src/` may use syntax Roslyn 5.9 cannot parse, because Stryker compiles it with that version: no preview language features. Each csproj enforces this with `<LangVersion>`: C# 14 for `DynaSharp` and `DynaSharp.Testing`, C# 12 for `DynaSharp.Generators`. Tests are free.
- No runtime package dependencies in `src/` (logging abstractions and the `[LoggerMessage]` generator ship with .NET 11). Propose any dependency before adding it.
- Observability is part of every feature, in the same change: traces via `ActivitySource`, metrics via `Meter` (both named `DynaSharp`, following the OpenTelemetry semantic conventions), logs via source-generated `[LoggerMessage]`. Logs only for events outside a single request (credential resolution, refresh failures, retry exhaustion). Never put keys, item data or attribute values into telemetry.

## Tests (TUnit)
- Class: `{TypeUnderTest}Tests`. Method: `Method_Scenario_ExpectedResult`.
- Arrange / Act / Assert sections in every test; one logical assertion; FIRST (no shared mutable state, no ordering, no sleeps).
- Tests that read or change the process environment (`new DynamoDbClient()`, `new EnvironmentCredentialsProvider()`) carry `[NotInParallel(TestClient.ProcessEnvironment)]`; other tests set every option so they never read it.
- Gates: 100 % line and branch coverage on `src/`, Stryker mutation score >= 90 %, the latter run once before a milestone closes.

## Commits
- Conventional Commits (`feat:`, `fix:`, `perf:`, `refactor:`, `test:`, `docs:`, `build:`, `ci:`, `chore:`). They drive the version.

## Commands
- `dotnet build DynaSharp.slnx` (warnings are errors), `dotnet format DynaSharp.slnx --verify-no-changes`
- `dotnet test --project tests/DynaSharp.UnitTests`, `dotnet stryker`
- `dotnet test --project tests/DynaSharp.IntegrationTests` (needs Docker; `DYNASHARP_BACKENDS` selects `dynoxide` (default), `dynamodb-local` or both, comma separated. `aws` is real DynamoDB, costs money, needs no Docker and is not part of `all`; `.github/workflows/real-aws.yml` runs it)
- `dotnet publish tests/DynaSharp.AotSmoke -c Release -r linux-x64` (fails on any AOT/trim warning)
