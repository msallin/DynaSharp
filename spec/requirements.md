# DynaSharp - requirements

Source of truth for WHY, WHAT and HOW. Changes to scope or principles are made here first, then planned in
`tasks.md`.

## WHY

DynaSharp is an alternative to [EfficientDynamoDb](https://github.com/AllocZero/EfficientDynamoDb) (MIT).

Keep:
- A fluent high-level API that stays true to DynamoDB semantics: easy to read and write, no ORM abstraction that
  hides how DynamoDB works.
- Allocation-conscious performance well beyond the AWS SDK.

Fix:
- Reflection-based mapping, which rules out Native AOT and costs startup time. DynaSharp uses a source generator.
- No credential chain in the core: anything beyond static keys needs the AWS SDK package. DynaSharp resolves
  credentials itself (environment, web identity, profiles with assume role, SSO and `credential_process`, ECS/EKS, IMDS).
- Weak testability for consumers. DynaSharp makes the HTTP wire the test seam: stub it, record it, or run against an
  in-memory fake.
- No observability. DynaSharp emits OpenTelemetry-compatible traces, metrics and logs from day one.
- Push performance further with current .NET (UTF-8 end to end, pooled buffers, one-shot crypto, generated parsing),
  without unsafe code.

## WHAT

### Functional

- **Low-level API**: request/response types mirroring DynamoDB, items as `Dictionary<string, AttributeValue>`.
  Operations: GetItem, PutItem, UpdateItem, DeleteItem, Query, Scan, BatchGetItem, BatchWriteItem,
  TransactGetItems, TransactWriteItems, DescribeTable, ListTables, CreateTable, UpdateTable, DeleteTable, DescribeTimeToLive,
  UpdateTimeToLive.
- **Streams**: the low-level API, ListStreams, DescribeStream, GetShardIterator, GetRecords (separate host
  `streams.dynamodb.<region>`, signing service `dynamodb`), on top of it the reading of one shard or of a whole
  stream in one process, and the changes of a table typed as its entities.
- **Entity mapping**: source generator for `[DynamoDbEntity]` partial types, keys via attributes, built-in converters
  for common .NET types, custom converters, compile-time diagnostics.
- **High-level fluent API**: `client.Table<TEntity>()`; conditions, update expressions, query/scan with pagination,
  batch with chunking and unprocessed-item retry, transactions. Attributes referenced through generated static
  handles (no expression trees), with nested paths and a raw-name escape hatch.
- **Single-table design**: several entity types per table; composite keys from key templates; multiple GSIs with
  composite and sparse keys (attribute present only when set); queries returning mixed entity types (adjacency list,
  vertical partitioning), resolved via a discriminator.
- **Key grammar**: key templates such as `USER#{Id}` or `COUNTRY#{Country}#CANTON#{Canton}`; the generator emits
  format, parse and prefix builders (prefix at any depth for `begins_with`), with fixed-width padding,
  order-preserving encoding of negative and decimal numbers, UTC timestamps in fixed format, and escaping or
  rejection of separators in values.
- **Concurrency primitives in the core**: optimistic locking via `[Version]` (opt-in per type, then enforced on every
  write); typed `ReturnValues` (for example a counter after `ADD`); transaction builder accepting raw marker items,
  a uniqueness helper (marker item with `attribute_not_exists`), and cancellation reasons mapped back to the action
  that caused them.
- **Fan-out query**: query a set of partition keys (write shards, time buckets) and merge results by sort key, with a
  composite continuation token.
- **TTL**: converter for epoch seconds, so TTL attributes work.
- **Pattern support** (see `patterns.md`): every pattern of the catalog is expressible with the core primitives and
  shipped as a recipe in the scenario tests (executable documentation). First-class types only where subtle bugs live,
  in a separate package `DynaSharp.Coordination`: lease/lock with fencing tokens, and leader election on top of it.
  Nothing else: the queue over a sparse GSI and the hi/lo allocator are recipes (decision of 2026-11-04 in `tasks.md`).
- **Configuration**: region and endpoint from options or environment (`AWS_REGION`, `AWS_DEFAULT_REGION`,
  `AWS_ENDPOINT_URL_DYNAMODB`, `AWS_ENDPOINT_URL`); endpoint override for DynamoDB Local.
- **Credentials**: static; environment; web identity from a token file through STS (IRSA on EKS); shared config/credentials files;
  ECS and EKS Pod Identity container endpoint; IMDSv2; in profiles also `role_arn` with `source_profile` or `credential_source` (assume role,
  also as `AssumeRoleCredentialsProvider`), web identity through `web_identity_token_file` and `role_arn`, SSO (`sso_session` and the older
  `sso_start_url`, also as `SsoCredentialsProvider`), and `credential_process`.
- **Resilience**: per-attempt timeout (surfaces as `TimeoutException`, distinct from cancellation); AWS standard
  retry semantics tuned for DynamoDB; clock-skew correction; typed exceptions per DynamoDB error code.
- **Testing support** (`DynaSharp.Testing` package): `StubHandler` (canned responses, captured requests, fault
  injection such as throttling or transaction conflicts), `RecordingHandler` (inspect real traffic), and an in-memory
  fake that speaks the wire protocol. The fake targets functional tests in C# (in-process, no Docker, state
  inspectable from the test); it does not reproduce DynamoDB's fine print (exact error messages, validation order,
  limits, capacity, consistency timing). Those are covered by tests against real emulators.
- **Observability**: traces (`ActivitySource "DynaSharp"`), metrics (`Meter "DynaSharp"`), logs (`ILogger`,
  `[LoggerMessage]`), following the OpenTelemetry semantic conventions for AWS SDK / DynamoDB.

### Non-functional

- Target `net11.0` only. SDK pinned in `global.json` without roll-forward.
- Native AOT compatible; trimming-safe. No reflection, no runtime code generation.
- No unsafe code (`unsafe`, `Unsafe`, `MemoryMarshal`, `CollectionsMarshal`, `Marshal`, `NativeMemory`, `GCHandle`).
- Zero runtime package dependencies. Logging uses the abstractions that ship with .NET 11.
- Performance: at least on par with EfficientDynamoDb in time and allocations on equivalent benchmarks; far ahead of
  the AWS SDK. Hot paths measured with BenchmarkDotNet before and after changes.
- Telemetry never contains keys, item data or attribute values. Near-zero cost when nobody listens.
- Public API fully documented with XML docs explaining behavior and DynamoDB semantics.

### Out of scope

- Streams: no consumer. No leasing, no checkpoints, no sharing of shards across processes (KCL territory). Reading one shard or one stream in-process is API.
- CRC32 response validation (TLS guarantees integrity).
- PartiQL, DAX, other target frameworks.
- Lambda stream-event deserialization (deferred, revisit on demand).
- One class per design pattern. Patterns are recipes over primitives; see `patterns.md`.

## HOW

### Architecture

One assembly `DynaSharp`; internal by default.

| Public namespace | Content |
|---|---|
| `DynaSharp` | `DynamoDbClient` (construction and `Dispose`), `DynamoDbClientOptions`, `AwsRegion`, all exceptions, `AttributeValue`, `AttributeType`, and the types a `Table<T>` user has to name (`ConsumedCapacity`, `CapacityDetail`, `ItemCollectionMetrics`, `CancellationReason`, `ReturnConsumedCapacity`, `ReturnItemCollectionMetrics`, `ReturnValue`, `ReturnValuesOnConditionCheckFailure`, `SelectKind`, `TableStatus`) |
| `DynaSharp.Requests` | every `XxxRequest`/`XxxResponse` record and its structure types, and the `XxxAsync(request)` methods as extension methods on `DynamoDbClient` (static class `DynamoDbRequests`) |
| `DynaSharp.Streams` | `DynamoDbStreamsClient` with its request and response records, structure types and enums, `TableStreamExtensions`, and the typed changes `Change<T>` and `MixedChange<...>` |
| `DynaSharp.Credentials` | `AwsCredentials`, `ICredentialsProvider`, providers |
| `DynaSharp.Serialization` | `DynamoDbReader`, `DynamoDbWriter`, `IDynamoDbConverter<T>` (generator- and custom-converter-facing) |

Placement rules: a type is in `DynaSharp` iff a `Table<T>` user has to name it, and all exceptions are there so that a catch clause needs no extra import, whatever API raised it. A type that only the streams operations reach is in `DynaSharp.Streams`; a type that table administration also uses (`StreamSpecification`, `StreamViewType`) stays in `DynaSharp.Requests`, so `Streams` depends on `Requests` and never the reverse. `DynaSharp.Model` does not exist.

Infrastructure: `DynaSharp.Keys` (except `KeyFormatException`, which callers catch), the metadata classes (`DynamoDbEntityInfo`, `DynamoDbIndexInfo`, `DynamoDbKeyTemplate`, `DynamoDbKeyInput`) and the converter wrappers (the list, map and set converters, `NullableConverter<,>`, `NullableReferenceConverter<,>`, `DocumentConverter<>`) are public because generated code reaches them. They are compatible only within a package version: the generator ships in the same package, so generated code follows them, and code written by hand against them has to. `SetArrayConverter<,>` and `EmptyStringAsMissingConverter<,>` are not part of this, because a property names them in `[DynamoDbConverter]`.

The public API is defined in `api.md`, the code as it stands is described in `architecture.md`. The primary API is `Table<T>` over `Item` or a generated entity, on the pipeline directly; the request API is a sibling in `DynaSharp.Requests`, not a layer beneath it.

Internal modules: HTTP pipeline, SigV4 signing, configuration resolution, operations, error mapping, telemetry.
Separate projects: `DynaSharp.Generators` (netstandard2.0, packed into `DynaSharp`), `DynaSharp.Testing`, and
`DynaSharp.Coordination` (lease with fencing and leader election; depends only on the public API of `DynaSharp`, at
that version or later, and reads the clock and the logger factory of the client through `Table<T>.Client`). `DynaSharp.Testing`
reaches internals of `DynaSharp` and so depends on exactly the version it was packed with.

Key design rules:
- **Primitives over patterns.** The core offers composable primitives (typed conditions and updates, transactions with
  markers, key grammar, fan-out query, versioning). Patterns are recipes on top; only coordination types with subtle
  failure modes and a contract that can be frozen get their own implementation: the lease and leader election.
- **One serialization path.** `DynamoDbReader`/`DynamoDbWriter` and `IDynamoDbConverter<T>` (static abstract) are the
  only DynamoDB-JSON code. `AttributeValue` is their first converter; generated entity code uses the same API. Every
  DynamoDB-JSON quirk (N as text, B as base64, `NULL: true`, no empty sets) is implemented once.
- **Contiguous pooled buffers** (`ArrayPool<byte>`) for request and response bodies; `Utf8JsonReader`/`Writer`
  directly on them. Revisit segmented buffers only if benchmarks show a need.
- **SigV4 specialized for DynamoDB** (always `POST /`, fixed header set). Signing key cached per credentials instance
  and date. `x-amz-security-token` signed when present.
- **Operations** are internal types with static abstract members (name, write request, read response); the pipeline
  is generic over them, so there are no delegates or closures per call.
- **Test seam** is `HttpMessageHandler`. No interface on `DynamoDbClient`.
- **Telemetry**: one CLIENT span per logical operation (`DynamoDB.<Operation>`); HTTP attempts are child spans from
  `System.Net.Http`. Metrics: `db.client.operation.duration`, retries/throttles, consumed capacity when requested.
  Logs only for events outside a request.

### Key grammar

Composite keys are declared as templates. The generator checks them at compile time and emits format, parse and prefix
builders on top of a small public helper, so every encoding rule exists once and generated code stays thin. The encoded
form is an on-disk contract: changing an encoding is a data migration, so golden vectors pin it. `architecture.md`
holds the implementation.

```csharp
[DynamoDbEntity]
[Discriminator("ORDER")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{CreatedAt}#{OrderId}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
public partial record Order { /* UserId, CreatedAt, OrderId, Status, Total */ }

Order.Keys.Sk.Format(createdAt, orderId);    // "ORDER#2026-10-31T12:34:56.7890123Z#4f9c2a7e-..."
Order.Keys.Sk.Prefix();                      // "ORDER#"
Order.Keys.Sk.Prefix(createdAt);             // "ORDER#2026-10-31T12:34:56.7890123Z#"
Order.Keys.Sk.UpperBound(createdAt);         // the prefix plus U+10FFFF, for an inclusive BETWEEN
Order.Keys.Sk.Parse(text);                   // SkParts(CreatedAt, OrderId); TryParse as well
```

`[PartitionKey]` and `[SortKey]` on a property declare a plain key; on the type they take a template. `[LocalIndexKey]`
takes a sort key template, and the generator checks that the index shares the table's partition key. Default attribute
names are `pk`, `sk` and, for index `GSI1`, `gsi1pk` and `gsi1sk`, overridable with `AttributeName`. Builder parameters
are named after the properties, so named arguments guard against swapping two values of one type.

Syntax: `template := (literal | "{" Property [":" format] "}")+`, with `{{` and `}}` for literal braces. Literals are
printable ASCII. A template without placeholders is legal (a `PROFILE` sort key). A property may appear in several keys
of an entity with different formats, once per template. Computed properties may feed the table's own key templates,
which are written once. They are rejected in index templates unless `[ComputedFrom]` names the stored properties the
getter reads; an update that changes one of those by handle throws, because it cannot run the getter.

| Type | Default | Formats | Width | Sort order |
|---|---|---|---|---|
| `string` | as is | `raw`, at the end of a template | variable | UTF-8 bytes, not culture |
| `Guid` | lowercase `D` | | 36 | by creation time for version 7, otherwise none useful |
| enum | member name | `d<w>` value | variable or fixed | by name or value |
| integers up to 64 bits | plain digits (`plain`) | `d<w>`, `n<i>` | variable or fixed | numeric only with a fixed format |
| `decimal` | shortest form (`plain`) | `n<i>.<f>` | variable or fixed | numeric only with a fixed format |
| `DateTimeOffset`, `DateTime` | `ticks` | `yyyy`, `yyyy-MM`, `yyyy-MM-dd`, `yyyy-MM-ddTHH`, `yyyy-MM-ddTHH:mm`, `s`, `ms`, `us`, `ticks` | fixed | chronological |
| `DateOnly` | `yyyy-MM-dd` | | 10 | chronological |
| `IUtf8SpanFormattable` + `IUtf8SpanParsable<T>` | invariant | | variable | the type's text |

`float`, `double`, `bool`, `TimeSpan` and wider integers are rejected: floats have no exact text, the others have not been
needed. `[Flags]` enums and enums with aliased members are rejected under the name format, because their names are not
one-to-one; an undefined enum value throws.

Encodings, chosen so that ordinal byte order equals value order, because DynamoDB compares strings by their UTF-8 bytes:
- `d<w>`: zero-padded to `w` digits. Non-negative only; a negative value or one too wide throws.
- `n<i>.<f>`: signed fixed point with `1 <= i`, `0 <= f`, `i + f <= 28`. A sign letter, `P` for zero and above, `N` below
  zero (`N` sorts before `P`), then `i` integer digits and `f` fraction digits around a literal point. Negative values use
  the nine's complement of every digit, so larger magnitudes sort first. With `n4.2`: `12.5` is `P0012.50`, `-12.5` is
  `N9987.49`, `0` is `P0000.00`. The sign test is `value < 0`: negative zero equals zero and is `P`, and the all-nines `N`
  form is never written. Values out of range throw, and so do values where `decimal.Round(value, f) != value`, so `12.500`
  fits `n4.2` and nothing rounds silently. `n<i>` is the same without the point, for integers.
- `plain` for `decimal`: trailing zeros dropped, negative zero as `0`, because `1.0m` and `1m` are equal and must be one key.
- Timestamps are converted to UTC and written with a fixed width: `ticks`, the default, is `2026-10-31T12:34:56.7890123Z`,
  seven fraction digits as in the attribute (`O`), and `ms` is `2026-10-31T12:34:56.789Z`, which matches keys the AWS SDK
  for .NET wrote. Every other format truncates finer precision; truncation is deliberate, it is how time buckets are expressed
  (`{At:yyyy-MM}`), so `a < b` gives `enc(a) <= enc(b)` within a bucket. Only `ticks` is lossless for `DateTime`, which is why
  it is the default: a read restores a key input from the key, `[KeyOnly]` works, and a key built from a value that was read
  from the attribute finds its item. No format keeps the offset of a `DateTimeOffset`. A `Local` `DateTime` is converted, an `Unspecified` one throws, and `Parse` returns
  `Kind == Utc`. Formats up to minutes are byte prefixes of the finer ones; `s` is not a prefix of `ms`.

Values are validated, not escaped. Escaping was rejected: a value with a separator is either a bug or an attack, because
in `TENANT#{Tenant}#USER#{Id}` an id of `1#USER#2` would address another row, and escaped keys stop being readable in the
console and in `begins_with`. No library surveyed escapes, and none applies these rules either; their failure mode is
silence. Case folding and normalization are never applied: ElectroDB's default lowercasing corrupted real data.

- The delimiters of a template are all characters adjacent to a placeholder: the last character of the literal before it
  and the first character of the literal after it. A variable-width value containing any of them throws, for every such
  placeholder including the last one, so `USER#{Id}` rejects `1#ORDER`. Fixed-width formats (`Guid`, timestamps, dates,
  `d<w>`, `n<i>.<f>`) are not checked: their alphabet and width cannot forge structure, so `ORDER-{GuidId}` is legal. A
  template that is a single placeholder has none. Use one delimiter per table: the generator cannot see other entity
  types on the same attribute.
- `raw` takes the delimiters back for a value that holds them by nature, such as `{Key:raw}` over `a#b`: the value goes in
  as it is. It round-trips only because `Parse` and a read take what is left of the text, so it is valid on a `string`
  placeholder that ends the template, with no literal behind it, and anything else is `DYNA118`. It lifts the delimiter
  check and nothing else: an empty value, a lone surrogate, U+0000 to U+001F and U+10FFFF still throw, so an upper bound
  stays above every key under a prefix. A value without a delimiter gives the same bytes as without `raw`. A format
  belongs to its template, so a strict `{Key}` of the same property in another key goes on rejecting what `raw` takes,
  and `DYNA120` warns of it. A raw input of the table key without `[KeyOnly]` is `DYNA123` (info): a `Put` stores it as
  an attribute and an `Update` does not, so only some items would hold it.
- The check runs on the formatted text of each segment, so a custom type that emits a delimiter is caught as well.
- An empty value throws: DynamoDB rejects empty key attributes, and an empty middle segment would turn a key into a prefix
  of other keys. A value that is not well-formed UTF-16 throws, because lone surrogates collapse into U+FFFD and two
  different strings would share a key. U+0000 to U+001F throw too, and so does U+10FFFF, which `UpperBound` reserves. Keys over 2048 bytes (partition) or 1024 bytes (sort)
  are left to DynamoDB to reject.
- `Format`, `Prefix` at every depth, `UpperBound`, `Of`, `Keys.Of` and `Parse` validate alike, at the call. Violations throw
  `KeyFormatException`, an `ArgumentException` naming the placeholder and never the value, so a key built from user input
  fails as a bad argument. `TryOf` returns `false` for exactly those values, without an exception.
- `Parse(x)` succeeds exactly when `Format(Parse(x)) == x`: exact widths, lowercase Guids, defined enum names, no delimiter
  in a segment.

Prefixes and ranges:
- A prefix always ends on a delimiter: `begins_with(sk, "USER#1")` would also match `USER#10`. Prefixes bind leading
  placeholders, at any depth, and cover the whole field.
- `PrefixPartial(leading..., text)` binds part of the last string placeholder, for autocomplete and geohash cells. It is
  validated like a value, and its caveat (`USER#1` matches `USER#10`) is the point of using it.
- `Prefix(createdAt)` as an upper bound excludes items at `createdAt`. `UpperBound` includes them, so
  `BETWEEN Prefix(a) AND UpperBound(b)` is inclusive on whole fields.
- The builders turn this into conditions: `Keys.Sk.Between`, `Range`, which is `BETWEEN Prefix(a) AND Prefix(b)`, `Ge`, `Gt`, `Le`, `Lt` and `Eq`. A range exists only where the bytes of the key follow the values, which `api.md` section 6 spells out.
- In a sort key a variable-width string before a delimiter breaks segment order for values containing characters below the
  delimiter: with `#` those are space, `!` and `"`, so `USER#a b#1` sorts before `USER#a#1`. `begins_with` with a whole-field
  prefix is unaffected. The generator reports that position as `DYNA115`, at Info severity.

Compile-time rules (`DYNA1xx`): syntax errors, unknown properties, unsupported types, a format that does not fit the type,
two variable-width placeholders without a literal between them (the key cannot be parsed), a nullable input in a key that
is not `Sparse`, a computed property in an index template without `[ComputedFrom]`, an entity outside an enabled nullable context (nullability
would be unenforceable; `Format` also throws on null at runtime), a `[KeyOnly]` input no key holds losslessly. Warnings
cover a number without a fixed format in a sort key, because `10` sorts before `9`, which `{Id:plain}` silences, and the
string position above.

Interaction with indexes and discriminators:
- Index keys are computed on every full write. A property used in a template stays a normal attribute, so reads never
  depend on parsing. `[KeyOnly]` on such a property stores it only in the key and restores it by parsing, which saves bytes
  and needs at least one key holding it losslessly (a timestamp in `ticks`).
- Sparse: if any input of any template of a sparse index is null, none of that index's key attributes is written, because
  one alone would be stored uselessly. A string with an `[OmitEmpty]` of its own that feeds only sparse keys counts `""` as
  null here too. An update setting such an input to null emits `REMOVE` for all of them, never a
  `NULL` value, which DynamoDB rejects on an index key.
- Overloaded indexes work because several entity types declare different templates over the same attribute names.
- Updates (M3): an update that sets a template input must also set the derived key attribute, or the index silently goes
  stale. The generated handles know the dependency; an update that lacks the other inputs of that template throws. An
  input of the table's own key cannot be updated, because DynamoDB keys are immutable, and it is known from the key the
  update was given, so a template that repeats it needs no read.
- Discriminators are a dedicated attribute (`_type`), not inferred from a key prefix: overloaded index keys and shared
  prefixes make a prefix ambiguous. `[Discriminator("ORDER")]` is required, because a default of the type name would turn
  renaming a class into a silent migration. The attribute name can be overridden, and a type can opt out for tables written
  by other tools, which also excludes it from mixed queries. A project whose tables other tools write says so once, in
  `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]`, which opts out and never makes up a value.
- JSON property order is not guaranteed, so a mixed query copies the reader (a struct, so a cheap checkpoint), finds the
  attribute, rewinds and dispatches. How a mixed result is declared is an M3 decision.
- A typed read checks `_type` when present and throws `DynamoDbMappingException` naming the type it found, because a query
  over an overloaded prefix can return other types. A typed Query or Scan can add it as a filter. Generated projections
  always include `_type`, the table key and every key that a `[KeyOnly]` property is restored from.

Recipes must note: a hash suffix needs a stable hash (`string.GetHashCode` is randomized per process), and values that
legitimately contain `#`, such as MQTT topics or the term `C#`, need another delimiter for that table.

Performance: format methods return `string` for callers, and generated write paths format into a stack buffer through
`DynamoDbWriter.WriteString(ReadOnlySpan<byte>)`, with a pooled buffer for long strings.

Tests: golden vectors for every encoding from an independent script, property tests that `a < b` implies ordinal
`enc(a) < enc(b)` and that `Parse(Format(x)) == x` for lossless formats, and scenario tests of `BETWEEN` and `begins_with`
against the emulators, which check the ordering claim against something other than this document.

### Quality

- Analyzers: `AnalysisLevel=latest-all`, StyleCop, code style enforced in build, warnings as errors.
  BannedApiAnalyzers forbid unsafe code and reflection in `src/`.
- Tests: TUnit on Microsoft.Testing.Platform. Naming `{Type}Tests` / `Method_Scenario_ExpectedResult`, AAA, FIRST.
- Gates: 100 % line and branch coverage on `src/`; Stryker mutation score >= 90 % (MTP runner, preview).
- Test layers: unit; in-memory integration (stub and recording handler, the fake); container integration;
  scenario tests across backends. Integration and scenario tests are written once and parametrized by backend:
  the in-memory fake (default locally, no Docker), Dynoxide and DynamoDB Local (in the PR pipeline, with the fake),
  real AWS (`DYNASHARP_BACKENDS=aws`, manual, through `.github/workflows/real-aws.yml`).
- External conformance material: the [Parity Suite](https://github.com/paritysuite/dynamodb-conformance)
  (Apache-2.0) is used to measure the fake's functional conformance, and its recorded AWS responses (if usable) as
  real DynamoDB JSON for serialization tests, with attribution.
- Independent oracles, so tests do not just confirm the implementation: SigV4 vectors from botocore, real DynamoDB
  JSON samples, DynamoDB Local.
- Native AOT smoke test: publishing fails on any trim or AOT warning.

### Process

- Conventional Commits, squash merges; PR titles linted. Version derived from commits (`build/next-version.sh`),
  tagged and released by CI on main. nuget.org push disabled until `PUBLISH_TO_NUGET` is set (trusted publishing).
- Dependabot: NuGet, .NET SDK, GitHub Actions. Package lock files, locked restore in CI.
- Agent workflow: the lead session owns contracts (public API, module interfaces) and `tasks.md`. Implementers take
  well-specified tasks, in separate git worktrees when working in parallel. Each milestone gets an independent review
  by a second reviewer, given the spec and the diff, not a summary.
