# DynaSharp - working agreements

## Specs and planning
- `spec/requirements.md` is the source of truth for WHY, WHAT and HOW. Read it before starting work.
- `spec/api.md` is the binding contract of the public API, and `spec/architecture.md` describes the code as it is today. A change that makes either wrong corrects it in the same commit.
- `docs/diagnostics.md` is the page behind the help links of the analyzers (published at https://msallin.github.io/DynaSharp/diagnostics.html), so its headings are stable. `docs/` holds user documentation, `spec/` holds specs. `spec/patterns.md` is the catalog of design patterns.
- `spec/tasks.md` is the shared blackboard: claim a task before starting it, keep its status current, record decisions and findings there.
- `spec/history/` holds the design documents as they were written. It is not updated, and it describes designs that were dropped.
- Specs, plans, designs and decisions go into `spec/`. Nowhere else.
- `README.md` is the overview for users: intro, installation, a short getting started, a few examples, API levels, design principles, benchmarks. Plans and status of work stay out of it, its examples must compile, and its numbers come from a benchmark run. The full guide is in `docs/`, published with DocFX (`docs/docfx.json`, `.github/workflows/docs.yml`) at https://msallin.github.io/DynaSharp/, whose home page is the README. Its examples compile too, and a link between pages is relative (`tables.md#adding-an-index`), so that it works on the site and on GitHub. The README ships in the package, so it links to the site with absolute URLs.

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
- `dotnet build DynaSharp.slnx` (warnings are errors, formatting included), `dotnet format whitespace DynaSharp.slnx --verify-no-changes` (which sees line endings too)
- `dotnet run build/ReadmeCheck.cs -- README.md src/DynaSharp.Testing/README.md docs/*.md` (compiles the C# blocks of the pages against the projects, in one compilation)
- `dotnet build src/DynaSharp.Testing -c Release`, then `dotnet docfx docs/docfx.json --serve` (builds the site into `artifacts/site` and serves it)
- `dotnet test --project tests/DynaSharp.UnitTests`, `dotnet stryker`
- Fast local loop, never the gate: `dotnet build tests/DynaSharp.UnitTests -c Release -p:RunAnalyzers=false` (source generators still run; analyzers and formatting do not, so the full build above comes before a commit), then `dotnet test --project tests/DynaSharp.UnitTests -c Release --no-build --treenode-filter "/*/*/DynamoDbWriterTests/*"` for one class, `"/*/DynaSharp.UnitTests.Fluent/*/*"` for a namespace, or `"/*/(!DynaSharp.UnitTests.Generators*)/*/*"` for everything but the Roslyn tests of the generator (about half the CPU of a run)
- `dotnet test --project tests/DynaSharp.IntegrationTests` (`DYNASHARP_BACKENDS` selects `fake` (default, the in-memory `FakeDynamoDb`, no Docker), `dynoxide`, `dynamodb-local`, comma separated, or `all` for the three; the emulators need Docker. `aws` is real DynamoDB, costs money, needs no Docker and is not part of `all`; `.github/workflows/real-aws.yml` runs it)
- `dotnet publish tests/DynaSharp.AotSmoke -c Release -r linux-x64` (fails on any AOT/trim warning)
