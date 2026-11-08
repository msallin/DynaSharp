# DynaSharp - working agreements

## Specs and planning
- `spec/requirements.md` is the source of truth for WHY, WHAT and HOW. Read it before starting work.
- `spec/api.md` is the binding contract of the public API, and `spec/architecture.md` describes the code as it is today. A change that makes either wrong corrects it in the same commit.
- `docs/diagnostics.md` is the page behind the help links of the analyzers (published at https://msallin.github.io/DynaSharp/diagnostics.html), so its headings are stable. `docs/` holds user documentation, `spec/` holds specs.
- `spec/tasks.md` is the shared blackboard: claim a task before starting it, keep its status current, record decisions and findings there.
- Specs, plans, designs and decisions go into `spec/`. Nowhere else.
- `README.md` is the overview for users: intro, installation, a short getting started, a few examples, API levels, design principles, benchmarks. Plans and status of work stay out of it, its examples must compile, and its numbers come from a benchmark run. The full guide is in `docs/`, published with DocFX (`docs/docfx.json`, `.github/workflows/docs.yml`) at https://msallin.github.io/DynaSharp/, whose home page is the README. Its examples compile too, and a link between pages is relative (`tables.md#adding-an-index`), so that it works on the site and on GitHub. The README ships in the package, so it links to the site with absolute URLs.

## Principles
- Below 1.0, breaking changes are allowed in every release and preferred over technical debt: choose the right design, not the one that keeps a shipped signature. Mark a break `feat!:`/`fix!:` and say in the commit body what to change (see `spec/requirements.md`).
- Keep it simple. No speculative abstractions, no duplicated logic. Hide internals: `internal` by default, public API only on purpose.
- Small, cohesive modules (signing, HTTP pipeline, serialization, mapping, fluent API) with narrow interfaces between them.
- Public API: complete XML docs, written for the hover in the IDE. `build/DocCheck.cs` refuses the words of the implementation (allocation, pool, box, heap, "no string", hot path) in the docs of public members, and `build/ReadmeCheck.cs` compiles the `<example>` blocks. The rules:
  - `<summary>`: one sentence, two at most, that reads alone in a tooltip: what the member does in DynamoDB terms, then the one thing the caller must know (the missing-item case, the capacity cost, the consistency, the retry risk). No cross-reference but the next step, no "how it works".
  - `<remarks>`: everything else, in `<para>`s with a bold lead-in (`<b>Not atomic.</b>`): edge cases, retries and idempotency, what DynamoDB rejects and with which exception. One canonical place per topic; the others link to it or take it with `<inheritdoc cref="..." path="/remarks"/>`.
  - `<param>`: what to pass, its constraints and what the default or `null` means. `<returns>`: the shape and the absent case. `<exception>`: one line each, argument checks first, the `DynamoDbException` catch-all last, and what state the operation left.
  - `<example>`: 3 to 8 lines on every type a newcomer meets, written against the shop of `samples/DynaSharp.Sample` and the fields of `build/ReadmeCheck.cs`, because the check compiles them.
  - Vocabulary: a command is sent, not "ended at a terminal"; `Order.Attrs.Total` is a handle; an interpolated value stands in a hole. A diagnostic is linked, `<see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA209</see>`, with the anchor of its section on that page.
  - An implementation fact stays only where the user must act on it, phrased as a rule (a command cannot cross an `await`; a handler reads the body before it returns). The rationale goes to `docs/design.md`. A DynamoDB cost is behaviour and stays: "costs twice the capacity".
- A change of the public surface of `DynaSharp`, `DynaSharp.Testing` or `DynaSharp.Coordination` updates `PublicAPI.Unshipped.txt` in that project, in the same commit. `RS0016` and `RS0017` fail the build otherwise. `PublicAPI.Shipped.txt` holds what a release has shipped: before a release, `build/ship-public-api.sh` moves the lines there, and the rules of `AnalyzerReleases.Unshipped.md` into a release section of `AnalyzerReleases.Shipped.md`, committed as `chore`; the release workflow refuses to run while an Unshipped file holds a line.
- Comments only for non-obvious things (protocol quirks, performance tricks, why something is not done the obvious way).
- Performance matters: pooled buffers, `Span<T>`, UTF-8 end to end, no LINQ or closures on hot paths. Measure with the benchmarks before and after.
- No unsafe code: no `unsafe`, `Unsafe`, `MemoryMarshal`, `CollectionsMarshal` (enforced by `src/BannedSymbols.txt`).
- No reflection or runtime codegen in `src/` (Native AOT). Mapping code comes from the source generator.
- Nothing in `src/` may use syntax Roslyn 5.9 cannot parse, because Stryker compiles it with that version: no preview language features. Each csproj enforces this with `<LangVersion>`: C# 14 for `DynaSharp`, `DynaSharp.Testing` and `DynaSharp.Coordination`, C# 12 for `DynaSharp.Generators`. Tests are free.
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
- `dotnet run build/ReadmeCheck.cs -- README.md src/DynaSharp.Testing/README.md src/DynaSharp.Coordination/README.md docs/*.md src/DynaSharp src/DynaSharp.Testing src/DynaSharp.Coordination` (compiles the C# blocks of the pages and the `<example>` blocks of the XML docs against the projects, in one compilation)
- `dotnet run build/DocCheck.cs -- src` (fails on a public XML doc that describes the implementation instead of the behaviour)
- `dotnet build src/DynaSharp.Testing -c Release` and `dotnet build src/DynaSharp.Coordination -c Release`, then `dotnet docfx docs/docfx.json --serve` (builds the site into `artifacts/site` and serves it)
- `dotnet test --project tests/DynaSharp.UnitTests`, `dotnet stryker`
- Fast local loop, never the gate: `dotnet build tests/DynaSharp.UnitTests -c Release -p:RunAnalyzers=false` (source generators still run; analyzers and formatting do not, so the full build above comes before a commit), then `dotnet test --project tests/DynaSharp.UnitTests -c Release --no-build --treenode-filter "/*/*/DynamoDbWriterTests/*"` for one class, `"/*/DynaSharp.UnitTests.Fluent/*/*"` for a namespace, or `"/*/(!DynaSharp.UnitTests.Generators*)/*/*"` for everything but the Roslyn tests of the generator (about half the CPU of a run)
- `dotnet test --project tests/DynaSharp.IntegrationTests` (`DYNASHARP_BACKENDS` selects `fake` (default, the in-memory `FakeDynamoDb`, no Docker), `dynoxide`, `dynamodb-local`, comma separated, or `all` for the three; the emulators need Docker. `aws` is real DynamoDB, costs money, needs no Docker and is not part of `all`; `.github/workflows/real-aws.yml` runs it)
- `dotnet publish tests/DynaSharp.AotSmoke -c Release -r linux-x64` (fails on any AOT/trim warning)
