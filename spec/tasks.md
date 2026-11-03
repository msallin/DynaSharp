# DynaSharp - tasks (blackboard)

Shared plan and todo list. Claim a task before starting (`[~] owner`), mark it done (`[x]`) in the same change that
completes it, and record decisions and findings below. Requirements live in `requirements.md`.

Legend: `[ ]` open, `[~]` in progress (owner), `[x]` done, `[-]` dropped (reason).

## Handoff (state at 2026-10-31)

- The first branch holds M0 and the M1a implementation (B1, B2, B3, C merged).
  Verified on the merged state with `CI=true`: locked restore, format, build, 1106 tests, coverage gate 100 % line
  and branch for all of `src/`, AOT smoke publish without warnings and exit code 0. Stryker (reported by the
  packages): 97.9 % for both library projects.
- `main` does not exist yet on GitHub: create it from this branch (or from `3cdca21` for M0 only), set squash merges
  only, and let CI run once to verify the workflows on GitHub (they have never run there).
- Next steps, in order:
  1. Merge `m1/lead` work back to this branch and let CI run on GitHub.
  2. M1b (contracts and packages below), then the key grammar spike before M2.
- Local setup: SDK exactly `11.0.100-rc.3.26522.148` (`global.json`, no roll-forward; bump when .NET 11 GA ships),
  `dotnet tool restore` (Stryker 5, uses the preview MTP runner). Docker for the M1b container tests. Signing vectors
  are regenerated with `tests/DynaSharp.UnitTests/Signing/generate_sigv4_vectors.py` in a venv with `botocore`.
- Working mode that worked: lead session owns contracts and this file; implementers get one work package each, in a
  separate git worktree, with explicit gates; the lead merges.

Local verification 2026-10-31 (SDK `11.0.100-rc.3.26522.148`, `CI=true`): on Windows locked restore, format, build,
1106 tests, coverage 100 % line and branch, pack, Stryker 97.94 %; in an Ubuntu 24.04 container locked restore, build,
1106 tests, coverage 100 %, AOT publish `-r linux-x64` without warnings, smoke test exit 0. Client benchmark below.

## M0 - repository and quality gates

- [x] Repository skeleton, analyzers, editorconfig, gitattributes, gitignore, global.json, central packages, lock files
- [x] TUnit + coverage gate (100 %), Stryker (break 90, MTP runner), Native AOT smoke test, BenchmarkDotNet
- [x] CI, release workflow (version from Conventional Commits), Dependabot
- [ ] Merge M0 to `main`; set repository to squash merges only; verify CI and release workflow on GitHub

## M1a - thin vertical slice (static/env credentials, GetItem, PutItem, DescribeTable)

The public contract types are committed and build; `AttributeValue` is a stub. Until M1a is complete the branch
fails the coverage gate, so `main` stays at the last green commit. Work packages B1-B3 can run in parallel (separate
worktrees); C follows after they are merged.

- [x] Public contracts: `AttributeType`, `AttributeValue` (stub), low-level models (GetItem, PutItem, DescribeTable,
      ConsumedCapacity, enums), `AwsCredentials`, `ICredentialsProvider`, `StaticCredentialsProvider`,
      `CredentialsUnavailableException`, exceptions, `DynamoDbClientOptions`
- [x] **B1 Serialization** (merged; Stryker 96.6 % on its files) (`DynaSharp.Serialization`, internal buffer)
  - `PooledByteBuffer` (internal, `IBufferWriter<byte>`, `ArrayPool<byte>`, grows by doubling, `Dispose` returns the
    array, can read a response stream with known or unknown length)
  - `DynamoDbWriter` (public `readonly ref struct` over `Utf8JsonWriter`): writes complete attribute values
    (`WriteString`, `WriteNumber<T>` with NaN/Infinity rejected, `WriteNumber(string)`, `WriteBinary`, `WriteBool`,
    `WriteNull`, start/end list, map, string/number/binary set, set elements, start/end item, attribute names as
    `ReadOnlySpan<byte>` and `string`)
  - `DynamoDbReader` (public `ref struct` over `Utf8JsonReader` on a contiguous span). Cursor rule: every
    `Read*`/`TryRead*` advances first, then checks the token. `ReadStartAttribute()` consumes `{` and the type
    descriptor and returns the `AttributeType`, positioned on the value; `ReadEndAttribute()` consumes `}`.
    `TryReadStartAttribute(out type)` for list elements (false at `]`); `TryReadAttributeName()` for maps/items
    (false at `}`), `AttributeNameEquals(ReadOnlySpan<byte>)` (handles escaping), `GetAttributeName()`;
    `TryReadSetElement()`; value getters `GetString`, `GetNumber<T>` (`IUtf8SpanParsable<T>`, invariant),
    `GetNumberText`, `GetBinary`, `GetBool`; `SkipAttributeValue()`; `ReadStartItem()`. Shape errors throw
    `JsonException`.
  - `IDynamoDbConverter<T>`: `static abstract void Write(DynamoDbWriter, T)` writes a complete value;
    `static abstract T Read(ref DynamoDbReader, AttributeType)` reads the value the reader is positioned on, caller
    consumes the closing brace.
  - `AttributeValue` implementation (struct over type + object; deep equality; sets order-independent;
    `ToString` = DynamoDB JSON) and internal `AttributeValueConverter` incl. `WriteItem`/`ReadItem`.
  - Tests incl. real DynamoDB JSON samples from the AWS docs; benchmarks for item round trips.
- [x] **B2 Signing** (merged; botocore vectors, Stryker 100 %) (internal `SigV4Signer`)
  - `Sign(HttpRequestMessage, ReadOnlySpan<byte> payloadSha256, AwsCredentials, DateTimeOffset utcNow)` sets
    `X-Amz-Date`, `X-Amz-Security-Token` (if any) and `Authorization`. Signed headers:
    `content-type;host;x-amz-date[;x-amz-security-token];x-amz-target`. Host = `RequestUri.Authority`
    (port only if non-default, needed for DynamoDB Local).
  - Signing key cached per (credentials instance, date). One-shot `HMACSHA256.HashData`, `SHA256.HashData`,
    `Convert.TryToHexStringLower`, stack buffers.
  - Test vectors generated independently with botocore (`SigV4Auth`) for: long-term keys, session token, non-default
    port, date rollover. Generator script in `tests/` so vectors are reproducible.
- [x] **B3 Configuration and credentials** (merged; Stryker 100 %)
  - `EnvironmentCredentialsProvider` (public; reads `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`,
    `AWS_SESSION_TOKEN` once, caches the instance; internal constructor takes an environment accessor for tests;
    missing values throw `CredentialsUnavailableException` naming the variables)
  - Internal `ClientConfiguration.Resolve(options, environment)`: region, endpoint (option, `AWS_ENDPOINT_URL_DYNAMODB`,
    `AWS_ENDPOINT_URL`, region default; only absolute http/https), credentials default, timeout validation
  - Tests for all contract types written in "public contracts" (exceptions, credentials, options, models)
- [x] **C Pipeline and client** (merged; Stryker 97.9 %; benchmark numbers still to record)
  - Internal operation abstraction (`IOperation<TRequest, TResponse>` with static abstract `Name`, table name for
    telemetry, `WriteRequest`, `ReadResponse`); GetItem, PutItem, DescribeTable operations
  - Pipeline: serialize once, hash once, per attempt: build request (`Content-Type: application/x-amz-json-1.0`,
    `X-Amz-Target: DynamoDB_20120810.<Op>`), sign, send with `ResponseHeadersRead` and per-attempt timeout
    (`TimeoutException` vs caller cancellation), read body into pooled buffer, parse or map error
  - Error mapping: `__type` (after `#`) and `message`/`Message` to exceptions; `ConditionalCheckFailed` with item;
    non-JSON bodies (503); request id from `x-amzn-RequestId`
  - `DynamoDbClient` (sealed, `IDisposable`, owns its `SocketsHttpHandler` with `PooledConnectionLifetime` unless a
    handler was given)
  - Telemetry: `ActivitySource`/`Meter` "DynaSharp", span `DynamoDB.<Op>` with AWS/DynamoDB semconv attributes (pin
    the semconv version, verify names against the spec), `db.client.operation.duration`; `[LoggerMessage]` logs for
    resolved region/endpoint/credentials source. Tests filter by trace id / unique table name (FIRST).
  - `DynaSharp.Testing` project with `StubHandler`; coverage config and Stryker include it
  - AOT smoke test exercises a stubbed GetItem; benchmarks for GetItem against canned responses
  - From B2: build request content as `ByteArrayContent` with `new MediaTypeHeaderValue("application/x-amz-json-1.0")`;
    the signer rejects any other content type (for example a `charset` parameter from `StringContent`).
  - From B3: `AttemptTimeout` above ~49.7 days passes `Resolve` but fails in `CancelAfter`; add an upper bound in
    `ClientConfiguration.Resolve`. Reject `ServiceUrl` with user info (it could leak into telemetry). The credentials
    source must be logged by C (providers have no logger).
- [x] M1a independent review (second reviewer, spec + `src/` diff, verified in a throw-away worktree): no critical
      findings; list below
- [x] Fix the M1a review findings (R1-R13 from the first review, R14-R19 from a second independent review)

### M1a review findings

All closed. Notes after the arrow say how, where it differs from the finding.

Major (fix first):
- [x] R1 Wire-accurate error codes: `InternalServerErrorException` and `RequestLimitExceededException` report
      `InternalServerError` / `RequestLimitExceeded` (DynamoDB, botocore and the AWS SDK for .NET use the unsuffixed
      code; class names keep the suffix). Remove the suffixed aliases in `Errors/ErrorMapper.cs`; replace the
      self-confirming test `DynamoDbExceptionTests.ErrorCodeIsTheTypeName` with a table driven by real error bodies
      (`ErrorMapperTests`). Matters for `error.type` and the M1b retry classification.
- [x] R2 Typed exceptions keep the real HTTP status: derived constructors take an optional `HttpStatusCode`, the
      mapper passes it through (today an `InternalServerError` with 503 reports 500). Do together with R1.
- [x] R3 Custom converters must be testable by users: make `DynamoDbWriter(Utf8JsonWriter)` and
      `DynamoDbReader(ReadOnlySpan<byte>)` public (document flushing before reading), or add equivalent helpers.
      Decide before M2 builds on the contract. -> Both constructors are public; a test round-trips a converter without internals.

Minor:
- [x] R4 `ClientConfiguration.Resolve`: reject a null `TimeProvider` (today it fails on the first request).
- [x] R5 Truncated response bodies surface as `IOException` (`EndOfStreamException`/`HttpIOException`), not the
      documented `HttpRequestException`: wrap in `ReadBodyAsync` or document.
- [x] R6 Docs: remove "retried automatically" from `InternalServerErrorException` and
      `RequestLimitExceededException` until M1b; `AttemptTimeout` docs must say the credential lookup is inside the
      timed section. -> Four exception types now say "Retryable.". The retry package restores the stronger wording.
- [x] R7 Telemetry: add `db.response.status_code` to the metric and span when a DynamoDB error code is known
      (verify against semconv v1.44). -> Omitted for `UnrecognizedResponse`, which is the client's code, not DynamoDB's.
- [x] R8 Allocations (verify with the client benchmark first): reuse one `Utf8JsonWriter` per pipeline via
      `Reset`; read `x-amzn-RequestId` lazily (only on error or when the activity records); cache the
      `X-Amz-Date` string per second in the signer. -> Measured (GetItem, 5.32 KB before): skipping the linked token
      source for uncancellable calls saves 200 B, done. Writer reuse saves 140 B (2.7 %) and adds per-thread state; the
      date cache saves at most 56 B and adds shared state; the request id allocates nothing extra because the header
      string already exists. Those three not done; revisit with the EfficientDynamoDb comparison.
- [x] R9 One internal constants type for `X-Amz-Target` prefix and content type (today duplicated in signer,
      pipeline and `DynaSharp.Testing`; add `InternalsVisibleTo DynaSharp.Testing`). -> `Http/WireProtocol.cs`. The signer's `u8` canonical
      prefix stays a literal; `ValidateRequest` ties it to the constant.
- [x] R10 `Operations/OperationJson.cs`: use the reader's advance-and-check instead of discarding `json.Read()`, so
      truncated envelopes report "unexpected end".
- [x] R11 `DescribeTableOperation`: missing `TableName` must fail instead of becoming `""`; do not expose mutable
      `List<T>` as `IReadOnlyList<T>`. Add `ReturnConsumedCapacity.Indexes` and `ReturnItemCollectionMetrics` with
      M1b (public enums are contract). -> `Indexes` done with R15; `ReturnItemCollectionMetrics` goes to the M1b operations.
- [x] R12 `WriteNumber<T>` docs: state DynamoDB's number range (1E-130 to 9.99E+125, 38 digits); check whether
      DynamoDB accepts `-0`. -> Docs done; `-0` and the exponent forms `WriteNumber<double>` emits go to the container tests.
- [x] R13 Tests touching the process environment must all carry the same `[NotInParallel]` key (today
      `DynamoDbClientTests`); keep it that way for every future test using `new DynamoDbClient()` or
      `new EnvironmentCredentialsProvider()`. -> Key `TestClient.ProcessEnvironment`, rule in `AGENTS.md`; tests that set every
      option no longer read the machine's variables.

Second review (additional findings):
- [x] R14 `DynamoDbNumber`: `NumberStyles.Float` accepted surrounding whitespace; now sign, decimal point and exponent only.
- [x] R15 `ReturnConsumedCapacity.Indexes`, `Capacity`, and `ConsumedCapacity.Table` / `LocalSecondaryIndexes` /
      `GlobalSecondaryIndexes` (read per index, which is why capacity is asked for at all).
- [x] R16 `TableStatus.ReplicationNotAuthorized` was missing from the enum (it mapped to `Unknown`).
- [x] R17 `User-Agent: DynaSharp` on every request, unsigned. No version: reading it needs reflection, which is banned.
- [x] R18 Tests: tautological tuple element removed; pipeline signature tests labelled as wiring checks, the signer's oracle
      is botocore in `SigV4SignerTests`.
- [-] R19 `X-Amzn-Errortype` fallback for bodies without `__type`: botocore ignores the header for this protocol too,
      and such bodies already surface as `UnrecognizedResponse`.

## M1b - complete wire layer

Contracts (lead):
- Models mirror the service model in botocore (`dynamodb`, `dynamodbstreams`): member names as there, `sealed record`,
  `required`/`init`, read-only collection interfaces, XML docs with DynamoDB semantics. Legacy parameters are left out
  (`AttributeUpdates`, `Expected`, `ConditionalOperator`, `KeyConditions`, `QueryFilter`, `AttributesToGet`, `ScanFilter`).
  Enums read from the wire have `Unknown` as the default, so a new service value does not fail a response.
- `DynamoDbClient` gets one `<Operation>Async(request, cancellationToken)` per operation, same shape as `GetItemAsync`.
- `DynamoDbClientOptions.MaxAttempts` (`int`, default 3, 1 means no retries, validated in `ClientConfiguration.Resolve`)
  is the only public retry setting. The policy is internal.
- `DynamoDbClientOptions.Credentials == null` means the default chain: environment, shared files, container endpoint,
  IMDS. New public types in `DynaSharp.Credentials`: `DefaultCredentialsProvider`, `ProfileCredentialsProvider`,
  `ContainerCredentialsProvider`, `InstanceMetadataCredentialsProvider`. Their HTTP goes through an internal constructor
  taking a handler, never through `DynamoDbClientOptions.HttpHandler` (that seam belongs to DynamoDB traffic).
- `DynamoDbStreamsClient` is a separate public sealed client with the same `DynamoDbClientOptions` (other host, same
  signing name `dynamodb`). `IOperation` gains `static virtual string TargetPrefix`; streams operations override it.
  Endpoint: option, `AWS_ENDPOINT_URL_DYNAMODB_STREAMS`, `AWS_ENDPOINT_URL`, region default.
- `RecordingHandler` is a public `DelegatingHandler` in `DynaSharp.Testing`.
- Transactions: member names as in the service model, but the structure types are `TransactGet`, `TransactPut`, `TransactDelete`,
  `TransactUpdate` and `TransactConditionCheck`: botocore's `Get`, `Put`, `Delete` and `Update` are too generic for public C# types.
- `IOperation.GetTableName` returns `string?`: null when a request spans several tables or none. `db.collection.name` is then
  omitted; `aws.dynamodb.table_names` comes from `SetActivityTags`.

Work packages, implementers in separate worktrees, all in parallel; the lead resolves the overlaps at merge. Gates per package: build,
format, tests, 100 % coverage; the lead runs the Linux AOT gate at merge. Stryker runs before the milestone closes.

- [x] **P1** (merged; classification from botocore's `retries/standard.py`) Retries: standard semantics, 3 attempts, full jitter, 25-50 ms base, 20 s cap, throttling vs transient
      classification, `TimeProvider`; clock-skew correction
- [x] **P2** (merged; 138 botocore file pairs as vectors) Credentials chain: shared files (`AWS_PROFILE`, `AWS_SHARED_CREDENTIALS_FILE`, `AWS_CONFIG_FILE`, `[profile x]`
      vs `[x]`, `aws_session_token`, `region`; profiles with `role_arn`/`credential_process`/`sso_*` fail loudly);
      container endpoint (`RELATIVE_URI`, `FULL_URI` + authorization token/token file); IMDSv2
      (`AWS_EC2_METADATA_DISABLED`, endpoint override, 1 s probe, single attempt, v1 fallback); pre-expiry refresh,
      single flight; logs for provider chosen and refresh failures
- [x] **P3** (merged; 43 request and 23 response vectors from botocore's serializer and parser) Operations: UpdateItem, DeleteItem, Query, Scan; `ReturnItemCollectionMetrics`,
      `Select`, `ItemCollectionMetrics`; `DynamoDbReader.TryReadStartItem()` for `Items` arrays (so envelopes need no reader copy)
- [x] **P4** (merged; 72 request, 50 response and 11 error vectors) Operations: BatchGetItem, BatchWriteItem, TransactGetItems, TransactWriteItems (auto `ClientRequestToken`);
      exceptions (`TransactionCanceled` with reasons, `TransactionInProgress`, `IdempotentParameterMismatch`,
      `TransactionConflict`, `ReplicatedWriteConflict`)
- [x] **P5** (merged) Operations: CreateTable, DeleteTable, UpdateTimeToLive
- [x] **P5** (merged; 105 request, 90 response vectors across all operations) Streams: ListStreams, DescribeStream, GetShardIterator, GetRecords
- [x] **P6** (merged) Response tags and capacity metric: `IOperation` gains a response hook, so spans carry `aws.dynamodb.count`,
      `scanned_count` and `consumed_capacity`, and consumed capacity feeds a metric when requested. Item collection
      metrics stay out: they contain the partition key value.
- [x] **P6** Pool hardening: `DynamoDbWriter` long-number fallback uses `DynamoDbNumber.Format` instead of the shared pool;
      `AttributeValueConverter` builder disposal covered by a counting pool (today a missing `Dispose` would go unseen)
- [x] **P6** `RecordingHandler`; benchmarks vs EfficientDynamoDb and AWSSDK.DynamoDBv2 (benchmark-only references)
- [x] **P7** (merged; 215 tests per backend) Container integration tests (Testcontainers), written once and parametrized by backend: Dynoxide
      (`ghcr.io/nubo-db/dynoxide`, default locally) and DynamoDB Local (additionally in the PR pipeline). Images
      pinned in Dockerfiles so Dependabot sees them. One instance per test class or unique tables per test (FIRST).
      Known Dynoxide gaps: `ConsistentRead` is a no-op, streams have one shard, no `TransactionConflict` /
      `TransactionInProgress`; mark affected tests per backend.
- [x] **P7** Check the Parity Suite's `ground-truth/` and `captures/` for recorded AWS responses; if usable, add them as
      serialization test data with Apache-2.0 attribution (NOTICE).
- [~] **P11** (branch `m1b/p11-review-fixes`) M1b review findings, see "M1b review findings" below
- [x] **P12** (merged; 97.22 % on the last full run, the rest equivalent or Stryker noise) Stryker survivors
- [x] **P13** (merged; pipeline 489 to 347 lines, 4 bugs found) Refactoring pass over M1b
- [x] **P14** (merged) API friction that does not depend on the layers decision
- [x] **P10** (merged) Runnable sample `samples/DynaSharp.Sample` against a local emulator, public API only; it moves to the fluent API with M3
- [~] M1b independent review

### M1b review findings

Independent review, no Critical or High. Decisions by the lead:
- [~] M1 Response bodies get a hard ceiling of 64 MB (above the 16 MB of BatchGetItem); beyond it the call fails.
- [~] M2 Enum convention: wire value `NONE` is member `None`. Optional enums a caller can send use `Unspecified` = 0 (sends nothing; on a response,
      a value this library does not know). Response-only and required enums use `Unknown` = 0.
- [~] M3 Generic public names go: `Record` becomes `ChangeRecord`, `Identity` `RecordIdentity`, `Tag` `TableTag`, `Projection` `IndexProjection`,
      `Capacity` `CapacityDetail`. Member names stay as in the service model.
- [~] M4 One default provider per client stays. The docs tell users on EC2 and ECS to share one provider between clients.
- [~] M5 `RefreshingCredentialsCache`: log by the window at completion, so a mandatory waiter never reads "stay in use"; a throwing logger cannot escape.
- [~] M6 One wire-name table per enum, used for reading and writing, not mutable and not public.
- [~] L1 `MaxAttempts` defaults to 4. With 3 a throttled call gave up after at most 150 ms; aws-sdk-java-v2 uses 4 attempts for DynamoDB.
- [-] L2 A skew resend stays inside `MaxAttempts`: 1 must mean one request.
- [~] L3 `ReadBodyAsync` wraps `IOException` only when the token is not cancelled.
- [~] L4 Add `rpc.system.name = aws-api` beside the deprecated attributes; `aws.dynamodb.count` on Query is a deliberate superset.
- [~] L5 `GetTableName` no longer boxes an enumerator for dictionaries.
- [~] L6 `AdvanceThroughDelaysAsync` takes the expected timeout explicitly.
- [~] L7 Retry, throttle and capacity counters carry `server.address`, as the duration metric does, and tests without a table isolate on it.
- [~] L8 One generic measurement capture in the tests instead of three.
- [-] L9 Dropped: botocore raises `InvalidConfigError` for that profile, so failing loudly already matches it. The review was wrong here.
- [~] L10 Say why the skew code list holds codes DynamoDB's protocol does not send: proxies and emulators.

## M1c - core for the table API

Order: C1 first, alone, because it touches every file. C2 and C3 then in parallel.

- [x] **C1** (merged) Namespaces per `history/api-design.md` section 2: `DynaSharp`, `DynaSharp.Requests` (records, and `XxxAsync(XxxRequest)` as extension methods on `DynamoDbClient`), `DynaSharp.Streams`, `DynaSharp.Expressions`; `DynaSharp.Model` dissolved
- [x] **C2** (merged; Query 98.85 KB, GetItem 3.37 KB, `AttributeNameTable` removed on the churn benchmark) `Item` and `ItemShape` behind the existing `IReadOnlyDictionary` signatures; getters, `ToString`, debugger proxy, `From`, `To<T>`; `char` blocked; shape-churn benchmark decides the fate of `AttributeNameTable`
- [x] **C3** (merged; `IWireCall<T>` is the seam for commands) HTTP floor from `history/api-layers-proposal.md` section 6, one retry loop, `SendRawAsync`, a real operation name in its telemetry

## M2a - commands

The `Table<T>` API of `history/api-design.md` section 1.3 on `Item`, ported from `src/DynaSharp/Spike` on `spike/api-layers`, to full gates. A1 first (it defines the engine), then A2 to A4 in parallel.

- [x] **A1** (merged; build phase 0 B, Put 1.49 KB, Get 2.52 KB untyped, 2.16 KB typed) Command engine (`CommandState`, segments, expressions, conditions, `Expr`), `Table<T>`, `Index<T>`, Get, Put, Update, Delete, return values, composition interfaces, per-call options, `ToRequestJson`, `ValidationException` with rendered expressions, stress test for pooled state
- [x] **A2** (merged; Query 100 items 97.95 KB untyped, 69.8 KB typed, 0 B per page of our own) Query, Scan, parallel scan, paging, `ContinuationToken`, `CountAsync`, `PagesAsync`, `DescribeAsync`, `WaitUntilActiveAsync`
- [x] **A3** (merged; transaction of 2 puts 2.12 KB untyped, 1.49 KB typed, batch of 25 6.38 KB, 1.49 KB) BatchGet, BatchWrite (bounded retry through `TimeProvider`, parsed `BatchIncompleteException`), TransactGet, TransactWrite with steps, markers, slots, `.Unique`
- [x] **A4** (merged; final table in Findings) `DynaSharp.Testing` typed helpers, benchmarks of the command API against the proposal's numbers. The sample moves to `Table<T>` in G8

## M2 - source generator (M2b)

Work packages G0 to G9 in `history/generator-design.md` section 10, with its amendments. The compiler spikes of G0 are done (`history/g0-spike-report.md`); G0's contracts remain.

- [x] **G1** (merged) Generator project and packaging
- [x] **G5** (merged; 0 B reading a struct of 12 value properties, 56 B for a class, 0 B writing) Entity emitter
- [x] **G6** (merged; 0 B to build a typed key) Key emitter, `Attrs` handles
- [x] **G4** (merged; 37 diagnostics) Model, parser, binder, converter resolver, all diagnostics, attributes
- [x] **G2** (merged; `DocumentConverter<T>` waits for `IDynamoDbDocument<T>`) Built-in converters
- [x] **G3** (merged; 1165 independent vectors, 10,000-sample properties, ordering checked on both emulators) Key encodings: `KeyWriter`, `KeyReader`, `KeyFormatException`, independent vectors
- [x] **Spike before M2: key grammar design** (done, see "Key grammar" in `requirements.md`). Template syntax, padding and number encoding, escaping, parse and
      prefix builders, interaction with GSIs and discriminators. Result: a design section in `requirements.md`.
      It shapes the generator API, so M2 starts after it.
- [ ] `DynaSharp.Generators` project (netstandard2.0, packed into `DynaSharp`); `[DynamoDbEntity]`, `[PartitionKey]`,
      `[SortKey]`, `[DynamoDbAttribute("name")]`, `[DynamoDbIgnore]`, `[DynamoDbConverter(typeof(X))]`
- [ ] Generated `IDynamoDbEntity<T>` read/write/key extraction against `DynamoDbReader`/`DynamoDbWriter`; UTF-8 name
      matching: the reader exposes the unescaped name bytes so generated code can switch on length and content
      instead of calling `AttributeNameEquals` per property
- [ ] Built-in converters (primitives, string, Guid, DateTime/DateTimeOffset ISO 8601, TimeSpan, enums, binary,
      nullable, list/array, sets, dictionary, nested entities)
- [ ] Key templates on keys and GSI keys; generated format / parse / prefix builders; public helper for the encodings,
      with golden vectors because the encoded form is an on-disk contract
- [ ] Single-table: multiple entity types per table, discriminator, multiple GSIs, sparse GSI attributes
- [ ] `[Version]` attribute (optimistic locking metadata); TTL epoch-seconds converter
- [ ] Diagnostics `DYNA001`.. (unsupported type, missing partition key, non-partial, name collisions, invalid key
      template)
- [ ] Snapshot tests (Verify.SourceGenerators) and compile-and-run round trips
- [~] M2 independent review (together with M3)

## M3 - fluent API

Feature complete on `m1/lead`. Both M2 and M3 stay open for one pass before the first push: Stryker (>= 90 %) and the AOT publish, paused by the owner. The last AOT run was before R1, R2 and R3.

Contracts: `history/api-design.md` section 9. Packages: **M3a** (merged) `[Version]`, template inputs, typed `.Unique`, generator findings of G5 and A2. **M3b** (merged) mixed-type reads, fan-out query. **A5** (merged) polish after the sample. **M3c** (merged; 36 patterns, 105 tests on each emulator) recipes. **R2** (merged) findings of the recipes. **R3** (merged) findings of the second review. **R1** (merged) findings of the independent review.

- [x] `client.Table<TEntity>()`; generated attribute handles in a sibling type, nested paths, raw-name escape hatch
- [x] Conditions, update expressions, placeholder generation with dedup
- [x] Query/Scan: key conditions limited to valid operators, filter, projection, index, consistent read; pagination
      on `LastEvaluatedKey` (empty filtered pages), `Limit` per page, `Page<T>` with raw key and opaque continuation
      token, `[EnumeratorCancellation]`, parallel scan
- [x] Batch (100 keys / 16 MB get, 25 items / 16 MB write, 400 KB item) with bounded unprocessed retry;
      transactions (100 actions / 4 MB) with per-action reasons
- [x] Automatic optimistic locking for `[Version]` entities on every write path (put, update, delete, transactions)
- [x] Typed `ReturnValues` on updates (for example the counter value after `ADD`)
- [x] Transactions: raw marker items, `.Unique(...)` helper, cancellation reasons mapped to the actions added
- [x] Mixed-type query results via discriminator
- [x] Fan-out query over a set of partition keys, ordered k-way merge by sort key, composite continuation token
- [x] Scenario tests, including the recipes from `patterns.md`
- [~] M3 independent review (done by a second reviewer after the A5 merge: no Critical or High, nine findings, decisions in `history/api-design.md` section 9. Not reached: bodies of several emitters, serialization, telemetry, credentials; integration tests were not run by it)

### G1 - additions from the three services

Owner's decision of 2026-11-01. One package, branch `m3/g1-additions`.

- [x] **Version 0 checks the key.** Every version 0, on `Put`, `IfVersion(0)`, `Delete(entity)` and transaction steps, renders `attribute_not_exists(<partition key>)` and no longer looks at the version attribute, so a first write fails on an existing item without a version instead of replacing it. With `IfNotExists()` the clause appears once. `IgnoreVersion()` is how such an item is taken over. Breaking.
- [x] **One item from a query.** `Table<T>.Query<TKey>(TKey key)` takes what `Get` takes, and queries the partition key with `Eq` on a sort key if there is one. `QueryCommand<T>.ItemAsync(ct)` gives the first matching item in query order or null. It follows empty filtered pages and stops at the first item. It adds no `Limit`, because `Limit` works before the filter and what goes out stays what `ToRequestJson()` shows; the docs show `.Limit(1)` for a read of a partition without a filter. Index queries get the terminal too. Not on fan-out, mixed reads or scans.
- [x] **`SetIfNotNull`.** `SetIfNotNull(handle, value)` on `IUpdatable<TSelf>`, by handle only, for reference and value types. With a value it is the `Set`, including the rules of section 8 of `api.md`. With null it adds nothing.
- [x] **Describe by name.** `DynamoDbClient.DescribeTableAsync(string tableName, CancellationToken ct = default)`, which returns what the request overload returns.
- [x] **`TableNotActiveException : TimeoutException`.** `WaitUntilActiveAsync` throws it, on `Table<T>` and `CreateTableCommand`, with `TableName` and `LastStatus` (`TableStatus?`, null when the table was not found). Sealed, in `DynaSharp`. A `catch (TimeoutException)` keeps working, and `AttemptTimeout` still gives the plain type.
- [x] **`[OmitEmpty]`.** One attribute for a property or an entity or document type. On a type it covers the `string?` properties of that type that feed no key, and nested types have their own. An empty string is written like null: left out by `Put` and `Item.From`, a `Remove` in `Set` by handle, nothing in `SetIfNotNull`, and rejected by `SetIfNotExists` by handle like null is. A stored `""` still reads as `""`. On a property that is not `string?`, or that feeds a key, it is an error with a diagnostic of its own. Project-wide defaults come later and set the same thing.
- [x] **`Options` on the clients.** `DynamoDbClient.Options` and `DynamoDbStreamsClient.Options` return a `DynamoDbClientOptions` holding what the client really uses, so a region or an endpoint from the environment is there and not null. `client.Options with { MaxAttempts = 1 }` makes a second client.
- [x] **`WaitUntilActiveAsync` waits for the indexes.** It returns when the table and all its global secondary indexes are `ACTIVE`, because a table reads `ACTIVE` while an index backfills. `TableNotActiveException` gets `PendingIndex` (`string?`), the index that was not active when the time ran out. There is no `WaitUntilIndexActiveAsync`.

### G2 - typed projection

Branch `m3/g2-projection`, merged. `Project` stays as it is: leaving out what the entity needs throws `DynamoDbMappingException`.

- [x] **Analyzer.** `DYNA208` warns when a typed read that ends in the same chain leaves out what it would throw without: a `required` member that does not read as null or empty, a constructor parameter without a default, the input of a lossy key. `DYNA209` warns on `Select` of a stored input of a table key, which throws, because an item made by `Update` does not hold the attribute.
- [x] **`Select(handle)`.** On `GetCommand<T>`, `QueryCommand<T>` and `ScanCommand<T>`: one handle, projects that attribute and reads its value, without the entity. `Return(attr)` is the model: clauses go before it and the command that comes back has terminals only. `SelectResult<TValue>` tells a missing item from a missing attribute. A missing attribute follows the type of the handle: null for a nullable one, `DynamoDbMappingException` otherwise, and empty for a set. The handle of a nullable property carries that in its converter. More than one handle is a later step.
- [x] **Typed `Get` and `BatchGet` with `Project`** add the discriminator and the keys, as `api.md` says, which they did not.

### D1 - web identity and real AWS

Branch `m3/d1-web-identity`, merged. Never run against AWS, because no role exists yet.

- [x] `WebIdentityCredentialsProvider` at botocore's place in the chain, for IRSA. Checked against vectors from botocore's serializer and parser. `AWS_STS_REGIONAL_ENDPOINTS=legacy` uses the global endpoint only for botocore's `LEGACY_GLOBAL_STS_REGIONS`.
- [x] The `aws` backend of the integration tests and `.github/workflows/real-aws.yml` on `workflow_dispatch`, which take their role from the repository variable `AWS_ROLE_ARN`. The header of the workflow says what the role needs.
- [ ] First run on real AWS, which needs the role. It checks the STS exchange, the renewal loop of the workflow, and the settling times in `SettlingHandler`. Then IRSA on EKS in a real service.

### H1 and H2 - review of the code at G2, and the last changes of the public shape

Three independent readers on runtime, generator and maintainability found nothing Critical or High. Branches `m3/h1-review-fixes` and `m3/h2-public-shape`, merged.

- [x] **H1, runtime.** `IfNotExists` over a `DELETING` table rethrows, and a table `DescribeTable` does not find is created again, five times at most over 1.5 seconds, because it is either gone or another process just created it. A `decimal` read throws `OverflowException` unless it holds the exact value. Floating-point reads throw on a value that is not finite on every path, in `DynamoDbNumber.Fits`. A batch that fails after a request went through reports its progress in the incomplete exception, with the failure as `InnerException`. A lone surrogate is an `ArgumentException` in a value, a set and an attribute name, and U+FFFD in `ToString()`, which is what someone reads while finding out why. A parallel scan stops the segments it started when a later one cannot start.
- [x] **H1, generator.** `DYNA007` on a `file` type, `DYNA203` only over the fluent interfaces, `DYNA204` through deconstruction, compound assignment and `++`, `DYNA216` on `[Flags]` enums in the method form too, and a `DYNA202` message that holds for every read. Agreement tests hold the lists of the analyzers and the terminals of `Select` against what they stand for.
- [x] **H2.** `SortKeyCondition`, `WriteResult.Item`, stream images as `Item`, `Keys.Sk.Eq/Between/Ge/Gt/Le/Lt` on every sort key whose bytes follow its values, a version byte in continuation tokens, a transient base of 100 ms, `Take(n)` in the docs of `Limit`, and the default names frozen in `api.md`. A typed update writes no key input by default, as checked.
- Decision: `Between` on a key with several fields takes `(leading..., from, to)` and not two whole tuples. Only this form holds for every template, because the keys between two bounds with a common start have that start. A range across fields is written by hand from `Prefix` and `UpperBound`.
- Decision: `TryReadMissing` stays visible, because a custom set converter needs it: an empty set is a missing attribute. Its remarks mark it as infrastructure.
- Finding: `Parse` of a token has no limit on length, on input that comes from a client. "Bounded fan-out tokens" under later work covers it.
- [x] **I1, what the three services found on preview 2.** Branch `m3/i1-app-fixes`, merged. `IsOmitted` of a custom converter holds on every entity write, and a property that feeds a key is always written. `AWS_ENDPOINT_URL_STS` and then `AWS_ENDPOINT_URL` are read as botocore does, and a loopback endpoint skips the proxy, which is what lets `WebIdentityEndToEndTests` run the success path over a real socket with bodies from botocore. A missing `required` member names the key that could have restored it. `DYNA209` also covers `Select` of a non-nullable `[Version]`. The docs say what the services had to find out.
- Open after I1: real STS and IRSA, and the decisions on the renames and on what the services still write by hand.
- Not reached by the review: `Mixed*Command` bodies, `TransactGetCommand`, `CommandDebugger`, `ExpressionLexer` and `ExpressionParser`, `WrapInAttrCodeFix`. Unchecked on real DynamoDB: `-0` and `1E-07` as `N` text, and `GetItem` of a projection that matches nothing, which an emulator answers with `Item:{}`.

### J1 to J5 - the renames, and what the services still write by hand

All the additions the lead proposed after preview 2, approved by the owner. Each package has a branch `m3/j*` from `m1/lead`.

- [x] **J1, renames.** `Select` to `SelectKind`, the `Select` terminals to `ValuesAsync` and `ValueAsync`, the analyzers no longer key on a method name, `NullableHandleConverter` and `NullableReferenceHandleConverter`, and `PageAsync(cancellationToken)` without `default` in front.
- [x] **J2, tables and versions.** `[Version(Missing = VersionMissing.Allow)]`, `IfNotExists` that describes first, `Stream` and `TimeToLive` on `CreateTable()`, a wait for the global indexes the entities declare, `RetryOnConflictAsync` on a table, and "Adopting an existing table" in the README.
- [x] **J3, project defaults.** Naming policy, discriminator opt-out and `[OmitEmpty]` per assembly, a converter per type, `[OmitEmpty]` on non-nullable strings and collections, and `DYNA117` as a warning.
- [x] **J4, keys.** `{Key:raw}`, `TryOf`, and `Keys.Sk.Range(from, to)`.
- [x] **J5, test helpers.** `StubResponse` for table administration, counts and streams, resolved expression text and transaction steps on `RecordedRequest`, and `StubClient.Create`.

### K1 to K4 - what the review of the open items decided to do

The owner had an independent reviewer judge what the services still asked for, and asked to build what it recommends.

- [x] **K1, `IDynamoDbConverter<T>.CountsAsMissing`.** A converter can say that a stored value counts as missing, which gives a lenient enum and a blank string as null. Merges after J1 and J3.
- [x] **K2, `ConditionalCheckFailedException.Key`.** The request knows the key, so an exhausted retry can carry it. Never in a message, `ToString()`, a log or telemetry.
- [x] **K3, `DYNA217` and `DYNA218`.** A nested `RetryOnConflictAsync`, and a version read before the retry started.
- [x] **K4, "Migrating from the AWS SDK" in the README**, and the element converters in the docs of `SetArrayConverter`. Merges last.
- Decision: no base type for client-side failures. `TimeoutException` and `HttpRequestException` are thrown as they are, and `api.md` promises the first, so a base type would mean wrapping, which breaks every `catch (TimeoutException)`.
- Decision: no date key type. `DateOnly` is already a key type, and a day bucket is a property of that type.
- Decision: no region from instance metadata. Resolving the client is synchronous, and EKS and Fargate inject `AWS_REGION`.
- Decision: `DYNA115` stays at Info. Its only fix is another delimiter, so a warning would be suppressed globally.
- Decision: `CountsAsMissing`, a predicate over a read-only reader, replaces the planned `IDynamoDbConverter<T?>`, which needed a third pair of handle wrappers and still left `Select` throwing where an entity read gives null. A `TryRead` with a default that reads cannot compile, because a static virtual default has no self type to call `Read` on. A predicate looks at the value twice, only on a property whose converter answers, and cannot leave it unconsumed.
- Decision: `Keys.Of` validates when called, as `Keys.Pk.Of` and `Format` do. The checks are a scan of a span, so they need no string.
- [x] **K5, `[ComputedFrom]`.** A computed property may feed an index template when it names the stored properties it reads. An update of one of those by handle throws, because an update cannot run the getter. Not on `[DynamoDbAttribute]`, which forces a property to be stored. Starts after J4.
- [x] **K6, `StoreKeyInputs()`.** A typed update stores the inputs of the table key with `if_not_exists`, for an upsert. Same implementer as K5, after it.
- [x] **K7, `StubSts`.** A loopback server in `DynaSharp.Testing`, because the requests of a credentials provider are not behind `HttpHandler`, and a `WebIdentityCredentialsProvider` constructor that takes the endpoint. The stub never touches the process environment.

### L1, M1, H3 and N1 - what was left after K, and a faster suite

- [x] **L1, open items.** A forward walk of the `Item` in `FailedCondition`, `DYNA120` for a raw placeholder beside a strict one with the delimiter in it, the README gate `build/ReadmeCheck.cs` and time limits in `ci.yml`.
- [x] **M1, docs.** A final pass over the README, `src/DynaSharp.Testing/README.md` and the traps the services ran into.
- [x] **H3, cleanup.** `WriteState`, `ReadOptions`, `DiscriminatorCheck`, the folders of `Fluent`, `CommandPool`, the fluent tests split into `Fluent/WriteRules` and `Fluent/KeyInputs`.
- [x] **N1, test and CI speed.** One compilation per analyzer test class with tree-scoped analysis, `GeneratorRun.Generate` where a test reads no compiled output, the compile guards batched, identical stress runs merged, `dotnet format whitespace` plus `IDE0055` in the build, and the build job limited to the unit tests. The suite went from 176 s to 54 s, measured locally, and nothing dropped a row: differentials compared the shared paths with the full ones.
- Decision: a result of the generator shared between tests is rebuilt under Stryker (`SharedResult<T>`). Its MTP runner keeps a test host for many mutants, so a kept result would hide later mutants and fail on others.
- Decision: an item of another type reports the type before a key that does not fit: a read finishes its walk first. A value of a shared attribute that the converter refuses can still fail first, under its own name, because finding the discriminator from there would cost every read a copy of the reader.

### Q1 - what `CreateTable()` reports, and an index that is not listed

Branch `m3/q1-create-result`, from item 3 of the second focus group.

- [x] **`CreateTableResult`.** `SendAsync` and the awaiter return it instead of `TableDescription`: `Table`, `Created`, `TimeToLiveEnabled`. Breaking, so before 0.1.0.
- [x] **Fail fast on an index that is not listed.** The wait of `CreateTableCommand` ends at the fifth look in a row without a declared index, 1.5 seconds in, instead of at the timeout. The wait of `Table<T>` keeps polling.
- Decision: a sealed class with an internal constructor, like `TransactGetResult`: administration is no hot path, and a default struct would have a null `Table`.
- Decision: `Created` is false after a retry of a lost create that meets the table, because nothing tells it from a table of another process. `TimeToLiveEnabled` is false when another instance won the update.
- Decision: the grace counts looks that find the table, not time, as the create loop counts creates, and a look that lists every declared index starts it over, because a replica that lags can answer between two that do not.
- Finding: the grace holds for the recipe of the README as long as each instance sends its `UpdateTable` (or loses it to one that is accepted) before the typed wait, and `DescribeTable` lists an accepted index within 1.5 seconds. Not measured on real DynamoDB yet.

### Q2 - warnings on models and key templates, before 0.1.0

- [x] **Q2.** `DYNA031` for an entity stored as a map in an entity, `DYNA032` for a getter with `[ComputedFrom]` that reads a stored property the attribute does not name, `DYNA120` through `[ComputedFrom]` for a getter that hands a raw input on, with `DYNA121` at Info for any other getter, and `DYNA122` for a constant that a key builder always refuses. A new Warning fails every consumer that builds with warnings as errors, which is cheap only before 0.1.0.
- Decision: `DYNA120` and `DYNA121` through a getter stand on the computed property, not on the strict template: they come from `ComputedGetterAnalyzer`, which sees the getter, and the model holds no place of a template, which would cost the incrementality of the generator.
- Decision: `DYNA031` is for a property of an entity, as the item of an entity is what carries the copy of the keys. An entity inside a document is not reported.
- Decision: `DYNA122` checks an empty value and a delimiter, what `KeyWriter` refuses of every string, and leaves control characters, lone surrogates and U+10FFFF to the run time.

### Q5 - what a run-time failure says

- [x] **Q5, messages.** A failed condition of `Put`, `Update` and `Delete` names the condition the write sent, from a byte of kinds in `WriteState.Conditions`: the version check by its attribute, the check that no item or that an item has the key, the caller's own, or all of them, since DynamoDB does not say which. A version that marks a new item, the plain `Put` of a fresh entity, points at the version last read and `IgnoreVersion()`. A cancelled transaction names each failed action as `Action 2 of 2, Put on Users: ConditionalCheckFailed`. A key value that does not fit names the template and its delimiters. No value in any of them.
- Decision: the verb of a transaction action is read back from the body on the failure path, like the key of `FailedCondition`, instead of a byte per action, so a transaction that gets through keeps nothing. The template of a key reaches the message through a `catch` in the generated builder and `KeyFormatException.InTemplate`, because `KeyWriter` sees the delimiters of a call and not the template, and an argument on every write would cost the success path.

### Q3 - warnings on commands and expressions, before 0.1.0

- [x] **Q3, analyzer warnings.** Branch `m3/q3-command-warnings`. `DYNA213` on bare names in the text of an expression, `DYNA220` for a reserved word as a bare name, `DYNA221` and `DYNA222` for a filter on what a key of the query holds, `DYNA223` for overlapping actions of one update, `DYNA224` for a key of another attribute, `DYNA225` for `Including` of an entity with another key. A new warning fails a build with warnings as errors, so these go in before 0.1.0, and a rule added later starts at Info.
- Decision: `DYNA221` reports only where the key condition decides the filter: a placeholder of the partition key, the first one of the sort key, a plain key and a raw key attribute. A filter on a later placeholder of the sort key is the bitemporal recipe and correct for items from `Put`, and a filter on an input of the table key in a query of an index cannot move into that key condition, so neither is reported, although an item an `Update` created lacks the attribute; that is `DYNA209`'s class of case and stays with it.
- Decision: `DYNA220` suggests `Attr(...)` or a handle, which render `#name`, and not a `#name` of one's own, which is `DYNA211`. `DYNA223` counts `SetIfNotNull`, because the update sends `SET` twice when it has a value, and leaves out only one that adds nothing: a constant null, or an empty string on an `[OmitEmpty]` property. `DYNA225` compares key names only; another type of a key attribute and an index declared twice differently throw in `TableSchema` as well and are not reported.

### Q10 - lenient enums, before 0.1.0

Branch `m3/q10-lenient-enums`, item 3 of what the review of the converters in the services found to ship: one service wrote 63 lines of converters for two enums, a helper and one struct per enum because a generic converter is `CA1000`, so that a name a newer version stores during a rollout does not fail the read.

- [x] **Q10.** `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]` in `DynaSharp.Mapping`, `UnknownEnumName.Throw` the default. `EnumRegistry` reads it into `EnumModel.CountsUnknownAsMissing`, `EnumEmitter` gives the generated converter a `CountsAsMissing` that is true for an `S` that `TryParseName` refuses, `ConverterResolver` sets `ConverterModel.CountsAsMissing` for a property of the enum without a converter of its own, and the forwarder in `Attrs` passes the question on. `DYNA033` (warning) for the attribute on a `[Flags]` enum or on one with its own `[DynamoDbConverter]`.
- Decision: the property is lenient, not every read of the enum. An element of a list, a set or a map still throws: dropping it gives a collection shorter than what is stored, which the next `Put` of the entity writes back, and a list has positions. This is the rule `CountsAsMissing` already has (K1), and the converters of the service behave the same, as their `Read` is strict. A collection that should drop unknown elements takes a converter of its own.
- Decision: everything else follows from as if the attribute were not in the item, as for K1: the initial value, null for a nullable property without an initializer, a throw for a `required` member, a key input restored from its key, a plain key read strictly (`EntityKeyBuilder.KeepKeysFromMissing`), `Select` null for a nullable handle and otherwise a throw that `DYNA209` reports, `Return` a throw. An empty string is no name, so it counts as missing. A value that is no string, `{"NULL":true}` on a property that is not nullable included, still throws. Writes and stored bytes do not change.
- Decision: names only. The generated converter never took a number, where `Enum.Parse` of the services did, so `"99"` is unknown, and an alias reads as its member.
- Decision: `DYNA033` comes from an analyzer on the symbol of the enum, because the generator sees an enum only through the types that use it, so it would report once per type or not at all. It reports the value that asks for something, never `Throw`. A `[DynamoDbConverter]` on a property or a `[DynamoDbDefaultConverter]` for the enum is not reported: they are per property and per assembly of the entity, and the enum can be lenient everywhere else. A value of `UnknownEnumName` that is no member throws, as the default does.
- Decision: the name of the attribute and of the enum stay as proposed. `[Version(Missing = VersionMissing.Allow)]` is the only other enum on an attribute, and `UnknownName = UnknownEnumName.CountsAsMissing` reads as what it does, with `CountsAsMissing` named after the member of `IDynamoDbConverter<T>` that it gives.
- Finding: a lenient property parses its name twice, in `CountsAsMissing` and in `Read`, a switch on the length and a span compare, without allocation for text without escapes. No AOT smoke change: `TryReadAttribute<T, TConverter>` over a struct converter is already there.

### Q11 - two gaps of the Streams page, before 0.1.0

Branch `m3/q11-stream-gaps`, from writing `docs/streams.md`.

- [x] **`Item.Is<T>()`.** True when the discriminator attribute of `T`, `_type` or its `AttributeName`, holds `T`'s discriminator. Constrained to `IDynamoDbDiscriminated<T>`, so a type that opted out does not compile. The page asks it instead of comparing `GetString("_type")` with a literal.
- [x] **Nullability of the stream responses.** Checked against botocore's `dynamodbstreams` 2012-08-10, the Smithy model in `aws/api-models-aws` and the API reference: nothing changes, see the decision.
- [x] **Page.** `DynaSharp.Streams` is a shared using of `build/ReadmeCheck.cs`, so the reader of the page needs no using lines of its own.
- Decision: `Is<T>()` reuses the rule of a mixed read: `MixedSchema.Of<T>` is a schema of one type, and its `FindType(Item)` answers -1 for an item of none of its types, also one without the attribute, with NULL or with a value that is no string, where the wire overload throws. A hand-written type that claims a discriminator without one throws `ArgumentException`, as `Of<...>()` does. It compares one `string` ordinally and allocates nothing.
- Decision: no `TryTo<T>(out T)`. On an item of the right type that does not map it would have to throw, which is not what a `Try` method does, or return false, which hides the failure. `Is<T>()` then `To<T>()` says both. `Is<T>(out T)` in the shape of `Mixed.Is(out T)` would read the same way, but `Mixed.Is` never throws, so it is left out as well.
- Decision: every member of the public stream responses stays nullable. No output shape of the model has a `required` list, but `KeySchemaElement` (whose members are not nullable already), the Smithy model has `@required` only on inputs and `KeySchemaElement`, and the reference says `Required: No` for every member of `Record`, `StreamRecord`, `Shard`, `StreamDescription`, `SequenceNumberRange`, `Identity` and `Stream`. A member of a response that is missing reads as null, as everywhere in the library, so there is no new failure. The only sentence that reads like a promise is "Each shard in the stream has a SequenceNumberRange" of `DescribeStream`, against `Required: No` on `Shard`, and making that one member non-nullable removes no `!` from the page.
- Finding: what the emulators send. DynamoDB Local 3.3.2 sends every member of the model in `ListStreams`, `DescribeStream`, `GetShardIterator` and `GetRecords`, leaves `NextShardIterator` out of the last page of a closed shard and `EndingSequenceNumber` off an open shard, and sends no `userIdentity` for a change of a client. Dynoxide 1.5.0 leaves `KeySchema` out of `StreamDescription`, sends `SequenceNumberRange` as `{}` before the first record and an `EndingSequenceNumber` on an open shard (the gaps `StreamDescriptionKeySchema`, `StartingSequenceNumberOfAnEmptyShard` and `OpenShardWithoutEndingSequenceNumber`), and drops the stream when it is switched off. Neither omits `StreamDescription`, `ShardId` or `dynamodb`, which the page marks with `!`, nor `SequenceNumber`, `eventID`, `eventName`, `eventVersion`, `eventSource`, `awsRegion`, `Keys`, `SizeBytes`, `ApproximateCreationDateTime`, `StreamViewType` of a record or `ShardIterator`.

### Q9 - opt-in converters for tables other code wrote, before 0.1.0

Branch `m3/q9-converters`. Four converters that services had written for themselves, and one word for opting out of the discriminator. All additive: no default changes, since defaults are frozen at 0.1.0.

- [x] **`BoolAsNumberConverter`**, `bool` as `{"N":"1"}` and `{"N":"0"}`, the AWS SDK's conversion V1.
- [x] **`SdkDateTimeConverter`**, `DateTime` as `{"S":"2026-10-31T12:00:00.123Z"}`, the SDK's text.
- [x] **`OffsetTimestampConverter`**, `DateTimeOffset` as `{"S":"2026-11-20T11:00:00.0000000+01:00"}`, the offset kept.
- [x] **`EmptyStringAsMissingConverter<T, TConverter>`**, what `TConverter` stores, with `{"S":""}` counted as missing.
- [x] **Docs.** `migrating.md` names the shipped types instead of printing two of them, shows the other two, and says which converters a hand-written one may call; `entities.md` lists the opt-in converters; `adopting.md` names the assembly opt-out of the discriminator.
- [x] **`DiscriminatorOptOut`.** `DynamoDbDefaultsAttribute.NoDiscriminator` is renamed, so both spellings say `OptOut`: `[Discriminator(OptOut = true)]` on a type and `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]` on an assembly, and the XML docs of each name the other. No obsolete alias, before 0.1.0. The descriptor of `DYNA020` keeps its field name `NoDiscriminator`, which names the condition it reports, and its message stays as it is.
- Decision: the SDK semantics were run, not read: a file-based program against `AWSSDK.DynamoDBv2` 4.0.109 (the version of `Directory.Packages.props`) on a machine at UTC+1/+2. Conversion V1 and V2 write `ToUniversalTime()` in `yyyy-MM-ddTHH:mm:ss.fffZ`, so `Unspecified` is local time and ticks below the millisecond are cut off; `DynamoDBContext` reads `Utc` under its default `RetrieveDateTimeInUtc = true` and `Local` with `false`. The converter does the first and reads `Utc`. The SDK also reads text without an offset as UTC, and a date alone; the converter rejects both, as `DateTimeConverter` does, because the SDK never writes them.
- Decision: `BoolAsNumberConverter` reads zero as false and any other number as true. The SDK compares the text with `"0"`, which is the same over what DynamoDB returns, because DynamoDB normalizes numbers; reading as a `double` also sees `-0` and `0.0` of other writers as zero. A `BOOL` throws: a table holding both needs a converter of its own.
- Decision: `OffsetTimestampConverter` writes `"O"` of the `DateTimeOffset`, `+00:00` for UTC and never `Z`, and reads the forms of `DateTimeOffsetConverter`, `Z` as offset zero, so a property can move to it and read what it stored. The name says what is stored, as `EpochSecondsConverter` does.
- Decision: `EmptyStringAsMissingConverter` passes `Write`, `Read`, `IsOmitted` and `CountsAsMissing` on, and not `TryReadMissing`. Any implementation of `TryReadMissing` makes the generator take the converter for one that answers `Select`, and `DYNA209` would go quiet over a non-nullable property that throws on `""`. `IsOmitted` is passed on so that a wrapped converter keeps what it leaves out; the write of such a property holds one more call, which answers `false` for a built-in.
- Decision: the generator knows two opt-in converters by name. `BoolAsNumberConverter` is a number key, because `WireTypeOf` takes any converter of `DynaSharp.Serialization` for a built-in that writes what its type writes, and a `bool` would be a string key. `SdkDateTimeConverter` goes into `OrderedText`, so a plain sort key has the ranges; `OffsetTimestampConverter` has `Eq` alone. `ExpressionAnalyzer.NoTextConverters` and the `BeginsWith` overloads that do not compile are left for `BoolAsNumberConverter`, as for a number converter of one's own.
- Decision: the opt-in converters are tested through `[DynamoDbConverter]` on `Remittance` in the unit tests and through `[assembly: DynamoDbDefaultConverter]` on `Payment` in `DynaSharp.DefaultsFixture`, which now gives `bool`, `DateTime` and `Guid` defaults; `Guest.IsVip` is stored as a number there. The AOT smoke has the wrapper, the one new generic shape, and the integration tests have a round trip of each, which CI runs.
- Finding: the wrappers a hand-written converter may call. The converters of single values and `SetArrayConverter` are API. The list, map, set and document converters and the wrappers of null are infrastructure by `requirements.md`: public for generated code, compatible within a package version, and `ReadOnlyListConverter` says a converter of one's own may call it and then follows the package. One service calls it. Open for the owner: bless the list converters for hand-written converters, or keep them infrastructure.
- Known friction, not changed: the built-in `DateTimeConverter` throws on `Unspecified` at 0.1.0, where the SDK takes it for local time. `SdkDateTimeConverter` is the way for code that relies on that.
- [x] **A refused value names its property.** A migrated service's first test run failed 83 times of 98 on an unset `DateTime` in test data, with a message that named no entity and no property. The rule stays a throw. The generated write of a property through `DateTimeConverter`, or a `double`, `float` or `Half` number converter or set element, alone or in a list, a dictionary or a set, sits in a `try` whose `catch` filters on `ConverterChecks.IsRefusedValue` and throws `ConverterChecks.RefusedValue`: the type, the property, the attribute, the kind it got, and the fix (`DateTimeKind.Utc`, or `SdkDateTimeConverter`), no value. The converters throw internal subclasses, `UnspecifiedDateTimeException` of `ArgumentException` and `NonFiniteNumberException` of `ArgumentOutOfRangeException`, which is what the filter knows them by; the rethrown exception has the same public type and holds the one of the converter. `docs/entities.md` states the rule where the timestamp formats are.
- Decision: a catch per such property, chosen by the name of its write converter in `WriteEmitter`, and not a store of the current attribute in a local, as the read does: the try costs the success path nothing, and a property of any other converter holds none. A nested document names its own type and property, because its own generated write holds the catch. Not measured with the benchmarks: none of their entities has a `DateTime`, `double`, `float` or `Half`.
- Decision: not for a string with a lone surrogate, which `DynamoDbWriter` refuses as well. Every string property would carry a `try`, for a value that broken input makes and test data does not, and its message says what is wrong. Not for a write by handle, which names the attribute in the call, and not for a key template, where `KeyFormatException` names the template already. A null in a property that is not nullable passes the filter as it is.


### Q12 - a retry on transaction conflicts only, and `Contains` by handle, before 0.1.0

Branch `m3/q12-retry-contains`, from what the design-patterns pages wrote by hand.

- [x] **Retry on transaction conflicts only.** `ConflictKind` (`ConditionalCheckFailed`, `TransactionConflict`, `Any`) and four overloads of `RetryOnConflictAsync` on the client and on `Table<T>` that take it after the delegate, with and without `maxAttempts`. The overloads without it are `Any`, so nothing changes for them. The uniqueness recipe and its page use `ConflictKind.TransactionConflict` instead of a `while (true)` loop, and the page says when to use which.
- [x] **`Contains` by handle.** `AttrExtensions.Contains` for every handle of a set, a list and a string that the generator emits, plain, nullable, with `[OmitEmpty]` and both, and `Attr.Contains(AttributeValue)` and `Contains<TNumber>`. The struct is `Contains<T, TConverter>`, and a set writes its operand through `SetElementValueConverter<T, TElement>`. The idempotent counter and its page use `Seen.Contains(requestId).Negate()`. Both recipes pass on dynoxide and DynamoDB Local.
- Decision: a `[Flags]` enum in an overload, not an options type or a method of another name. It says which failures repeat, the default stays as it was, `Any` is the union by construction, and `maxAttempts` keeps its meaning. A value that is none of the three throws at the call, as `maxAttempts` below 1 does. A conflict that is not named surfaces at once, like any other failure, so there is no wait and no `conflict.exhausted` for it. A cancelled transaction runs again when any of its reasons is named, the rule the default already had.
- Decision: `DYNA218` does not look at a retry whose `retryOn` is a constant without `ConditionalCheckFailed`: a stale version fails the first run and surfaces, which is what the version is for, and nothing runs again with it. A `retryOn` that is no constant counts as one that repeats a failed condition. `DYNA217` stays for every form, because an inner retry multiplies the runs whatever the outer one repeats. The analyzer cannot reference the library, so it holds `ConditionalCheckFailed` as the number 1, and a test pins it to the enum.
- Decision: no `NotContains`. DynamoDB has no such function, and the API negates with `Negate()` everywhere except `Exists`/`NotExists`, which are two functions of DynamoDB. `NOT (contains(...))` holds on an attribute that does not exist, which the counter relies on and the XML docs say.
- Decision: one overload per shape of handle, as `BeginsWith` has, because the element type of a set or a list is in the converter of the handle and C# cannot infer it from a constraint. A shape outside them, a number, a boolean, a binary value or a custom converter of the whole collection, has no overload (`CS1929`); `CompileGuardTests` pins that, and `ContainsTests` compiles a call on each shape the generator emits. A string takes a substring only on `StringConverter` and its wrappers, not on an enum, a `Guid` or a date stored as text.
- Decision: the set operand needs a converter of the element as a value, which an element converter is not, so `SetElementValueConverter<T, TElement>` writes the descriptor of the scalar type and then what `WriteElement` writes inside a set. It is public because the return type of the overloads names it, like `NullableHandleConverter`, and it reads as well, for the contract of `IDynamoDbConverter<T>`.
- Decision: no `Size` by handle. It is a third operand form for `Compare` and `Between`, and a typed one would need the same matrix of handles plus refusals for numbers and booleans, where DynamoDB rejects it. Not cheap, so `size` stays in the grammar as text.
### Q15 - diagnostics and stubs from the migrated services, before 0.1.0

Branch `m3/q15-diagnostics-stubs`, three fixes the owner approved from what the migrated services ran into.

- [x] **`DYNA209` passes over a plain `[Version]`.** `Select(Attrs.Version)` is no longer reported; the cases of a stored input of the table key, `[OmitEmpty]` and `CountsAsMissing` stay. On an item without the version attribute `Select` throws `DynamoDbMappingException` with a message of its own, `MissingVersion`, which says that the item was written without one and names `[Version(Missing = VersionMissing.Allow)]`, and holds no value. An index has the `EntityWrites` of its table, so its `Select` says the same.
- Decision: this reverses the part of I1 that had `DYNA209` cover `Select` of a non-nullable `[Version]`. Every write of the library holds the version: `Put` writes it, `Update` with `IfVersion` sets it, a transaction step does both, and a batch takes `IgnoreVersion()`, which stores the entity as it is. Only `Update(key).IgnoreVersion()` that creates the item leaves it without one, besides code of another kind. On such an item the throw of `Select`, which now names the cause and its answer, is better than the 0 a read of the entity gives without a word, and a warning on every `Select` of a version cost the services a suppression each.
- [x] **`DYNA020` keeps the model.** `DiagnosticBag.ReportKeepingTheModel` reports the error without marking the type broken, so the type is generated as `[Discriminator(OptOut = true)]` generates it, `IDynamoDbEntity<T>`, `Keys` and `Attrs` included, and a service that forgot the attribute sees one error instead of a `CS0311` and a `CS0426` on every use. The suggested value is `NameCasing.ToSnakeCase(name).ToUpperInvariant()`: `TransportRecord` gives `TRANSPORT_RECORD`. The message names the assembly-wide opt-out beside the one of the type, `[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]`, by the name the property has after its rename on `m1/lead`.
- Decision: no other error gets this. `DYNA020` is the only one whose model is whole, because `ReadDiscriminator` already answers it with the discriminator of an opt-out; every other error means that a part of the model could not be built. The warnings that `EntityKeyBuilder` reports after its check for errors, such as `DYNA117` and `DYNA120`, now show beside `DYNA020`, as they do for an opt-out.
- Decision: no suffix is cut off the suggestion. `Record`, `Entity` or `Item` at the end can be part of what the type is, and a value the build suggests is copied as it stands, where a guess that drops a word is a rename someone has to notice.
- [x] **Stub answers for `UpdateTable` and `ListTables`.** `StubResponse.TableUpdated(name, status = Updating, params globalIndexes)` writes the description of `Table(...)` under `TableDescription`, and `StubResponse.TablesListed(params names)` and `TablesListed(names, lastEvaluatedTableName)` a page of names. Both are answers to their one operation, so `StubHandler` throws when another request takes them, and its failure for a request to `UpdateTable` or `ListTables` that took another answer names the factory. The library's own tests that stubbed both with `Json` use them.
- Decision: the page with more to come is `TablesListed(ReadOnlySpan<string> names, string lastEvaluatedTableName)`, with the names first and not as `params`. A `TablesListed(string? lastEvaluatedTableName, params ReadOnlySpan<string> names)` beside `TablesListed(params ReadOnlySpan<string> names)` would take `TablesListed("Orders", "Users")` as a page after `Orders`, because C# prefers the overload with more declared parameters when both apply only in their expanded form.
- Decision: an index keeps the status it was given, `ACTIVE` by default from `GlobalIndex`, as in `TableCreated`. Defaulting the indexes of `TableUpdated` to `CREATING` would rewrite a status the test stated, and most of them describe what the table had before the update.

### Q13 - inputs of an index key, and a time-ordered id, before 0.1.0

Branch `m3/q13-index-inputs-ids`, two gaps the design-patterns pages ran into.

- [x] **Time-ordered ids.** `Guid.FirstVersion7(at)` and `Guid.LastVersion7(at)`, static members that `GuidExtensions` adds to `Guid` through an extension block, give the lowest and the highest version-7 value of the millisecond of `at`. With them a range of time is a typed sort key condition of a key with a `Guid` placeholder, `Keys.Sk.Between(Guid.FirstVersion7(from), Guid.LastVersion7(to))`. "Newest first with version-7 GUID sort keys" replaces the hand-rolled ULID of the patterns page, `Version7SortKeyTests` is its recipe, and the ULID recipe stays for a service that has ULIDs, with the same bounds as text.
- Decision: no id type. `Guid.CreateVersion7` is in the BCL, and DynaSharp stores a `Guid` as lowercase `D` text in keys and attributes, whose order is that of the time: the 48 bits of milliseconds lead, big-endian, and `0-9` sort below `a-f`. What was missing is the bounds of a millisecond, and those are two values, not a type.
- Decision: the bounds are `Guid` and not key text, because the conditions of a key with a `Guid` placeholder take a `Guid`, and a plain `Guid` sort key does as well. They are static members of `Guid`, in namespace `DynaSharp`, so they stand beside `Guid.CreateVersion7` where a caller looks for them, and `using DynaSharp;` is already there. The lowest has every random bit 0 and the highest every one 1, version and variant as `Guid.CreateVersion7` sets them, so both are values it could make, and every value of the millisecond lies between them. They take what `Guid.CreateVersion7(DateTimeOffset)` takes and throw what it throws, an `ArgumentOutOfRangeException` before the Unix epoch, and they allocate nothing.
- Decision: a millisecond and not an instant is what a bound stands for, because that is all a version-7 value holds. So `Between(First(from), Last(to))` takes both milliseconds whole, and `Lt(First(to))` is the half-open end.
- [x] **Index keys from several inputs.** No new member. The messages of `KeyInputChanges` name the `Set` to add, `.Set(WorkItem.Attrs.CreatedAt, value) with the value the item holds`, and why: DynamoDB cannot build a key from what an item holds. `DYNA226` reports in the build what the update throws for when it is sent: a key of an index that only some of its inputs reached, and one key of a sparse index without what the other one is made of. The pattern pages keep what does not change in the key of the table: the status-keyed index sorts by a version-7 id instead of a time of its own, and the owner of a note is in its table key, so closing an item and restoring a note each set one property and need no read. Each recipe keeps a test of the design with the input outside the key, which throws and names the `Set`.
- Decision: no way to name the value of an unchanged input for the key without writing its attribute. It saves no read, which is the friction: the caller needs the value either way, and a `Set` of the same value adds a few bytes to a request that DynamoDB bills by the size of the item. And it lets the key disagree with the attribute it is made of, unless it adds a condition on that attribute, which `.If(handle.Eq(value))` already says for a caller who wants a value that changed in between to fail the write.
- Decision: no read inside an update, and no key written from some of its inputs. A hidden read doubles the cost and the latency of an update and races with other writers, and a half key is the stale index the check exists for.
- Decision: the docs show the design that needs no read, and what it costs: an input in the key of the table cannot change, so a note that changes hands is deleted and put again, and an id that holds the time replaces a time of its own.
- Decision: `DYNA226` is a warning before 0.1.0, as Q3 decided for new warnings, and the next free command rule. It reports only what it can prove throws: a chain from the verb to a terminal, an `await` or the next step, with no method of the consumer between them, and an index none of whose inputs the update removes, changes by `Add`, `SetIfNotExists` or `Delete`, or sets to a value that may be null, since a null is a `Remove` that takes the item out of a sparse index. A write that `DYNA205` or `DYNA219` report is theirs. One report per index, for what the terminal throws first. Over the repository it found every test of the run-time check, which now suppress it, and nothing else.

### Q14 - a bare timestamp placeholder is `ticks`, before 0.1.0

Branch `m3/q14-ticks-default`. Approved by the owner; it reverses the decision of 2026-10-31 that the default stays `ms`, before 0.1.0 freezes the stored key format.

- [x] **Default.** `TimestampInput` binds a `DateTime` or `DateTimeOffset` placeholder without a format to `ticks`, 28 bytes, the round-trip form `O` that the attribute holds as well: `ORDER#{PlacedAt}#{OrderId}` writes `ORDER#2026-10-31T12:34:56.7890123Z#...` where it wrote `ORDER#2026-10-31T12:34:56.789Z#...`. `DateOnly` and the other types take no timestamp format and do not change. `s`, `ms`, `us` and the bucket formats stay as they are, explicit.
- [x] **What follows from it.** A bare timestamp is lossless, so it is a `FallbackKey` and a `RestoredFromKey` like a string: a read restores a required input from the key of an item an `Update` created, and `[KeyOnly]` on it compiles. `DYNA116` fires only on an explicit `s`, `ms` or `us` and says to drop the format. `DYNA208` no longer reports a `Project` that leaves out such an input, which a key the read adds restores. A filter on it in a query of its key is `DYNA221` (warning) instead of `DYNA222` (info). `DYNA209` now reports `Select` of it on a key of the table, which is right: a read of the entity restores it and `Select` does not, where before both threw. A key built from a value that was read from the attribute finds its item. `BeginsWith`, `Prefix`, `UpperBound`, `Between`, `Range` and the other sort key conditions need no change: they come from the encoding, and the coarser formats are byte prefixes of `ticks` as of `ms`. `Keys.*.Parse` of a bare placeholder accepts seven digits and rejects three.
- [x] **Vectors.** `generate_key_vectors.py` knows `ticks` in a composite template. `composite/spec-order-*` and `composite-read/spec-order-*` are in `ticks` now, with a read of a millisecond key that it rejects, and `composite/sdk-order-sort-key`, `composite-read/sdk-order` and `composite-read/sdk-order-seven-digits` keep `{CreatedAt:ms}`, which `VectorKeys` holds as `V18`.
- [x] **Docs.** `docs/entities.md` shows the attribute and every key form of one value in one table, `docs/migrating.md` says that `{At:ms}` reads and writes keys the AWS SDK wrote, and the key attributes, `DYNA116`, `DYNA209`, `api.md` and `requirements.md` say `ticks`.
- Finding: `DYNA224` compared the text of two templates, so a condition of the table's `ORDER#{PlacedAt}#{OrderId}` on an index of `ORDER#{PlacedAt:ticks}#{OrderId}` was reported, although both write the same bytes. It compares the segments now. Two keys on one attribute (`EntityKeyBuilder.Claim`) still have to be spelled alike, which fails at compile time and is left as it is.
- Decision: `ticks`, because it is the only lossless format and the attribute has the same seven digits. The cost is 4 bytes per key and per index key. Sort order and `begins_with` on a date are unchanged, and `{At:ms}` stays for keys that the AWS SDK or another tool wrote.

### Q17 - what the first run on real DynamoDB found

Branch `m3/q17-real-aws`. The first run of the integration tests on real DynamoDB, eu-central-1 through IRSA from GitHub OIDC: 874 passed, 10 failed, 10 skipped. Credentials worked end to end. No failure was a bug of the library.

- [x] **The cleanup.** `UpdateTable_DeletionProtectionOn_PreventsDeleteTableUntilItIsOff` had its second `UpdateTable`, 360 ms after the first, refused with `ThrottlingException` on all four attempts of the client; the cleanup then could not delete the protected table, the hook at the end of the run failed on it, and stopped there, before the tables after it in the list. `Tables.DeleteAsync` turns protection off when that is what refuses it and bears with a throttle, `Tables.UpdateAsync` repeats a throttled call seconds apart, the end of a run tries every table and throws what failed together, and a sandbox tracks a table before the create, so a create whose wait fails is cleaned up with its test. The protected table of that run is left in the account until a run after six hours takes it for an orphan, which it can now delete.
- [x] **Reads of an index right after a write.** `Query_IndexOfAnEntityWithoutADiscriminator_KeepsItsFrozenNamesAndFindsTheItem` found nothing, `TransactWrite_StatusTransitionOnRawKeyStrings_MovesTheItemBetweenIndexPartitions` still found the item under its old key, and `Query_MyOpenItems_ReadsOldestFirstAndNoOtherStatus` missed the first of three puts, each 1.6 to 1.9 seconds after the write, on a table created 17 seconds before. A global index is updated asynchronously, with no bound DynamoDB documents. `SettlingHandler` holds a read that names an index for 5 seconds after the last write, a read of a table stays at 1.5.
- [x] **The capacity of a scan.** `Scan_Total_ReportsCapacityForTheItemsRead` expected 0.5 units for three small items and got 2, where a query of the same items reported 0.5. A scan reads every partition and rounds each up to 4 KB, and a new on-demand table has several: four of 0.5. The test is now `Scan_Total_ReportsHalfUnitsAndTwiceThatForAConsistentScan`, which holds for any number of partitions. Inferred from the numbers: DynamoDB documents the rounding per 4 KB, not per partition.
- [x] **Batches over 16 MB.** `BatchGet_ResponseOver16MB_ReadsWhatDynamoDBLeavesInTheNextRound` read all 45 items in 5 requests where it expected 2, and its write of the 45 items of 390 KB took 5 requests where 2 do. `BatchGetItem_ResponseOver16MB_ReturnsUnprocessedKeysThatAFollowUpFetches` found 42 items and keys, because its raw writes ignored `UnprocessedItems` and 3 items were never written. DynamoDB leaves writes and keys unprocessed when a partition runs out of throughput, not only at 16 MB, as `BatchGetItem` and `BatchWriteItem` document. The fluent test asks for two rounds or more, the raw one sends again what is left on both sides.
- [x] **An index that `UpdateTable` adds.** On tables of none to three items DynamoDB took 8.5 minutes (`SendAsync_IfNotExistsAfterUpdateTableAddedTheIndex_WaitsForItAndCreatesNothing`, which gave its wait 5) and about 9 (`UpdateTable_CreateGlobalSecondaryIndex_IndexFillsFromTheTableAndCanBeQueried`, which passed) on demand, more than 6 for `WaitUntilActiveAsync_IndexAddedByUpdateTable_ReturnsWhenItCanBeQueried`, which gave it 1, and more than 15 on a provisioned table (`UpdateTable_CreateGlobalSecondaryIndexOnProvisionedTable_ReportsTheIndexCapacity`, 10 minutes of wait). DeleteTable is refused with `ResourceInUseException` while an index is created, so each cleanup then spent its five minutes and failed the test with `TaskCanceledException`, which hid the timeout of the test. `Tables.IndexBuildTime`, 30 minutes, bounds the waits for an index and the cleanup. These tests run alone, so a run takes about an hour more.
- [x] **Docs of the wait.** The XML docs of `Table<T>.WaitUntilActiveAsync`, `CreateTableCommand.WaitUntilActive`, `TableNotActiveException`, `UpdateTableAsync`, `GlobalSecondaryIndexUpdate.Create` and `GlobalSecondaryIndexDescription`, `api.md` and `docs/tables.md` said that only the backfill of a table with much data outlasts the default of five minutes. They say now that DynamoDB allocates an index before it backfills it, which takes minutes on any table, and give half an hour for a small table. The default stays five minutes, which fits `CreateTable`.
- [x] **A failed cleanup no longer hides the failure of its test.** Three of the ten causes were hidden, because an exception from `DisposeAsync` under `await using` replaces the one of the body, and a fixture cannot see whether the body threw. `Sandbox` hands the tables it could not delete to `CleanupFailures`, and its `[AfterEvery(Test)]` hook decides with the result of the test: one that failed keeps its exception and gets the cleanup failures in its error output, one that passed fails with them. `ShopSandbox`, `CommandSandbox` and `ReadSandbox` dispose through `Sandbox`. `CleanupFailuresTests` covers the decision without a backend; that TUnit sets the result before the hook, and fails a passed test whose hook throws, was checked with a test of each case that was then removed.
- Finding: the grace of 1.5 seconds for an index that is not listed (Q1) held. No `TableNotActiveException` of the run said "not listed": after `UpdateTable` the wait listed the index from its first looks and failed on `Creating`, and `SendAsync_IfNotExistsAndWaitUntilActiveOnATableWithoutADeclaredIndex_FailsWithinSecondsNamingIt` failed fast as it should, in about 2 seconds.

### Q16 - `IsTransient`, and `DYNA033` on a property, before 0.1.0

Branch `m3/q16-transient-enum`. Approved by the owner.

- [x] **`DynamoDbException.IsTransient`.** Public, get-only, non-virtual, on the base type, named after `System.Data.Common.DbException.IsTransient`: true when the same call can succeed later without any change. It is `FailureClassifier.IsTransient`, which answers from `ClassifyResponse`, the rule of the retry loop, for every code, and adds `TransactionConflictException`, a `TransactionCanceledException` whose `Failures` are all `TransactionConflict` or `Throttled`, and the code `BatchIncomplete` of both batch exceptions. `FailureClassifierTests` pins every response of its oracle that the policy repeats to true and every other one, the conflicts of a transaction aside, to false; `DynamoDbExceptionTests` has a case per type and per code.
- Decision: non-virtual and computed from the code, the status, the reasons and the cause, so a `DynamoDbException` made by hand or by a stub answers as one from the wire, and there is one place to change. A subclass of a user cannot override it, which keeps it the same rule as the retry loop.
- Decision: a conflict of a transaction is true although the pipeline does not retry it: nothing about the request has to change, the other transaction ends. A failed condition is false, also inside a cancelled transaction and beside a conflict, because the same call cannot pass unless the item changes. A cancelled transaction without reasons, or with only `None`, says nothing and is false, as is an `Unknown` reason.
- Decision: a batch that gave up is true without a cause, which only rounds without progress leave, and otherwise follows its `InnerException`: through `IsTransient` for a `DynamoDbException`, and through `Classify` for a network failure or a timeout.
- Decision: `ResourceNotFoundException` and `ResourceInUseException` are false although a table that is being created or updated passes later: the same codes mean a table that does not exist and one that exists already, and the client cannot tell. `ExpiredTokenException` is false too: the client resends it at once when the provider can refresh, and what surfaces after that needs a change of the credentials. A code this library does not know follows its status, as the retry loop does: true for 500, 502, 503 and 504, false otherwise.
- Decision: a `TimeoutException` of an attempt, an `HttpRequestException`, an `OperationCanceledException` and a `CredentialsUnavailableException`, which is also what a rejection by STS (`InvalidClientTokenId` and the rest) surfaces as, keep their types and get no `IsTransient`. Wrapping them would break `catch (TimeoutException)` and the documented contract; the XML docs and `docs/errors.md` say which of them are transient.
- [x] **`DYNA033` on a property.** The same ID, descriptor and message format, `Enum '{0}' asks for an unknown name to count as missing, but {1}`, with a reason of its own per case, reported by the generator (`ModelBuilder.ReportUnknownNamesThatThrow`) on the identifier of the property, for a property whose generated enum converter counts an unknown name as missing (`ConverterResolver.CountsUnknownNameAsMissing`, the rule `ConverterModel.CountsAsMissing` uses) where that throws anyway: a plain key of the table or of an index, which `KeepKeysFromMissing` reads strictly, and a property whose `MissingOf` is `Throw`, a `required` one that is not nullable or one bound to a constructor parameter without a default. The existing cases on the enum stay in `EnumAttributeAnalyzer`.
- Decision: the generator, not the analyzer, because only the model knows what a missing attribute does, and the analyzer would repeat `MissingOf` and the key rules. Q10 put the enum case in an analyzer because it is about the enum once; this case is about each property, which is what the generator sees.
- Decision: only what is certain. A required input of a key template that holds it without loss is not reported: the read falls back on the key, which an item written by another tool may hold with a name the enum knows. A `[KeyOnly]` property reads no attribute and an element of a collection throws by the rule of Q10, so neither is reported. A nullable `required` property keeps null, and one that is not required keeps its initial value, so both are silent, and so are `Throw` and no attribute.
- Decision: the test entities `Squall`, `Vane` and `Sail`, which test the throw, suppress the warning, and `EmitterShapes.Warnings` lists the two that the `LenientEnums` shape reports, so `Run_Shape_CompilesWithoutWarnings` pins them instead of seeing none. No new mistake in `PackageSmoke`: its check is per rule, and `Access` already shows `DYNA033` in a consumer's build.

## M4 and later

- [ ] M4: in-memory fake in `DynaSharp.Testing` for functional tests (scope: see requirements; not DynamoDB's
      fine print). Plug it into the backend-parametrized scenario tests. Host it behind a small HTTP listener
      (test tool, `HttpListener`) so the Parity Suite tiers 1 and 2 can run against it; track its score.
- [ ] M5: SSO, `credential_process`, assume role
- [ ] M6: first release 0.1.0 (enable `PUBLISH_TO_NUGET`, configure trusted publishing)

### R4 - one-shard stream reader (before 0.1.0, approved by the owner)

- [x] `DynamoDbStreamsClient.ReadShardAsync(ReadShardRequest)`: one shard as `IAsyncEnumerable<ChangeRecord>`, from `TrimHorizon`, `Latest`, `AtSequenceNumber` or `AfterSequenceNumber`;
      renews an expired iterator after the last record returned; waits `EmptyPageDelay` on `TimeProvider` after an empty page of an open shard; ends on a closed shard read to its end; honours cancellation.
      Branch `m4/r4-stream-reader`. Docs page and `OrderFeed` use it.
- Decisions: records, not pages, because the checkpoint is per record (`StreamRecord.SequenceNumber`). `TrimmedDataAccessException` surfaces: the caller lost data and only it knows how to rebuild. An iterator
  expired right after `GetShardIterator` is not renewed again, so a broken clock or service cannot loop. Before the first record the renewal reuses the request's position, which for `Latest` skips what was written in the
  gap; the gap only exists when no record arrived, and `EmptyPageDelay` is capped below 15 minutes so a wait alone cannot expire an iterator. No new telemetry: each call is its own span and measurement, and a renewal shows as a
  failed `GetRecords` followed by `GetShardIterator`. No lineage helper: `OrderFeed` needs the roots and the children of an ended shard, which a sorted snapshot does not give.
- Notes: an async iterator with `while (cond)` around a `yield` left a compiler branch on the loop line that no test reached under MS CodeCoverage; `while (true)` with an explicit `yield break` and a `for` over the
  `IReadOnlyList` (no interface enumerator to dispose) are fully covered. `build/ReadmeCheck.cs` has no `checkpoint` field, so the resume snippet of the streams page uses `after`.

### R2 - signed page tokens, opt-in, before 0.1.0

Branch `m4/r2-signed-tokens`. Approved by the owner. The threat, from the focus group: inside the key condition a forged token only picks a position among items the caller may read, DynamoDB rejects a start key outside it, and a `Filter` applies from any position. So signing is for services whose key condition does not separate their callers, and for those that must not let a client choose its position at all.

- [x] **Signer.** `ContinuationTokenSigner(params ReadOnlySpan<byte[]> keys)`, keys of at least `MinKeyLength` (32) bytes, copied. HMAC-SHA256 of the BCL under the first key; a check tries every key with `CryptographicOperations.FixedTimeEquals`, which is the rotation. The signer holds nothing else and prints its type name.
- [x] **Where the key lives.** `DynamoDbClientOptions.TokenSigner`, not a per-call argument and not a table option. A key per call would need `Parse(text, key)` and `ToString(key)`, and the token would have to carry what it binds, because a page is over when its text is written; the options know the read on both sides. Per client, every instance of a service configured with the same keys checks what another one handed out, and a client without a signer keeps plain tokens byte for byte. A table option would let two handles of one table disagree.
- [x] **Format.** Version 2, `Base64Url([2][key JSON][32-byte HMAC])`. The signed message is `[2][key JSON][key length, 4 bytes BE]DynaSharp.ContinuationToken\0{TableName, IndexName as in the body}\0<resolved key condition>[\0<partition keys as a JSON array>]`. JSON holds no zero byte and the key length starts with one, so no part can pass for another. `Parse` checks the shape of both versions (version 2 needs more than the 32 bytes of the signature, and a key in front of them), because it has no keys; `PageAsync` checks the signature.
- [x] **Bound.** Table, index, the key condition with names and values in place of their placeholders (`ExpressionWriter.WriteResolvedKeyCondition`), and the partition keys of a fan-out. So another partition, sort key condition, table, index, scan or list of partitions rejects the token. **Not bound:** filter, projection, `Limit`, direction, consistency, and time: a token goes on with another filter or page size of the same query, as a plain one does, and does not expire. A fan-out still checks its direction and partition count on its own.
- [x] **Rejections.** All `FormatException` before a request goes out, with no token content in the message: a signature that does not hold, a plain token that came as text on a client that signs, a signed token on a client that does not. Kept as `FormatException` and not a new exception, so a web handler still has one thing to catch for bad input; `docs/errors.md` says it.
- [x] **Accepted unsigned.** A plain token this process made, from `FromKey` or kept as a value: it is no client input, and without that `FromKey`, the bridge from the request API, would not work on a client that signs. `ContinuationToken` holds a form byte for that; equality stays the text alone (`Equals` and `GetHashCode` written out).
- [x] **Cost.** The unsigned path reads one more field and allocates nothing more. The signed path writes the binding into a pooled buffer and signs on the stack.
- [x] **Docs and tests.** XML docs, `PublicAPI.Unshipped.txt`, `api.md` sections 2, 6 and 13, `architecture.md`, `docs/queries.md` (paging for web APIs, whose example now takes client input with `TryParse` and answers 400), `docs/errors.md`, `docs/compatibility.md` (what a release promises for tokens: a patch release keeps both formats, a change of format is a new version in the notes, and a rolling deployment across one restarts the reads it meets). `ContinuationTokenSignerTests`: round trip, every byte flipped, another key, rotation both ways, a plain token on a signing client and a signed one on a plain client, `FromKey` on a signing client, six kinds of cross-query replay, a filter and limit that change, scan and fan-out, a fan-out of other partitions, too short and garbage version 2.
- Finding: no telemetry was added. A rejected token throws before the read starts its span, and the exception is the signal; a counter of forgeries would be a new metric, to decide with the owner.
- Finding: switching signing on or off rejects the tokens in flight once. A mode that accepts plain text tokens during a rollout was left out; it is additive.

### D6 leftovers

- [ ] `AnalyzerReleases.Shipped.md` and `PublicAPI.Shipped.txt` are labelled 0.1.0 before the release: release prep moves Unshipped into Shipped, so that "a rule that is new after 0.1.0 starts at Info" holds from the tag.
- [ ] Benchmark timings (mean and error per call) to back a speed claim; the README compares allocations only.

## M7 - DynaSharp.Coordination (after M3)

- [ ] Lease / lock: Lock Client semantics (relative lease duration, record version number, heartbeat at most half the
      lease, contender waits out the lease on a monotonic clock via `TimeProvider.GetTimestamp`), fencing token
      (monotonic counter on acquire) plus a condition helper for downstream DynamoDB writes; document that external
      systems must check the token themselves
- [ ] Leader election on top of the lease
- [ ] Sparse-GSI queue: sharded GSI key (`PENDING#k`), conditional claim with owner and lease, completion removes the
      GSI attribute, reaper resets expired claims, transactional enqueue together with a business write
- [ ] Hi/lo allocator (block claim via `ADD`, gaps on crash documented)
- [ ] Telemetry for acquire/renew/lose, claim/reap, block allocation
- [ ] M7 independent review

## Decisions

| Date | Decision |
|---|---|
| 2026-10-31 | Target `net11.0` only (personal project for now). |
| 2026-10-31 | Streams: low-level API only. No consumer, no leasing. |
| 2026-10-31 | No CRC32 response validation. |
| 2026-10-31 | Stryker break threshold 90 %. Coverage 100 % line and branch. |
| 2026-10-31 | No interface on `DynamoDbClient`; the handler seam plus the fake is the testing story. |
| 2026-10-31 | Fluent API references attributes through generated handles, not expression trees. |
| 2026-10-31 | Benchmark-only references to EfficientDynamoDb and the AWS SDK are fine. |
| 2026-10-31 | Retries: AWS standard semantics with DynamoDB-tuned defaults. |
| 2026-10-31 | Observability from M1a: traces, metrics, logs. Logging abstractions ship with .NET 11, so no package. |
| 2026-10-31 | Contiguous pooled buffers instead of segmented ones (simpler; one-shot hashing). Revisit with benchmarks. |
| 2026-10-31 | No gzip in M1. If added later, handle error bodies that claim gzip but are not compressed. |
| 2026-10-31 | `AGENTS.md` is the only file of working agreements. Specs and plans live in `spec/`, never in `README.md`. |
| 2026-10-31 | Design patterns: primitives in the core, recipes as scenario tests, first-class types only in `DynaSharp.Coordination` (lease/lock, leader election, queue, hi/lo). |
| 2026-10-31 | Optimistic locking: opt-in per entity type via `[Version]`, then enforced on every write. Not a global default (blind upserts must stay possible). |
| 2026-10-31 | Single-table design and key grammar are core requirements; the key grammar gets a design spike before M2. |
| 2026-10-31 | Integration tests run against Dynoxide (local default) and DynamoDB Local (additionally in PRs), same tests parametrized by backend. |
| 2026-10-31 | The in-memory fake stays, scoped to functional tests in C#; DynamoDB fine print is left to emulator-backed tests. Measured with the Parity Suite. |
| 2026-10-31 | `DynamoDbException.ErrorCode` is the wire code without namespace, exactly as DynamoDB sends it (for example `InternalServerError`); exception class names keep the `Exception` suffix. |
| 2026-10-31 | `AttributeValue.AsList`/`AsMap`/`As*Set` return internal storage typed as read-only interfaces (no wrapper allocation); documented that callers must not cast and mutate. |
| 2026-10-31 | `DynamoDbReader` and `DynamoDbWriter` have public constructors, so users can test custom converters. |
| 2026-10-31 | Pooled buffers are returned uncleared: the data stays in the process, which holds the same data as objects, and clearing costs on every call. |
| 2026-10-31 | `DynaSharp` grants `InternalsVisibleTo` to `DynaSharp.Testing`, its own package, shipped at the same version. |
| 2026-10-31 | `KeyConditionExpression` is `required` on `QueryRequest`: botocore marks it optional only because of the legacy `KeyConditions`, which DynaSharp leaves out. |
| 2026-10-31 | `LastEvaluatedKey` is null at the end of a result, also when the service sends an empty map, so null is the only end marker. A non-null `ExclusiveStartKey` is always written, even empty, so DynamoDB rejects it instead of silently restarting. |
| 2026-10-31 | `QueryResponse.Items` and `ScanResponse.Items` are never null; they are empty for `Select.Count`. |
| 2026-10-31 | Commits under this repository use the identity Marc Sallin `<7792779+msallin@users.noreply.github.com>` and carry no AI attribution; the global git identity on the dev machine is a placeholder. |
| 2026-10-31 | Retry classification is botocore's list reduced to what DynamoDB's JSON protocol can send: throttling `ThrottlingException`, `ProvisionedThroughputExceededException`, `TransactionInProgressException`, `RequestLimitExceeded`, `LimitExceededException`; transient `RequestTimeoutException`, `ReplicatedWriteConflictException` and HTTP 500, 502, 503, 504. |
| 2026-10-31 | The retry quota refunds as botocore does: 1 after a success without retries, the cost of the last retry after a success with retries. |
| 2026-10-31 | Clock skew compares the `Date` header with the time the attempt was signed with, and recomputes the offset from the raw clock on each skew response, so a stale offset heals itself. |
| 2026-10-31 | Advisory credential refresh runs in the background and pauses 30 s after a failure. botocore makes one caller wait and retries on every call, which costs seconds per request while the source is down. |
| 2026-10-31 | With `AWS_WEB_IDENTITY_TOKEN_FILE` set and no credentials in the environment, the chain fails loudly until M5 adds web identity. Otherwise an IRSA pod would silently sign with the node's role. |
| 2026-10-31 | Profiles follow botocore's precedence: `role_arn`, `web_identity_token_file`, `sso_*` and `login_session` fail even next to static keys; credentials-file keys are never mixed with config-file keys. Only `AWS_PROFILE` selects a profile, not `AWS_DEFAULT_PROFILE`. |
| 2026-10-31 | Standalone credentials providers have no public logger overload; only the default chain logs, through `DynamoDbClientOptions.LoggerFactory`. |
| 2026-10-31 | Key grammar: values containing a delimiter or empty values are rejected, not escaped, because escaping hides key injection (`TENANT#{Tenant}#USER#{Id}`). Signed numbers use a sign letter plus nine's complement, which stays readable. |
| 2026-10-31 | Discriminators are a dedicated `_type` attribute, not inferred from key prefixes, and `[Discriminator]` is required: a default of the type name would make renaming a class a silent migration. |
| 2026-10-31 | The key grammar had an adversarial review (80k property cases per encoding, a survey of ElectroDB, Toolbox, OneTable, PynamoDB, aws-sdk-net). All Critical to Medium findings are in `requirements.md`. Default timestamp stays `ms`, truncation documented, because the attribute keeps full precision. |
| 2026-10-31 | In batch and transaction responses `Responses`, `ConsumedCapacity`, `ItemCollectionMetrics` and the unprocessed collections are never null. `CancellationReason.Code` is a string, because the service adds codes. |
| 2026-10-31 | `aws.dynamodb.table_names` is set for transactions too, as the natural extension: semconv defines it only for `RequestItems`. |
| 2026-10-31 | Unit tests run with `--project`, not `--solution`: the solution now holds the integration project, which needs Docker. |
| 2026-10-31 | `ListStreams` returns `StreamSummary`, not botocore's `Stream`, which is ambiguous with `System.IO.Stream` under implicit usings. `DynaSharp.Model` otherwise keeps service names. |
| 2026-10-31 | Streams spans are `DynamoDB Streams.<Operation>` (`rpc.service` as botocore's serviceId) and keep `db.system.name=aws.dynamodb` and `db.operation.name`, so the duration metric stays valid. `ServiceUrl` applies to both clients, because an emulator serves both APIs on one address. |
| 2026-10-31 | Query item dictionaries are sized like the previous item, which closed a 25 % allocation gap to EfficientDynamoDb. |
| 2026-10-31 | `RecordingHandler` disposes its inner handler (`DelegatingHandler` convention); `DynamoDbClient` still does not dispose a handler it is given. |
| 2026-10-31 | API decision by the owner: `Table<T>` is primary, the request API a sibling in `DynaSharp.Requests`, built before the generator. Contract in `history/api-design.md`. Commands are ref structs for safety. |
| 2026-10-31 | Compile-time JSON templates are not planned: 0 B gain, 0.08 % of a round trip. An analyzer covers the validation. |
| 2026-10-31 | All exceptions stay in `DynaSharp`, so a catch site needs one import. A type goes to `DynaSharp.Streams` only if nothing outside the streams operations reaches it; `Streams` depends on `Requests`, never the reverse. |
| 2026-11-01 | Generator snapshots use a local comparer instead of Verify: Verify 33 needs a sponsorship declaration that only the owner can make, and the comparer is 40 lines. |
| 2026-11-01 | A `char` converts to a string value. `[Obsolete(error)]` on the operator broke consumers' TUnit tests, because TUnit's generator emits every implicit operator of a parameter type (reported as thomhurst/TUnit#7316, still present in 1.77.0). |
| 2026-11-01 | An `Item` inside an `AttributeValue` is read-only, to keep `AttributeValue` immutable; `new Item(other)` makes an editable copy. `Item` getters return null for a missing attribute and for DynamoDB NULL. Number errors name the attribute, never the text. |
| 2026-11-01 | `HttpClient` stays; Put at 1.49 KB instead of 1.41 KB is accepted: 80 B is not worth a different disposal contract. Response streams are read through the synchronous `ReadAsStream`, saving 72 B; a custom handler whose content builds its stream on demand can block there. |
| 2026-11-01 | A `char` is a string value everywhere, fixed in `DynamoDbWriter.WriteNumber<T>` because `char` is an `INumberBase<char>`. `ConditionalCheckFailedException.Item` is `Item?`. |
| 2026-11-01 | A token is client input: `ContinuationToken.Parse` accepts exactly one JSON object of attribute values, because it reaches the body as raw bytes. Tokens are not signed. `Parse("")` gives the default token, so a loop on `Next.ToString()` must check `HasMore`. Parallel scan concurrency is the segment count. |
| 2026-11-01 | Batch retries are bounded by requests in a row without progress (8), not by rounds, so a 16 MB read cannot exhaust them. Throttling on a request's last attempt becomes the incomplete exception with the throttle inside, or the caller loses what was written. `BatchIncompleteException` and `BatchGetIncompleteException<T>` derive from `DynamoDbException` with code `BatchIncomplete`. `ConsumedCapacity` on a batch is one entry per table and request, not a sum. |
| 2026-11-01 | `TransactWrite` verbs are generic extension methods over `ITransactWriteChain<T>` and `ITransactWriteStep<T>`, whose members are internal: instance methods would be about 60 repeated members. Price: `await shop.Query(...)` now reads CS0315 instead of CS1061, a worse message. To be weighed in the review. |
| 2026-11-01 | The generated entity has no seam to the key emitter: `WriteEmitter` and `ReadEmitter` emit the final calls into `Keys`. A partial-method guard would silently drop keys. |
| 2026-11-01 | Owner's decision: Stryker and the AOT publish are paused to iterate faster, and run once before the first push. That pass is mandatory: the first green CI on `main` tags and releases v0.1.0, and CI runs both. M2 and M3 stay open until then. Until that pass, `samples` and `tests/DynaSharp.AotSmoke` are only compiled, not published or run. |
| 2026-11-01 | Owner's decision: `main` starts as one commit, and work lands there as ordinary commits. `release.yml` is off, so nothing is tagged or released until its trigger is restored. Stryker runs by `workflow_dispatch` only, which replaces the nightly run. |
| 2026-10-31 | Stryker is not a per-package gate. It runs once before a milestone closes, followed by a refactoring pass; CI keeps enforcing >= 90 %. Keeps iteration fast. |
| 2026-11-03 | Owner's decision, reversing the one of 2026-10-31 that kept `ms`: a timestamp placeholder without a format is `ticks`, before 0.1.0 freezes the key format (Q14). `ms` cut a key off from its own attribute: an input of a table key could not be restored from the key of an item an `Update` created, `[KeyOnly]` needed an explicit format, `DYNA116` fired on the default, and a key built from a value read from the attribute missed the item. `ticks` costs 4 bytes a key and keeps sort order and date prefixes; `{At:ms}` stays for keys the AWS SDK wrote. |
| 2026-11-03 | Owner's decision, security review: `KeyWriter` returns its arrays uncleared like every other pooled buffer, and the in-process threat model is decision 18 of `architecture.md`. An http endpoint is not rejected, because DynamoDB Local in docker-compose (`http://dynamodb-local:8000`) must work; on a host that is not loopback the client warns once (event 9), and the web identity provider once for STS (event 25). A custom `HttpHandler` reads the request content only before its `SendAsync` returns and sends a request once. |
| 2026-11-03 | Owner's decision (R2): continuation tokens can be signed, opt-in, with `DynamoDbClientOptions.TokenSigner`. Plain tokens stay as decided on 2026-11-01. Details under "R2" in "M4 and later". |

## Findings and notes

- G5 findings to fix after G6 merges: two indexes sharing a key attribute under different sparse gates lose items in the second index (needs a diagnostic or model change); `Order` and `ORDER` in one namespace collide on the hint name, CS8785, all output lost; a custom converter with explicit static interface members passes DYNA008 then fails in generated code (reasoned, not run); the non-nullable set rule always replaces an initializer, so `= []` allocates a set that is thrown away; a malformed value in a skipped attribute is reported under the last known name.
- A2 findings: a typed query on an index that does not project the discriminator returns nothing silently (documented); an index keyed on `_type` is rejected on a typed query, because DynamoDB refuses filters on key attributes (needs a skip or a diagnostic); `DescribeAsync` returns a type from `DynaSharp.Requests`.
- A3 findings: the emulators never produce `UnprocessedItems` or `TransactionConflict`, so these paths are tested with stubs only; `Keys(...)` takes `IKey<T>`, so `Table<Item>` needs a loop over `Key(pk, sk)`; the span of a retry round names the tables of its original chunk.
- Analyzer shipped: DYNA201, DYNA202 (`await` on a read command), DYNA210 to DYNA214. `dotnet build` shows a warning beside CS4007 on the same span. DYNA213 can fail a warnings-as-errors build for a legitimate raw name outside the entity; accepted, since a variable or a pragma silences it and it is what section 5 asked for. Dynoxide differs from DynamoDB Local: it rejects `(a) = :v`, `a = (:v)`, `a[ 0 ]` and `size(:v)` and accepts `SIZE(a)`, `_a` and 101 `IN` operands.
- Command API, final numbers (KB, untyped / typed, proposal in brackets): Put 1.49 / 1.49 (1.41), Update 1.49 (1.41), Get 2.52 / 2.13 (2.44 / 2.16), transaction of 2 puts 2.12 / 1.49 (2.04 / 1.41), batch of 25 6.38 / 1.49 (6.30 / 1.41), batch of 60 16.20 / 4.48 (15.96 / 4.24), Query of 100 reading 3 of 10 attributes 97.95 / 66.70 (98.56 / 70.39). Suspending Put 1.81 (1.55), suspending Get 2.10 (1.67), not explained yet; likely the state machine of `HttpClient`. Sockets not measured. Update body 450 to 610 ns, above the 300 to 400 ns target.
- A4 findings: jitter cannot be fixed by a consumer, so a retry test advances a fake clock until the call is done; helpers cover one table in `Unprocessed` and no `ConsumedCapacity` or `TransactGetItems`, left to `Json` on purpose.
- **G8** merged. Bytes, generated / hand-written / `Table<Item>` / request API: per item 656 / 688 / 976; Query of 100 68,320 / 71,520 / 100,320 / 100,456; Get 2,256 / 2,288 / 2,576 / 2,608; Put 1,528 / 1,528 / n/a / 1,640. Template keys read 176 B less per item than plain strings. Against other mapped clients, DynaSharp / EfficientDynamoDb / AWS `DynamoDBContext`: Get 2,384 / 5,936 / 40,794, Put 1,528 / 5,112 / 44,420, Query of 100 with a `HashSet` 81,120 / 91,810 / 991,052. Hand-written comparison types are `HandWrittenOrder` and `HandWrittenAddress`, because A4 moved `BenchOrder` to generated code. AOT size grew 672 KB for about 45 shapes. Timings are noise on this machine: do not rank on them.
- API friction from writing the sample (A5 takes 1 to 4; the rest is the owner's call): (1) `(await t.Get(k).SendAsync(ct)).Item!` needs a terminal `ItemAsync(ct)`; (2) transaction steps do not assign in a loop: add implicit conversion from each step to `TransactWriteCommand`; (3) index names are strings: generate `Order.Indexes.ByStatus` constants; (4) `BatchWrite` takes no sequence, unlike `BatchGet().Keys`; (5) `Query` takes an `AttributeValue`, so `Customer.Keys.Pk.Format(x)` compiles on `orders` and the type name repeats; (6) no `Table<T>` create or delete, about 25 lines in the sample; (7) `Set(handle, [])` compiles and DynamoDB rejects it while `Put` omits it; (8) a sign-only range needs the on-disk prefix; (9) a `var` holding `.At(...)` breaks `dotnet format`, whose workspace has no generated code.
- A non-nullable property of an enum with no members throws on every write with no diagnostic.
- `UpdateTable_CreateGlobalSecondaryIndexOnProvisionedTable_ReportsTheIndexCapacity` hit one 500 from DynamoDB Local under full machine load; three reruns of the suite were clean.
- M3a findings: after a lost response, a retried versioned write fails its own condition although the first attempt applied (documented on `PutCommand.SendAsync`); `.IfVersion(n)` checks `long`, not the property type, so an `int` version past its maximum stores a value that later fails to read; `TransactWriteResult` carries no stored versions; an update of a key input boxes the value and allocates the key string, every other path measured 0 B.
- A5 decisions by the lead: a fresh `TransactWriteCommand` can be sent (it throws on no action), because the loop form could not be sent otherwise; the three guards went with it. `DeleteAll<TKey>` is generic, because struct keys have no variance. `ItemAsync` returns `T?`. Accepted restriction: changing one key of an item already in a sparse index needs a raw path or a `Put`. Open: `Delete(entity)` reads the version at the call, so a `ulong` above `long.MaxValue` throws even before `IgnoreVersion()`; DYNA024 misses a `[KeyOnly]` property of an empty enum.
- M3b: a fan-out token carries the start key and a hint of the next item per partition: 23 instead of 48 reads over six days of four items in pages of two. `Page.ConsumedCapacity` on a fan-out page adds every response of that `PageAsync`, also of items that did not reach the page. `Query(default)` and `Query([])` are CS0121 now; a default key always threw, and an empty list has to be a typed variable. Semaphore-gated tests hang instead of failing on a regression: run with `--timeout` when one stalls.
- Recipes not written: distributed lock, leader election, hi/lo, queue and priority queue are Coordination (M7); rollups need Streams. Caveats only stated in comments: TTL deletion lag, about 1000 WCU/s per item, Streams limits. Recipes read a GSI right after a write, which holds on emulators only. New gap `TransactionCapacityPerItem`: DynamoDB Local reports 2 units for a transaction of two items and Dynoxide 4.
- R1 notes: handles drop `?`, so a literal null needs `null!` and a `Guid?` handle takes no null, `Remove` is the clean form (changing that means changing the generator); `SetIfNotExists`, `Add` and `Delete` by handle still throw on null; a third type on an index keyed on the discriminator reaches the client and throws, because the query has no filter; `Put` of an entity at the last version its property holds throws `OverflowException` before `IgnoreVersion`; `IgnoreVersion` on a `Put` serializes the item twice, and a struct entity with `[Version]` boxes once per `Put`.
- R2 notes: `Query(string[])` is CS0121 below C# 13, because the compiler ignores the priority attribute; accepted, the library targets net11. A literal key of a non-sparse index, such as `PartitionKey = "ALL"`, is not written by an update, so an item an update creates stays out of that index silently. Nullable key inputs are not restored on read: null is omitted on write, and a stale sparse key must not bring a removed value back. The read fallback costs one struct copy per item, not measured.
- Second independent review (after the R2 merge, second reviewer; generator bodies, serialization, telemetry, credentials, lost-response retries): no Critical or High. Fixed in R3: expired container credentials fell through to the instance role in the default chain; a type named `Keys`, `Attrs` or `Indexes` gave CS0542 in generated code; `PutCommand.SendAsync` called a retry safe although a condition or `ReturnOld` change the outcome. Left open, Low: credential expiry is judged on the uncorrected local clock while signing uses the corrected one, and `ExpiredTokenException` does not trigger a refresh, so a clock more than 10 minutes behind fails until the next mandatory refresh. Not reached by either review: `ErrorMapper` parsing details and Streams operations beyond their tags.
- Refactoring pass: 128 lines fewer across 38 files, public API, snapshots and allocation tests unchanged. Left on purpose: repetition of ref struct commands, doc comments between `QueryCommand` and `ScanCommand`, `BoundedReads` beside `ParallelScan`, `PagedRead` beside `FanOutRead`. Possible follow-up: a `KeyTemplate.Placeholders` in the generator model. `IfVersion` followed by `IgnoreVersion` throws, because the condition cannot be taken out of the expressions; the other order lets `IfVersion` win.
- R3: `CredentialsOutcome` has a `Kind`, and the chain moves on only for an explicit `DoesNotApply`, with `Broken` as the zero value. A profile named by `AWS_PROFILE` that is missing stops the chain now, a behaviour change, believed to match botocore (not checked against its source). A secret without an access key still moves on, pinned for parity with botocore. DYNA025 rejects a type named like a class generated into it.
- Maintainability review (second reviewer, on the tip after R3): no rewrite needed. Decisions by the lead. Before 0.1.0: package M1 (guards): a test that every field of `CommandState` is reset by `Release`, an ownership table in its remarks, an agreement test between the keyword lists of `ExpressionWriter` and the analyzer, a test that every read clause is forwarded by the mixed commands, `LangVersion` on the runtime projects, Stryker out of the PR path, analyzer rules moved to Shipped, generator-contract members hidden from IntelliSense and named infrastructure in `requirements.md`. Package M2 (docs): `spec/architecture.md` as the code is today, design documents frozen under `spec/history/`, help links on a stable page with a test. Reversed: DYNA212 and DYNA214 are warnings for 0.x, because a false error in a consumer's build can only be fixed by a new package. The owner approved `Microsoft.CodeAnalysis.PublicApiAnalyzers` as a build-time dependency (package O1). Later: `WriteState` and `ReadOptions` structs in `CommandState`, emitters all on `IndentedCode`, splitting the `KeyFormats` snapshot, `Fluent` folders and a `CommandPool`. The review did not read `KeyWriter`, `DynamoDbReader`, `Credentials`, `Telemetry` or the `FanOut` internals for maintainability, and ran no integration tests.
- Maintainability packages merged. The reset test found `CommandState._itemStart` and `ExpressionWriter.ValueJson` not reset, neither a reachable leak. Stryker runs nightly and by dispatch only, so `release.yml` is no longer gated on it: the rule that it runs before the first push is what holds this. `DynamoDbMappingException` and `CredentialsUnavailableException` moved to `DynaSharp`, as requirements says.
- Owner's calls, taken as recommended on 2026-11-01: typed partition keys and sort conditions, null by handle, `Index<TEntity>` on an untyped table, literal keys on every index and public API tracking go into package O1 (branch `m3/o1-owner-calls`); `Table<T>` create and delete and a conflict retry helper wait. Contract in `api.md`.
- O1 merged. Lead's decisions: `Of` exists on partition key builders only, because a `PartitionKey` from a sort key would be the mistake itself; the table key on an index compiles, documented; a literal sort key on a local index counts as all-literal (still to do). The owner reversed two calls: `Table<T>` create and delete and a conflict retry helper are in after all (N1, N2), with the credential clock (N3), the emitters (C1), `CommandState` grouping and the `Fluent` split. CI builds the generator before `dotnet format`, which otherwise reports CS1503 on a named `PartitionKey<T>`.
- N3 merged. Client-owned providers judge expiry on the corrected clock; `ExpiredTokenException` and `ExpiredToken` drop the rejected credentials and resend once. botocore lists no such codes for DynamoDB, so the source is `credsExpiredCodes` of the AWS SDK for Go v1, recorded on `FailureClassifier`. Accepted limits: a provider the application created still judges on the local clock and recovers through one rejected request; during an IMDS outage a stale identity costs one extra fetch per call, past `LimitRefreshRate`; `ExpiredTokenException` cannot be produced by the emulators, so the resend has unit tests only.
- N2 merged. `RetryOnConflictAsync` retries `ConditionalCheckFailedException`, `TransactionConflictException` and a cancelled transaction with those reasons, with up to 5 runs and at most 1 s of jitter. n writers on a version need n runs. Risk documented and pinned: a conditional write whose response is lost fails its own retry, and the helper then applies the change again; `.MaxAttempts(1)` on the write avoids it.
- N1 and C1 merged. Open by design: a custom converter on a plain key that writes binary, or a number without `IAddableConverter`, is created as a string, and the first put fails; `ToRequest()` is the way out. DynamoDB Local fails with `InternalFailure` on a provisioned table with 2 global and 2 local indexes under parallel load, so two tests carry `[NotInParallel]`.
- Owner's decisions after the focus group, 2026-11-01, all as recommended. Before 0.1.0: web identity in the default chain with no dependency; a real AWS run through GitHub OIDC; `SortKey` becomes `SortKeyCondition`; generated `Keys.Sk.Between/Eq/Ge/Gt/Le/Lt`; `ms` stays the default for a timestamp in a key, with the warning only where a row can be lost; `WriteResult.Attributes` becomes `Item`; stream images are `Item`; `Limit` keeps its name and its docs show `.ItemsAsync(ct).Take(n)`; methods only for conditions; no warning on a plain `Put`, a README decision table instead; typed updates never write key inputs by default; try `net10.0` in a branch; a version byte in continuation tokens; a longer transient retry window; default names `pk`, `sk`, `<index>pk` and `_type` are frozen. Later, additive: `{Key:raw}` and `TryOf`, project-wide defaults, `IDynamoDbConverter<T?>`, testing helpers, a non-consuming `ToRequestJson()`, `BatchWrite().MaxConcurrency(n)`, bounded fan-out tokens, a DI helper, `Item.IsReadOnly`. The focus group report is not in the repo.
- Owner's decision: `net11.0` only, updating to GA when it ships. A wide audience is not a goal, so no LTS target. The try built and passed all tests on a real .NET 10 runtime, at the price of a dependency on `Microsoft.Extensions.Logging.Abstractions` for `net10.0`; the branch is local.
- Coverage flake closed: `OperationTelemetry.RecordResponse` depended on no other test listening to the capacity metric at that moment (99.99 % on some runs). The decision takes `metering` as a parameter now.

- C3: `CancellationTokenSource.TryReset` is true only for timers from `TimeProvider.System`, so with a fake clock no source is reused: safe, but reuse is only exercised on the system clock. The signer no longer validates requests or replaces headers; the pipeline is its only caller.

- C2: renting per nested map cost about 30 % CPU on Query (a nested rent misses the pool's thread cache), so 16 attributes of scratch live on the stack. Plain FNV collapses for names like `attribute07`; the hash has a MurmurHash3 finalizer. `cond ? x : null` on `ReadOnlyMemory<byte>?` gives empty, not null.

- P12: Stryker 5 with the MTP runner is unreliable for static-initializer mutants and near timeouts: they flip between Killed, Timeout and Survived across runs. Hand-check before believing a drop. `ClientCoreTests` reads `_http` by reflection and fails loudly on a rename.

- P14: `AttributeValue v = null` is now a compile error (ambiguous), `ulong` has no implicit conversion, and a `char` converts as a number through `int`, so `'a'` becomes 97. Dynoxide ignores GSI capacity; DynamoDB Local rejects an `UpdateTable` that only changes the table class.

- P10 API friction from the sample, open until the API layers decision: options cannot be copied (`with`); `MaxAttempts` is client-wide; implicit conversions only for strings; dictionary boilerplate; paging, waiting for ACTIVE and unprocessed loops are hand-written; items are hard to print and read; cancellation reasons are strings matched by index; exceptions do not name the operation or table; reserved words fail only at runtime and the docs do not warn; `StaticCredentialsProvider` needs two objects; no `ListTables`, `DescribeTimeToLive`, `UpdateTable`; capacity metric has no index tag; retry lists differ between `DynamoDbClient` remarks and `MaxAttempts`.
- P10: Dynoxide 1.5.0 keeps a stream under the table name, so a recreated table replays the old table's records. The sample uses a per-run table name.

- BenchmarkDotNet builds its child project from the process start directory, not from `--project`. Set the current directory to the worktree, or a run measures another tree.

- P7: neither emulator can produce `TransactionConflict` or `TransactionInProgress` or multiple shards (200 parallel transactions on one item all succeed), so those stay untested against a real service. Only a real AWS backend would close that.
- P7: Dynoxide gaps, skipped per test: repeated TTL enable accepted, disabling a stream removes it, open shards have an ending sequence number, DescribeStream lacks KeySchema, TransactGet with a projection returns keys. DynamoDB Local answers concurrent DDL with `InternalFailure` (HTTP 500, so the client retries it) and sends an empty `Message` for `IdempotentParameterMismatch`.

- P6 benchmarks, DynaSharp vs EfficientDynamoDb 0.9.19 vs AWSSDK.DynamoDBv2 4.0.109, same canned HTTP, 10-attribute item, `--job short` on a busy
  machine (times rough, allocations exact). GetItem: 4.98 KB, 7.16 KB, 30.51 KB; about 11, 23, 61 us. PutItem: 2.49 KB, 4.99 KB, 26.71 KB; about 8,
  17, 35 us. Query of 100 items: 187 KB, 202 KB, 379 KB; 305, 323, 686 us, level with EfficientDynamoDb within noise. The performance requirement holds.
  P8 added a per-response attribute name table: Query of 100 items is 142.73 KB, 29 % below EfficientDynamoDb, about 20 % faster on CPU. What remains is value strings and per-item dictionaries; a shape shared across a page is the next lever. R8's writer reuse and date
  cache stay undone: allocations already beat EfficientDynamoDb.
- P5: the DescribeStream reference sample prints a date string and a trailing comma; the service sends epoch seconds. `Record.Region` is `awsRegion`.

- P7, emulators only, not real AWS: `-0` is accepted and stored as `0`. Exponent forms such as `1E+21` and `1E-07` are accepted and read back
  normalized, so numbers must be compared numerically. `+5`, `.5`, `5.` and `0005` are accepted; whitespace, `0x10`, `1_000` and `1,5` are not.
  `double.MaxValue` and `double.Epsilon` are outside DynamoDB's range.
- P7: `ErrorMapper` matches both emulators' wire errors. DynamoDB Local sends `Message`, Dynoxide `message`. Message wording differs,
  so tests assert on codes. Dynoxide returns `CREATING` from CreateTable; DynamoDB Local returns `ACTIVE`. Dynoxide 1.5.0 honors`r
  `ConsistentRead` in capacity and GSI validation, so the note above that it is a no-op is out of date for what tests can see.
- P7: Parity Suite: `ground-truth/` holds only a README, `captures/` holds SDK-level records without `__type` or status, so no wire-level error
  data. 12 real AWS item bodies were taken into `real_aws_responses.json`, with attribution in `NOTICE`. Real AWS accepts empty members in`r
  `SS` and `BS` sets. The `ghcr` tag of Dynoxide is `1.5.0`, without `v`. Testcontainers also needs `testcontainers/ryuk` from Docker Hub.

- P4: `TransactionConflict` is a cancellation reason code; the wire error of a single-item operation hitting a transaction is
  `TransactionConflictException`. A missing item in `TransactGetItems` is `{}` on the wire though the API reference says null; both are read.
- P4: botocore's modeled parse of `TransactionCanceledException` reads only `Message`, not `message`; the mapper accepts both.
  The API reference has no worked examples for transactions, so those vectors are crafted from its syntax, and its BatchGetItem response
  sample is invalid JSON (a comma is missing).
- P4: `StubResponse.DynamoDbError` cannot carry `CancellationReasons`. Requirements ask for fault injection of transaction conflicts,
  so add a helper with M3. Counters without a table tag have no per-test discriminator: only one test per operation may use them.
- `[IO.File]` resolves relative paths against the process start directory, not `Set-Location`, so a script can write into another worktree.
  It happened once in M1b. A test run with `--no-build` after a failed build reports the old binaries: check the build exit code first.

- P2: a method-group conversion such as `Environment.GetEnvironmentVariable` at several call sites creates `??=` cache branches that
  cannot all be covered; use one shared static delegate. `new CancellationTokenSource(timeout, timeProvider)` replaces the missing
  `TimeProvider.CreateCancellationTokenSource`. TUnit accepts only one `--treenode-filter`.
- P2: botocore keeps inline `#` comments in values, inherits `[DEFAULT]` into every section, matches `[profile x]` by prefix and splits it
  like a shell, and fails the whole file on a bad nested block. Chain order there: env, assume role, web identity, SSO, credentials
  file, login, process, config file, container, IMDS.
- P2: the container and IMDS providers deviate from botocore in small ways: an empty variable counts as unset, a token file is trimmed,
  `FULL_URI` with user info is rejected, URIs are never echoed because the ECS path holds a credential id. IPv6 IMDS is not supported.
- `ClientConfiguration.Resolve` without a region and without a reader reads the machine's real shared files: tests must set a region or
  pass a reader.

- P1: botocore has no clock-skew correction (it only estimates a TTL from `Date`), so the skew oracle is the AWS SDKs.
- P1: `Task.Delay(TimeSpan, TimeProvider, token)` truncates to whole milliseconds and returns a completed task for 0.
- P1: the retry quota uses `Interlocked.Add` with rollback rather than a CAS loop, whose contention branch cannot be covered
  deterministically. A `Date` header such as year 9999 is clamped, so it cannot make signing throw permanently.
- P1: `GetItem` allocation fell from 5.12 KB to 5.05 KB (`AttemptAsync` is a `ValueTask`). `DynamoDbException.ClockSkewCorrected`
  is an internal mutable flag that carries a decision through the exception; revisit in the refactoring pass.
- `TestClient.Create` defaults to `maxAttempts: 1`; retry tests opt in.

- P3: `SetActivityTags` must never throw: the pipeline starts the span before its `try`, so a throw would leak the activity.
  `GetSelectName` returns null for undefined values and `WriteSelect` throws during serialization instead.
- P3: benchmark for a Query page of 100 items of 10 attributes (43 KB): 327 us, 253 KB, about 3.3 us and 2.5 KB per item;
  GetItem unchanged at 5.12 KB. The `Items` array costs 0.8 KB of that, the rest is per-item `Dictionary` and `AttributeValue` parsing.
  Machine busy, so times are rough.
- P3: botocore 1.42.21 does not know `ConsumedCapacity.VectorIndexes`, which the API reference lists. Unknown members are skipped,
  and a vector pins that. botocore validates `Limit >= 1` client-side, so `Limit` 0 is covered by end-to-end tests instead.
- `Select` as a public type name triggers CA1716 (a VB keyword), suppressed on the enum: the name mirrors the service model.
- The dev machine's global git identity is a placeholder `a <a@b>`. Pass `-c user.name` and `-c user.email` to commit and rebase.

- Client benchmark (`--job short`, Windows x64, 22 logical cores, GetItem of a 10-attribute item against `StubHandler`):
  6.77 us (error 5.74 us, stddev 0.31 us; three iterations on a busy machine, so treat the time as rough), 5.32 KB
  allocated per call; 5.12 KB after R8.
- `ClientBenchmarks` was `sealed`, which BenchmarkDotNet rejects, and the run still exits 0. Check that a benchmark run
  reports `executed benchmarks: N` with N > 0.
- File-based apps default to Native AOT, so `build/packages.lock.json` pinned the Linux ILCompiler and the coverage gate
  failed under `CI=true` on Windows. `build/CoverageGate.cs` sets `PublishAot=false`.
- Visual Studio without the pinned SDK installed keeps emptying the lock files in an open checkout (`"net11.0": {}`).
  Close it or install the SDK before a locked restore there.
- Native AOT on Windows needs the "Desktop development with C++" workload. Without it, run the `-r linux-x64` gate in a
  Linux container.
- C: telemetry names follow semconv v1.44.0 (read from the GitHub sources); `rpc.system` / `rpc.service` are emitted
  although marked deprecated, because the AWS SDK conventions still prescribe them. Histogram bucket boundaries are
  unverified.
- C: TUnit registers a global `ActivityListener`, so `StartActivity` never returns null in tests; the null path is
  tested with an injectable `ActivitySource`.
- C: string `switch` with 7+ cases produces compiler branches that cannot be covered; dictionaries are used instead.
- C: the wire codes for `InternalServerError` and `RequestLimitExceeded` have no `Exception` suffix. The mapper
  accepts both, but the exception classes report the suffixed `ErrorCode`. Decide in the review which one is right.
- C: `stryker-config.json` has no `project` entry any more, so Stryker mutates both library projects.
- M0 bug found in M1a: publishing the AOT smoke test restores referenced libraries for its runtime identifier, which
  changed their lock files and would fail locked restore in CI. `src/Directory.Build.props` now lists the same
  `RuntimeIdentifiers` for all library projects.
- After a Stryker run, Release binaries under `bin/` may contain the mutated assembly; running the test DLL directly
  then fails. `dotnet test` rebuilds, so only manual runs are affected.
- Tests must not depend on `ArrayPool<byte>.Shared` behavior (shared global state); inject the pool instead, and
  assert with a counting pool that every rented array is returned once (a missing `Return` is a leak no other test sees).
- `Utf8JsonReader` default `MaxDepth` 64 is too low: DynamoDB allows 32 nesting levels and each level is two JSON
  levels (`{"M":{...}}`). The reader uses 128.
- MS CodeCoverage counts a hidden compiler branch when a method on a generic `T` gets a property call as argument;
  pass a local instead.
- Some tools turn `\uXXXX` escapes into the actual characters. Write escaped-JSON test data
  programmatically or verify it after writing.
- Internal converter classes trigger CA1812 in consumer projects without `InternalsVisibleTo`; documentation examples
  use `public sealed class`.
- B1 benchmark (20-attribute item, low-level `AttributeValue` path): write 1.74 us / 168 B, read 4.98 us / 6.4 KB.
- Stryker.NET 5 supports TUnit only via `"test-runner": "mtp"`, flagged preview. Spot-check surprising results.
- BenchmarkDotNet 0.15.x does not recognize net11.0; 0.16.0-preview is required until a stable release.
- SDK 11.0.100-rc.3: `dotnet publish -o` fails copying the XML doc file for executables;
  `PublishDocumentationFile=false` in the AOT smoke project works around it.
- Pattern catalog review (`patterns.md`): idempotent counter cost grows with item size (writes are billed on the
  larger of the before/after image); a sparse-GSI queue needs a sharded GSI key or it creates a hot partition, and GSI
  throttling throttles base-table writes; zero-padding orders only non-negative integers; TTL deletion lag is
  "a few days", not 48 h (~70 %); fencing tokens protect only systems that check them.
- Independent review of the M1-M3 plan found: signing-key cache must include the credentials identity (rotation),
  two serialization paths would force a rewrite in M2, M1a was too large. All incorporated above.
