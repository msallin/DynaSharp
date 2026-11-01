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

## M4 and later

- [ ] M4: in-memory fake in `DynaSharp.Testing` for functional tests (scope: see requirements; not DynamoDB's
      fine print). Plug it into the backend-parametrized scenario tests. Host it behind a small HTTP listener
      (test tool, `HttpListener`) so the Parity Suite tiers 1 and 2 can run against it; track its score.
- [ ] M5: web identity (IRSA, STS XML), SSO, `credential_process`, assume role
- [ ] M6: first release 0.1.0 (enable `PUBLISH_TO_NUGET`, configure trusted publishing)

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
