# DynaSharp - tasks (blackboard)

Shared plan and todo list. Claim a task before starting it (`[~] owner`), mark it done (`[x]`) in the same change that completes it, and record decisions (with their why) and findings here. Requirements live in `requirements.md`, the public contract in `api.md`, the code as it is in `architecture.md`. Git history holds the narrative of finished work; this file keeps what still constrains or helps.

Legend: `[ ]` open, `[~]` in progress (owner), `[x]` done, `[-]` dropped (reason).

## Handoff (state at 2026-11-08)

Repository: https://github.com/msallin/DynaSharp (private). Docs site: https://msallin.github.io/DynaSharp/ (public, built by `docs.yml` from `main`). Commits are authored as `Marc Sallin <7792779+msallin@users.noreply.github.com>`; the `gh` account is `msallin` (`GH_TOKEN="$(gh auth token --user msallin)"` when several accounts are signed in).

Where the code is:
- Latest release `v0.8.0`, tag on `47c9db7` ("chore: mark the API and analyzer rules of 0.8.0 as shipped"): `DynaSharp`, `DynaSharp.Testing` and `DynaSharp.Coordination` on nuget.org.
- Since then on `main`: a docs pass over the README and `docs/` ([Limits](../docs/errors.md#limits), [Changing a model](../docs/migrating.md#changing-a-model), the `IfVersion`/`IgnoreVersion` decision table); the Stryker survivor passes of the `fluent` and `core` shards (run on `e78cb67`), with tests that kill mutants and refactors that drop dead branches; the specs compacted (the frozen design documents, the pattern catalog and the plan of the fake removed: the fake's rules are in `architecture.md` section 14, the patterns in `docs/patterns.md` and `tests/DynaSharp.IntegrationTests/Recipes/`); the project skills `releasing`, `mutation-testing` and `fake-fidelity`.
- Six services use DynaSharp on branches `feat/dynasharp` (data-pond, nrt-showcase, yats, customer-adapter, socce-cockpit, lps-label-print-server); a push of those deploys to a shared environment.

Next steps: the Stryker cycle, then the owner items under Open.

Releasing: the `releasing` skill. In short: `build/ship-public-api.sh` and a `chore` commit, then `release.yml` by `workflow_dispatch` on `main`; it releases the head only after a green CI run of a push for it, and derives the version from the Conventional Commits since the last tag.

How to work:
- Local: SDK exactly `11.0.100-rc.3.26522.148` (`global.json`, no roll-forward), `dotnet tool restore` (docfx, Stryker). Run unit tests with `-c Release`: the allocation tests (`CommandAllocationTests` and others) fail in Debug, where the JIT does not optimize. Docker only for the emulator backends. Python with `botocore` for the vector scripts.
- Run locally only the build, the tests that cover a change, `dotnet format whitespace --verify-no-changes` and ReadmeCheck; the full unit suite with the coverage gate, the AOT smoke and the integration tests (`dynoxide,dynamodb-local,fake`) run in CI. Gate a change by pushing its tree as a `ci/merge-*` branch (`ci.yml` runs on `main` and `ci/**`), wait for `ci.yml` and `docs.yml`, then put it onto `main` as one commit per package and delete the branch. History is linear.
- Docs-only changes may go to `main` without the gate. Docs describe what the library does: no status notes, no "not yet"; open work goes into this file.
- Stryker: `ci.yml` job `mutation`, `workflow_dispatch` only, one shard per runner within the six-hour job limit (the `mutation-testing` skill).
- Real AWS: `real-aws.yml` (`workflow_dispatch`, input `filter`, for example `/*/*/FidelityTests/*`) assumes `arn:aws:iam::211125322028:role/dynasharp-integration-tests` in `eu-central-1` over GitHub OIDC (repository variables `AWS_ROLE_ARN`, `AWS_REGION`; the trust policy uses the immutable subject `repo:msallin@7792779/DynaSharp@1440878896:*`; permissions are limited to tables `dynasharp-it-*`, see the header of the workflow). It costs money; a full run takes about 90 minutes and last passed 886, skipped 10, failed 0. A run removes leftover tables older than six hours.
- Fidelity of the fake: the `fake-fidelity` skill, `differential.yml`, `parity.yml`, and the section below.

## Open

Release and quality:
- [ ] **Stryker cycle** before the next release (owner, 2026-11-06), break at 90 % per shard. `generators`, `testing` and `coordination` have no score yet (the first two hit the six-hour limit in run 41671331841, `coordination` never ran): run them, split a shard by its `mutate` glob if it outgrows the limit, work the survivors. The `fluent` (163) and `core` (226) survivors of `e78cb67` are worked off, kills checked by hand; one run of those two shards confirms their scores.
- [ ] `docfx metadata` warns "Duplicate type parameter 'T1'/'T2' found in comments" (8 times), probably on the mixed readings (`ReadChangesAsync<T1, T2...>`, `MixedChange`); `docs.yml` applies `--warningsAsErrors` only to `docfx build`.
- [ ] `LastEvaluatedKey` of a query page that `Limit` ends at the last match: the rule on the partition key alone (no rule fits the four answers of real DynamoDB, see Fidelity), and whether `begins_with`, `BETWEEN`, `>` or a filter behave as `sk <= :end`, which the fake assumes. Ask with `FidelityTests` on `real-aws.yml`.
- [ ] Real DynamoDB: run `StreamTests.GetShardIterator_AtTheStartingSequenceNumberOfAShard_ReadsFromItsFirstRecord` with `DYNASHARP_BACKENDS=aws`; it decides whether the 23-hour age rule of child shards can read every child at its starting sequence number instead.
- [ ] The open questions of the fake (Fidelity section): ask real DynamoDB through `FidelityTests` when one matters.
- [ ] Coordination, from the M7 review: L2, work that returns on its own just as its lease expires runs again because the election contends again (consider "returned while the token was not cancelled" as done); L4, an acquire that committed but then failed leaves an orphaned lock for one lease, because the retry uses a new owner id (reuse it across `RunAsLeaderAsync` retries); L13, logs 41 and 42 inside `RenewAsync` are written within a caller's request.
- [ ] Small checks: a bare name starting with `_` is a syntax error in the fake (as on DynamoDB Local) but accepted by the analyzer's `ExpressionLexer`; a non-nullable property of an enum without members throws on every write without a diagnostic, and `DYNA024` misses such a `[KeyOnly]` property.

Owner side:
- [ ] Credentials against real AWS, each with `DescribeTable` of a test table: `role_arn` with `source_profile`, `credential_source = Environment`, a chain of two roles, `duration_seconds = 900`, an SSO profile after `aws sso login`, the same after the access token expired (renewal through `CreateToken`, written back so `aws sts get-caller-identity --profile` still works), and `credential_process = aws configure export-credentials --profile X --format process`.
- [ ] IRSA on EKS in a real service.
- [ ] Repository allows squash merges only (rebase merges are still on).
- [ ] Move the AWS CLI off the root user of the account.
- [ ] Bless the list, map and set converters (`ReadOnlyListConverter` and the rest) for hand-written converters, or keep them infrastructure (one service calls them).
- [ ] A metric of rejected continuation tokens? Today the `FormatException` before the read is the only signal.
- [ ] Consumers on JFrog: the release lag of `registration-semver2` and its workaround are in the `releasing` skill; tell consumers in the release notes if wanted.

API backlog (build when a service asks):
- [ ] Filling a page after a client-side filter: `ContinuationToken.After(entity, index)`, since `FromKey` needs index key attributes an entity cannot easily give.
- [ ] A batch across tables: a client-level `BatchGet`/`BatchWrite` like `client.TransactWrite()`, since `BatchGetItem` and `BatchWriteItem` take several tables in one request.
- [ ] Create every declared table of an assembly from `IncludingAll()`: one `CreateTable` per declared table with its entities. Open: a generated method per table or one over all, and what an entity without a declared table does.
- [ ] `lease.MakeCurrent()`: a scope that makes a lease from `TryAcquireAsync` or `AcquireAsync` the `Lease.Current`; today such a lease is passed explicitly.
- [ ] C# union types: once released and parseable by the Roslyn of Stryker, let `Mixed<...>` and `MixedChange<...>` opt into the union pattern so a `switch` is exhaustive.
- [ ] `QueryIndex` (global indexes only, exactly one index of `T` with that partition key attribute, else `ArgumentException`), only on a second request from a service; today the index is kept in a field and named once.
- [ ] Accepting plain tokens on a signing client during a rollout (additive); switching signing on or off rejects the tokens in flight once.
- [ ] A retry on a lost race without a table: `RetryOnConflictAsync` exists only on `Table<T>`; a form for code that has no table at hand or whose writes span tables (on the client, for example).

Analyzers and docs:
- [x] Code fixes for the analyzers: the survey of the 103 rules is under [Code fixes](#code-fixes-survey-of-2026-11-08); `DYNA020` is done (`DeclareDiscriminatorCodeFix`). The fixes to build, in the order a newcomer meets the rules:
- [x] `DYNA001` (a): add `partial` to the type and to each containing type that lacks it (`MakePartialCodeFix`).
- [x] `DYNA239` (b): `.IgnoreVersion()` after the verb; `.IfVersion(version)` with a placeholder, since only the caller knows the version read. Done (`SettleVersionCodeFix`): the placeholder is the first of `version`, `expectedVersion`, `expectedVersion2`... that names nothing at the call, so the code fails with `CS0103` until the version read stands there; a local that happened to be called `version` would compile with a value nobody chose. `CS0103` marks it, which is the pointer to it; DYNA239 stays beside it until then, since an `IfVersion` that does not bind is no clause to the analyzer.
- [x] `DYNA234` (a): insert `.ItemsAsync()` (`.ValuesAsync()` after `Select`, the `{1}` of the message, to carry in a property) before the LINQ call. Done (`ReadTerminalCodeFix`, property `Terminal`). It writes what the message says, so a synchronous name (`ToList`) stays one that `IAsyncEnumerable` lacks.
- [x] `DYNA202` (b): one action per terminal the read has (`ItemsAsync`, `ItemAsync`, `PageAsync`, `CountAsync`...), from the read's own members. Done (`ReadTerminalCodeFix`): a member whose name ends in `Async`, offered where the chain binds with it (the arguments of `SendAsync(...)` kept, the calls after it included) and fits: a value under `await`, a stream in `foreach`. A fan-out's `PageAsync(pageSize)` needs a size, so it is not offered.
- [ ] `DYNA237` (b): `DateTimeKind.Utc` as the last argument (the seven-argument form for a date), or `new DateOnly(y, m, d)` where the time is midnight and the target takes one.
- [ ] `DYNA117` (a): make the key property `required` and drop its initializer.
- [x] `DYNA203` (b): `await <command>.SendAsync()` in an async member; `x = x.<call>` where the receiver is a variable; `_ = ` to drop it on purpose. Done (`DiscardedCommandCodeFix`): the send outside a `lock`, where `SendAsync()` binds, on a chain that does not start at a command (sent, a local would be used up for its next use); the assignment where the chain starts at a local of the result's type; the discard where `_` names nothing.
- [ ] `DYNA220` (a): put the reserved word in a hole, `{Attr("total")}`, with `WrapInAttrCodeFix`'s import.
- [x] `DYNA019` (a): `#nullable enable` at the top of the file (`EnableNullableCodeFix`).
- [x] `DYNA236` (b): `.IgnoreVersion()` on the batch (writing one by one with `Put` is a rewrite, not offered). Done (`SettleVersionCodeFix`), right after the write the diagnostic is on.
- [ ] `DYNA108` (b): `{X:plain}`, or `{X:d<w>}` with the width of the type's maximum.
- [x] `DYNA204` (a): assign the call's result back to the variable, `q = q.Limit(1);` (`SuggestedEditCodeFix`, where the earlier call is a statement of its own in a block the reuse is in).
- [x] `DYNA227` (b): `GetValueOrDefault()`, or `result.Found ? result.Value : default` (the test only where the result is a name).
- [x] `DYNA224` (a): replace the builder with the index's, `Order.Keys.ByStatusPk`, only where it has the same method with parameters of the same names and types.
- [x] `DYNA233` (a): `Add` becomes `Set`.
- [x] `DYNA235` (b): remove `IfVersion(n)`, or replace it with `IgnoreVersion()` (the second only with a `[Version]`, as the message says).
- [x] `DYNA208` (a): append the missing members to the `Project(...)`, as strings or handles like the call (none where one has no path: a dot in its name, or a key-restored member by handle).
- [x] `DYNA207` (b): one action per declared index, `Order.Indexes.X`, closest spelling first.
- [x] `DYNA213` (b): one action per attribute of the type within a small edit distance (one edit per four letters), in a string literal, and in the text where it can stand bare.
- [x] `DYNA206` (b): the version at 0, or `IgnoreVersion()` after `IfNotExists()`: the two ways the message names. Dropping `IfNotExists()` turns a first write into a checked one that fails for a missing item, so it is not offered.
- [ ] `DYNA107` (b): `Sparse = true` on the index, or make the property non-nullable and `required`.
- [ ] `DYNA104` (b): one action per format the message lists for the type.
- [ ] `DYNA032` (a): add `nameof(X)` to `[ComputedFrom]` (the name in a property).
- [ ] `DYNA114` (a): add `[ComputedFrom(nameof(...))]` with the stored properties the getter reads (`ComputedGetterAnalyzer` already walks it).
- [ ] `DYNA123` (a): add `[KeyOnly]` to the property.
- [ ] `DYNA110` (a): remove `Sparse = true`.
- [ ] `DYNA036` (a): on a `DateTimeOffset`, add `[DynamoDbConverter(typeof(EpochSecondsConverter))]`.
- [ ] `DYNA043` (a): write the name in full, the `{1}` of the message.
- [ ] `DYNA016` (a): give the type the accessibility of the entity, where the type is in source.
- [ ] `DYNA113` (a): `{X:d<w>}` with the digits of the largest member.
- [ ] `DYNA116` (b): drop the format (ticks); a tie-breaker is not offered.
- [ ] `DYNA005` (b): `[DynamoDbConstructor]` on each candidate constructor, one action each.
- [ ] `DYNA003` (b): `[DynamoDbIgnore]` (a converter needs a type).
- [ ] `DYNA102` (b): one action per property within a small edit distance.
- [ ] `DYNA035` (b): remove the initializer, or make the property non-nullable.
- [ ] `DYNA024` (b): make the property nullable.
- [ ] `DYNA042` (b): remove the modifier (by value), or make it `in`.
- [ ] `DYNA012` (b): remove the attribute, or make the type an entity.
- [ ] `DYNA013` (b): remove `[DynamoDbEntity]` or `[DynamoDbDocument]`.
- [ ] `DYNA021` (b): remove `[DynamoDbIgnore]` or the key attribute.
- [ ] `DYNA009` (b): remove one of the declarations, one action each.
- [ ] `DYNA030` (b): remove `[OmitEmpty]`, or `Sparse = true` where the index is the reason.
- [ ] `DYNA033` (b): remove `[DynamoDbEnum]`.
- [ ] `DYNA034` (b): make the setter `protected` where the base is in source, or `[DynamoDbIgnore]`.
- [ ] `DYNA029` (b): remove the second default, or drop the `?` of the type.
- [x] A docs section explaining the discriminator ([Discriminator](../docs/entities.md#discriminator)): what `_type` is, why `[Discriminator]` is required, opting out for tables other tools write, and what a typed read and a mixed read do with it.

Services:
- [ ] customer-adapter: migrate the tour lock to `DynaSharp.Coordination` (`LockTable`), deployed with the `Recreate` strategy so no old and new pod hold the lock by different rules at once.

## Done

Git history has the detail; each line says what an area delivered and where it lives.

- M0: repository, analyzers, central packages and lock files, coverage gate, Stryker, AOT smoke, BenchmarkDotNet, CI and release workflows.
- M1a/M1b wire layer: `DynamoDbReader`/`DynamoDbWriter`, `AttributeValue`, SigV4 (botocore vectors), pipeline with retries and clock skew, every DynamoDB and Streams operation as a request record (`src/DynaSharp/Requests`, `Streams`, `Http`, `Serialization`), error mapping, telemetry, `StubHandler`/`RecordingHandler`, the emulator integration suite.
- Credentials (`src/DynaSharp/Credentials`): environment (with `AWS_CREDENTIAL_EXPIRATION`), shared files, container, IMDSv2, web identity (IRSA), `credential_process`, assume role, SSO with token renewal; `StubSts` in `DynaSharp.Testing`.
- Authentication review (2026-11-08) of the signer, the providers, the refresh cache, the shared files and the signing path of the pipeline: no vulnerability. Fixed: the STS clock-skew correction before assuming a role (event 30), the plain-http warnings for STS and IAM Identity Center (events 25 and 31), the IMDS and STS settings of the profile and the IPv6 endpoint mode. Kept: the signing-key cache keyed by reference, since no provider of the library returns a new instance per call. What was checked is under Findings.
- M1c/M2a/M3 (`src/DynaSharp/Fluent`): `Item`, the command engine, `Table<T>`, reads, batches, transactions, typed keys and conditions, `[Version]`, mixed reads, fan-out, `Select`, `TrySendAsync`, `RetryOnConflictAsync`, `IsTransient`, and the follow-ups G1 to Q18.
- M2 generator (`src/DynaSharp.Generators`): entities, keys and handles, converters, key grammar with vectors, diagnostics `DYNA0xx`/`DYNA1xx`, analyzers `DYNA2xx` with code fixes (`DYNA204`, `DYNA206` to `DYNA208`, `DYNA210`, `DYNA213`, `DYNA224`, `DYNA227`, `DYNA233`, `DYNA235`, `DYNA240`, and `DYNA020` with the three ways of its message).
- M2 generator (`src/DynaSharp.Generators`): entities, keys and handles, converters, key grammar with vectors, diagnostics `DYNA0xx`/`DYNA1xx`, analyzers `DYNA2xx` with code fixes (`DYNA210`, `DYNA240`, `DYNA020` with the three ways of its message, `DYNA001`).
- M4 `FakeDynamoDb` (`src/DynaSharp.Testing/Fake`, rules in `architecture.md` section 14): data and control plane, expressions, streams with shard lineage, time to live, `FailNext`; vectors of DynamoDB Local, the Parity Suite runner (`tools/DynaSharp.FakeServer`), differential and fidelity tests (R20).
- Streams readers (`src/DynaSharp/Streams`): `ReadShardAsync`, `ReadStreamAsync` (whole stream, transient failures read through), typed `ReadChangesAsync` and `As<T>()`.
- R1 to R19: signed tokens, `OnMappingFailure`, `DYNA230`-`DYNA234`, sparse-key copy, table drift checks, `ForEmulator`, `[TimeToLive]`, `IncludingAll()`, JSON bridge, declared tables and `TableNaming`, generated keys only on typed tables, R15 without compatibility shapes, property-based tests (`tests/DynaSharp.UnitTests/Properties`).
- M7 `DynaSharp.Coordination`: `LockTable`, `Lease` with fencing tokens and `IsHeld`, `RunAsLeaderAsync`, `Lease.Current`, `DynaSharp.Coordination.Testing`. Recipes for queue and hi/lo.
- Coordination: an acquire whose write meets a transaction on the lock item (`TransactionConflictException`, which the client does not retry and which escaped `TryAcquireAsync`/`AcquireAsync`) counts as refused: a consistent read, the usual wait, the sighting kept, and the response-lost retry that meets one still finds its own owner (2026-11-08). A renewal or release that meets one is sent again through `RetryOnConflictAsync(TransactionConflict)`: before, a renewal was retried by the heartbeat with log 42 and thrown from `RenewAsync` (never lost), and a release was logged (43) and left the lock taken until the lease ran out.
- Releases 0.1.0 (2026-11-03) to 0.8.0 with trusted publishing; `build/ship-public-api.sh` checked by `release.yml`.
- Docs site (DocFX): guide, diagnostics page, API reference; the docs pass after 0.8.0.
- Stryker survivors of `fluent` and `core` on `e78cb67`: verdicts below, what is left under Open.

## Decisions

Where `requirements.md`, `api.md` or `architecture.md` section 13 ("Decisions you must not undo") states a decision in full, the row only points there.

| Date | Decision and why |
|---|---|
| 2026-10-31 | Target `net11.0` only, moving to GA when it ships: a wide audience is not a goal, and `net10.0` would need a logging package. |
| 2026-10-31 | No CRC32 response validation. No gzip (if added, handle error bodies that claim gzip but are not compressed). Contiguous pooled buffers, not segmented (one-shot hashing). |
| 2026-10-31 | `architecture.md` section 13 holds: handles, not expression trees; the handler is the test seam, no interface on the client; one serialization path; commands are ref structs; key values rejected, never escaped, `_type` required; batch retries bounded by rounds without progress; `TransactWrite` verbs are extensions (about 60 members otherwise); no seam to the key emitter; a `char` is a string value, no `[Obsolete]` (thomhurst/TUnit#7316); a local snapshot comparer, not Verify; `HttpClient` stays, body read with the synchronous `ReadAsStream`; pooled buffers returned uncleared; a skew resend counts against `MaxAttempts`. |
| 2026-10-31, 2026-11-04 | Gates: 100 % line and branch coverage on every change; Stryker breaks below 90 % per shard, runs by `workflow_dispatch` in cycles (below v1 not before each release), never in a pull request's path. |
| 2026-10-31 | Build-time and benchmark-only dependencies are fine (EfficientDynamoDb and the AWS SDK in benchmarks, `PublicApiAnalyzers` approved 2026-11-01); runtime ones are not. |
| 2026-10-31 | Commits are authored as Marc Sallin `<7792779+msallin@users.noreply.github.com>`; the dev machine's global git identity is a placeholder, so pass `-c user.name` and `-c user.email`. |
| 2026-10-31 | Retries: AWS standard mode, `MaxAttempts` 4 (as aws-sdk-java-v2; 3 gave up on a throttle after 150 ms), the only public retry setting. Classification is botocore's list reduced to what DynamoDB's JSON protocol sends (`FailureClassifier`), the quota refunds as botocore's, skew is recomputed from the raw clock on each skew response. Throttled control-plane calls keep the same budget (2026-11-03). |
| 2026-10-31 | `ErrorCode` is the wire code without namespace (`InternalServerError`, unsuffixed, as DynamoDB, botocore and the AWS SDK send it); class names keep `Exception`. All exceptions live in `DynaSharp` (one using); `Streams` depends on `Requests`, never the reverse. |
| 2026-10-31 | Wire model: botocore member names; legacy parameters left out, so `KeyConditionExpression` is `required`. `LastEvaluatedKey` is null at the end, also for an empty map; a non-null `ExclusiveStartKey` is always written, so DynamoDB rejects it instead of restarting. Response collections are never null. `CancellationReason.Code` is a string (the service adds codes). Enums: `NONE` is `None`, sendable optional enums have `Unspecified` = 0, response-only ones `Unknown` = 0. Generic names avoided (`ChangeRecord`, `StreamSummary`, `TransactPut`, ...). |
| 2026-10-31 | `AttributeValue.As*` return internal storage as read-only interfaces (no wrapper; callers must not cast and mutate). An `Item` inside an `AttributeValue` is read-only; getters give null for missing and NULL; number errors name the attribute, never the text. |
| 2026-10-31 | `DynamoDbReader` and `DynamoDbWriter` have public constructors, so users can test converters. |
| 2026-10-31, 2026-11-04 | `DynaSharp` grants `InternalsVisibleTo` only to `DynaSharp.Testing` (and tests), which therefore depends on exactly `[X]`; `DynaSharp.Coordination` uses public API only (`>= X`), hence the public `Table<T>.Client`. |
| 2026-10-31 | Telemetry: source and meter `DynaSharp` (Coordination too, so one subscription covers it); semconv v1.44 plus the deprecated `rpc.*` the AWS conventions prescribe; `aws.dynamodb.table_names` on transactions; Streams spans `DynamoDB Streams.<Op>`. No item collection metrics (they hold the partition key), no stream ARN (names account and table). `User-Agent: DynaSharp` without a version (needs reflection). The lock name is on spans and logs, not metrics: an accepted exception to the no-keys rule. |
| 2026-10-31 | `ServiceUrl` applies to both clients (an emulator serves both APIs on one address). `RecordingHandler` disposes its inner handler; the client does not dispose a handler it is given. One default credentials provider per client (docs: share one on EC2 and ECS). Credential providers never use `HttpHandler`, which is DynamoDB's seam. |
| 2026-10-31, 2026-11-03 | Credentials follow botocore 1.42 (chain and profile order), except: only `AWS_PROFILE` selects a profile; file keys never mix across files; `login_session` and `mfa_serial` fail (no MFA prompt: every refresh would need the code); only the default chain logs; the process provider is internal. Advisory refresh runs in the background and pauses 30 s after a failure and after a success (botocore makes a caller wait, and storms in the advisory period). `credential_process` gets a closed stdin, its stderr is dropped (may hold a secret), and it is killed after 20 s (below the attempt timeout) or 64 KB. No region from IMDS: resolving is synchronous, and EKS and Fargate inject `AWS_REGION`. |
| 2026-11-03 | An http endpoint is not rejected (DynamoDB Local in docker-compose); off loopback the client warns once (event 9), web identity once (event 25). A custom `HttpHandler` reads the request content only before its `SendAsync` returns, and sends a request once. |
| 2026-10-31 | Optimistic locking is opt-in per entity with `[Version]` and enforced on every write path, so blind upserts stay possible. Version 0 renders `attribute_not_exists(<partition key>)`, so a first write fails on an item without a version; `IgnoreVersion()` takes one over. A batch cannot carry a condition, so a versioned batch write needs `IgnoreVersion()`. |
| 2026-11-03 | Keys (`requirements.md`, Key grammar): a bare timestamp placeholder is `ticks`, the only lossless format, so an input can be restored from its key and a key built from the read attribute finds its item (4 bytes more); `{At:ms}` stays for keys the AWS SDK wrote. Default names `pk`, `sk`, `<index>pk`, `_type` are frozen. `Of` exists on partition key builders only (a `PartitionKey` from a sort key is the mistake itself). `Between` on a multi-field key takes `(leading..., from, to)`. No date key type and no id type: `DateOnly` and `Guid.FirstVersion7`/`LastVersion7` cover them. |
| 2026-11-01 | Typed updates never write key inputs by default (`StoreKeyInputs()` opts in). An update never reads inside and never writes a key from some of its inputs: a hidden read doubles cost and races, a half key is the stale index the check prevents; the message names the `Set` to add. The other key of a sparse index is copied with `SET #k = #a` where the texts are equal, without `attribute_exists` (DynamoDB's `ValidationException` is louder; a failed condition would be retried by `RetryOnConflictAsync`). |
| 2026-11-01, 2026-11-03 | A continuation token is client input: `Parse` takes exactly one JSON object; `Parse("")` is the default token, so a loop on `Next.ToString()` checks `HasMore`. Signing is opt-in (`DynamoDbClientOptions.TokenSigner`, per client, so every instance checks the others' tokens): HMAC-SHA256 with rotation, bound to table, index, resolved key condition and fan-out partitions, not to filter, projection, `Limit`, direction or time. Every rejection is a `FormatException` (one catch for bad input); a token from `FromKey` passes unsigned. |
| 2026-11-01 | `BatchIncompleteException`/`BatchGetIncompleteException<T>` (code `BatchIncomplete`) carry the progress of a batch that gave up. Batch `ConsumedCapacity` is one entry per table and request. A transaction past 100 actions throws at the call; no splitting, which would give up atomicity. |
| 2026-11-01 | No compile-time JSON templates: 0 B gain, 0.08 % of a round trip. |
| 2026-11-01 | No base type for client-side failures: `TimeoutException` and `HttpRequestException` stay as they are (wrapping breaks every `catch (TimeoutException)`). `IsTransient` is non-virtual and computed, the rule of the retry loop; a transaction conflict is transient, a failed condition, `ResourceNotFound`/`ResourceInUse` and `ExpiredToken` are not. A table whose key differs from the command is `InvalidOperationException` (configuration error), not a new type; other drift goes to `CreateTableResult.Drift` and log event 14 and is never changed. |
| 2026-11-03 | Analyzer severity: a new warning fails consumers with warnings as errors, so warnings went in before 0.1.0 and a later rule starts at Info. `DYNA202` stays a warning (it can report code that compiles); `DYNA212`/`DYNA214` are warnings for 0.x; `DYNA115` and `DYNA123` are Info; `DYNA230`-`DYNA234` are errors (they report only calls that do not compile); `DYNA038` is an error (2026-11-06). Never-released ids `DYNA027`, `DYNA028`, `DYNA228`, `DYNA229` stay unused. |
| 2026-11-03 | Generator diagnostics that leave the model whole (`DYNA020`, `DYNA104`, `DYNA105`) do not mark the type broken, so one error shows instead of `CS0311` on every use; a suggested discriminator keeps the whole name. A rule that needs the model lives in the generator (`DYNA033` on a property, `DYNA035`), one about a symbol in an analyzer. |
| 2026-11-03 | The README makes the speed claim with times beside allocations; a new benchmark run updates both. |
| 2026-11-03, 2026-11-04 | `FakeDynamoDb` is the local default of `DYNASHARP_BACKENDS` (no Docker); CI runs `dynoxide,dynamodb-local,fake` in a job still named `Integration tests (Dynoxide, DynamoDB Local)`, which branch protection may require. The fake is for functional tests, fine print stays with the emulators, the Parity Suite measures it. It supports `SSESpecification` and `OnDemandThroughput` in `CreateTable` and `UpdateTable` (the client always writes `Enabled`, since `false` switches back to the AWS-owned key), refuses `AttributesToGet` and vector indexes, and has the gaps `PartiQl`, `Tags`, `BatchResponseLimit`. Rules: `architecture.md` section 14. |
| 2026-11-04 | Streams: no consumer, no leasing, no checkpoints, no sharing of shards across processes. Reading a shard or a stream in-process is API: two apps hand-wrote the same shard walk, and the sample got failure reporting wrong. |
| 2026-11-04 | `DynaSharp.Coordination` is the lease with fencing tokens and leader election. The sparse-GSI queue and hi/lo are recipes (`Recipes/SparseIndexQueueTests.cs`, `Recipes/HiLoAllocatorTests.cs`): a queue cannot be frozen (it grows into a small SQS), hi/lo is one `ADD` and a documented gap. Out of scope: a hosting package, configuration sections, custom attribute names, enforcing fencing in other systems, a migration guide from home-grown leases. |
| 2026-11-04 | Coordination shape: `TryAcquireAsync` is one instant attempt (a short-lived process uses `AcquireAsync` with a wait over one lease). Followers poll with a consistent read and write only for a free lock or a due takeover (a failing write per poll set off alarms); without a sighting the acquire writes at once, so an uncontended lock costs one request. A release keeps the item, so the token never goes back; item expiry is opt-in, with the documented gap that a deleted item restarts the token at 1. Each acquire writes a new owner id. `RunAsLeaderAsync` lives on `LockTable` (a static `LeaderElection` collided with an app namespace); `LockTable.For<T>` because a constructor cannot be generic and `Table<T>` has no non-generic base by design. Test helpers are in `DynaSharp.Coordination.Testing`, because `DynaSharp.Testing` must not reference Coordination. `Lease.Current` is an `AsyncLocal` set only by the election. |
| 2026-11-04 | Releases are on demand: `release.yml` by `workflow_dispatch` on `main` after a green CI run of a push, so packages land and ship together. Version from MinVer and the tag; GitHub's generated notes, no `PackageReleaseNotes`. |
| 2026-11-04 | `CreateTableCommand.ToRequestJson()` gives the body without ending the command; a parallel scan has none. A transaction's `ToRequestJson()` is a member of the command and its steps, because an extension over a type parameter binds on every type (R15). |
| 2026-11-04 | `FakeDynamoDb.Items<T>` of a discriminated entity skips other entities and throws for an item of `T` it cannot read; without a discriminator it reads every item. `Get<T>` throws for another entity, as a client's `Get` does. |
| 2026-11-05 | No `ListAsync`: `ItemsAsync` is the one streaming terminal and LINQ's `ToListAsync()` the list; the token goes to `ItemsAsync` once (CA2016 is answered by a sentence in `queries.md`, not a suppressor). |
| 2026-11-06 | Below 1.0, breaking changes are fine in every release and preferred over technical debt (`requirements.md`, `AGENTS.md`). R15 applied it: one `ForEmulator(serviceUrl, region = "us-east-1")`, one `TryOf` returning a nullable key, `WriteResult<T>.Version` alone, two forms per delegate shape of `RetryOnConflictAsync` (`(op, ct)` exists because a token after optional parameters cannot be passed by position). Kept on their own merits: the names `ReadStreamAsync`/`ReadChangesAsync` (records vs typed changes), `MixedChange` beside `Mixed` (a change is no document; one union would make `Table<Change<T>>` compile), the mixed readings as members of the partial `Table<T>` (an extension block needs every type argument), the transaction verbs as extensions. |
| 2026-11-06 | A typed table or index takes only generated keys; raw keys are extensions on `Table<Item>`, because `Get("1")` on a templated entity sent the wrong key and read null silently. `DYNA230` names the builder. |
| 2026-11-06, 2026-11-07 | An entity declares its table (`[DynamoDbEntity(Table = ...)]`, assembly default); `client.Table<T>(name)` always wins and is used exactly as given. `TableNaming` (`Func<string, string?>`) maps declared names only; null or empty keeps the declared name; `ResolveTableName` applies it for code without an entity. Stubs echo a request's name and keep taking it. |
| 2026-11-07 | `IncludingAll()` is generated per assembly into an `internal` `DynamoDbEntities` in namespace `DynaSharp` (top-level statements did not see the assembly's namespace); two friend assemblies with entities give `CS0121` on purpose rather than silently dropping one's entities. Entities with two pairs of key names or nested in a private type get none (`DYNA037`). The READMEs show explicit `Including<T>()`, which the owner prefers, and `DYNA240` names only that. |
| 2026-11-07 | `DYNA240` checks a `CreateTable()` chain as written up to the call that leaves the command, or the expression a member returns; a command in a variable, field, argument or behind a conversion, a table opened by name and an entity the file cannot name are not checked, because the code in sight could still be right. |
| 2026-11-07 | Reads: under `Take(n)` each request's `Limit` is what is still to give, so `LastEvaluatedKey` is the exact position after the last item given; `Take(n).PageAsync()` is one page, `HasMore` stays DynamoDB's, `Take(n + 1)` tells a cut; `Take` counts items given, `Limit` items read; `Parallel` after `Take` throws. `ItemAsync` adds no `Limit` (it works before the filter). `PageAsync(ContinuationToken? after = null)`, `Next` stays a plain token beside `HasMore`. `OnMappingFailure` runs after the call of the page, not in the parse (the pipeline would turn its exception into an unrecognized response); `Page.Count` stays DynamoDB's count. |
| 2026-11-07 | Writes: `TrySendAsync` reads a failed condition through the internal `TryReadFailure` seam before `MapError`, with no `try`/`catch` (no exception, no error span, no second parse). `Put(...).ReturnNew()` reads the stored entity back from the request body (DynamoDB has no `ALL_NEW` for a put): no reflection, no generated copy, no capacity. `onConflict` is a synchronous `Action<int, DynamoDbException>` before the token (breaks a positional token; the guide names it). Batch deletes by entity are extensions (the batch's `T` cannot require `IDynamoDbEntity<T>`). `ConflictKind` is a `[Flags]` overload; no `NotContains` (DynamoDB has none: `Negate()`), no typed `Size`. |
| 2026-11-07 | Fake facade: one `Requests` list and queue per fake for all its clients (a test asks what the code sent, not through which client; a queued response may go to any client, so a background reader is failed with `FailNext`). No `FakeDynamoDb.CreateLockTable`: `DynaSharp.Testing` does not reference Coordination; `fake.CreateTable(locks.CreateTable())` does it. `PutItem<T>(Item)`/`GetItem<T>(key)`, not `Put<T>(Item)`, which beside `Put<T>(entity)` reads as putting a `T` and makes `Put<T>(null)` ambiguous. `TableItems<T>()` (raw, whole table) is named apart from `Items<T>()` (entities). `FailNext` lives in the fake, so it holds for every client. Readers of `CreateStreamsClient` wake at the next stream change (internal `NextStreamChange`), not through a long poll in `GetRecords`, which would change what a reader sees. |
| 2026-11-07, 2026-11-08 | Tests: `ConfigureAwait(false)` is behaviour wherever the await can run on the caller's context, killed with `CountingSynchronizationContext` and `HeldResponses`; equivalent only on the pool. An exception message is behaviour: tests assert the message of each argument check and refusal. Property tests (TUnit.FsCheck) and differential tests run from a fixed seed, so CI fails only for a change of the code. The differential harness compares the raw wire, not `Table<Item>`, and messages only of `ConditionalCheckFailedException` and `TransactionCanceledException`. A generator result shared between tests is rebuilt under Stryker (`SharedResult<T>`). Unit tests run with `--project`, not `--solution`. |
| 2026-11-07 | DynamoDB's limits live in a section of `docs/errors.md`: hitting one is an exception, and that page is where a reader arrives. |
| 2026-11-08 | A generator diagnostic hands a code fix what it computed through `DiagnosticInfo.Fix`, which becomes the property `Fix` (the suggested discriminator of `DYNA020`), so fix and message cannot disagree. The `DYNA020` fix adds no `using`: `[DynamoDbEntity]` binds where the type is, so where `Discriminator` does not, the file names DynaSharp's attributes in full or another type takes the name, and the fix writes the qualifier of `[DynamoDbEntity]` or `global::DynaSharp.Mapping.`. The opt-out of the project goes on the `[assembly: DynamoDbDefaults]` the project has, else beside the other attributes of the assembly in source (where project-wide settings are looked for; a file with an `<auto-generated>` header, such as the SDK's `AssemblyInfo.cs` in `obj/`, does not count), else after the usings of the entity's file; its fix-all edits each project once, so no second attribute (`CS0579`). Nothing is offered where the edit would be lost: a file named `.g.cs`, `.designer.cs`, `.generated.cs` or with an `<auto-generated>` header. New syntax is parsed, not built with `SyntaxFactory`, whose elastic trivia makes the IDE respace neighbours. |
| 2026-11-08 | The fixes of analyzer rules whose edit is plain text come from the analyzer: it puts each edit on the diagnostic, its span as an additional location and its title and text as the properties `Title<n>` and `Fix<n>` (`SuggestedEdit`), and one `SuggestedEditCodeFix` writes them (`DYNA204`, `DYNA206`, `DYNA207`, `DYNA208`, `DYNA213`, `DYNA224`, `DYNA227`, `DYNA233`, `DYNA235`). The analyzer has the model the edit needs (the declared indexes, the attributes, the builders, the members left out), so fix and message cannot disagree, a text edit keeps the layout and line endings of the file, and the fix needs no semantic model in the IDE. The work runs only when a diagnostic is reported. The equivalence key is the rule and the rank of the edit, so a fix-all takes the first (closest) edit of each. |
| 2026-11-08 | Code fix of `DYNA001`. `DYNA001`: `partial` goes right before the keyword, the one place C# allows it, on each declaration around the type that lacks it; a type that is not partial has one declaration, so nothing else needs it. The fixes share `GeneratedFiles` in `src`, and `CodeFixRun` in the tests. |

## Findings and gotchas

Tools:
- Stryker 5 with the MTP runner (preview for TUnit) flips mutants in static initializers and near timeouts between Killed, Timeout and Survived; hand-check before acting. A local run of a few files still pays the per-test coverage capture of the whole suite (about 19,300 tests), longer than 30 minutes. After a run `bin/` may hold a mutated assembly: `dotnet test` rebuilds, direct DLL runs do not.
- A coverage run instruments its `bin` in place: a test run from the same `bin` meanwhile fails with `BadImageFormatException`.
- `--no-build` after a failed build runs the old binaries: check the build's exit code. TUnit takes one `--treenode-filter`. Semaphore-gated tests hang instead of failing: run with `--timeout`.
- BenchmarkDotNet builds its child project from the start directory, not `--project`: start it in the worktree. A `sealed` benchmark class is skipped while the run exits 0: check `executed benchmarks: N`. 0.15 does not know `net11.0`; 0.16.0-preview is pinned.
- SDK rc.3: `dotnet publish -o` fails copying the XML docs of an executable (`PublishDocumentationFile=false` in the AOT smoke). File-based apps default to Native AOT (`build/CoverageGate.cs` sets `PublishAot=false`). AOT on Windows needs the C++ workload; otherwise run the `linux-x64` gate in a container. Libraries list the same `RuntimeIdentifiers`, so the AOT publish leaves their lock files alone.
- Visual Studio without the pinned SDK empties the lock files of an open checkout (`"net11.0": {}`).
- Some tools turn `\uXXXX` escapes into characters, and `dotnet format whitespace` sees line endings: write escaped JSON programmatically and check files after writing. PowerShell `[IO.File]` resolves relative paths against the process start directory.
- JFrog: after a release `registration-semver2` of a virtual feed lists the old version for over an hour; seed the global NuGet cache with nuget.org's own nupkgs (not the GitHub release files, which nuget.org re-signs).
- Coverage-gate traps: method-group conversions at several call sites create `??=` cache branches (use one static delegate); a string `switch` of 7+ cases lowers to unreachable branches; a method on a generic `T` with a property call as argument gets a hidden branch (pass a local); an async iterator with `while (cond)` around a `yield` leaves a branch (`while (true)` with `yield break`). Timing-dependent branches flake: hold the path open in the test instead of racing it.
- TUnit registers a global `ActivityListener`, so `StartActivity` never returns null in tests (inject an `ActivitySource` for the null path). Telemetry tests isolate by trace id, table or `server.address`; a counter without a table tag allows one test per operation.
- Tests never depend on `ArrayPool<byte>.Shared`: inject a counting pool and assert every rented array returns once.
- `Utf8JsonReader`'s default `MaxDepth` 64 is too low for 32 DynamoDB levels; the reader uses 128. `CancellationTokenSource.TryReset` works only for `TimeProvider.System` timers. `Task.Delay(TimeSpan, TimeProvider, ct)` truncates to milliseconds. `cond ? x : null` on `ReadOnlyMemory<byte>?` gives empty, not null. A nested pool rent misses the thread cache (30 % CPU on Query), so item scratch lives on the stack; plain FNV collapses on names like `attribute07` (MurmurHash3 finalizer).
- `SetActivityTags` must never throw (the span starts before the `try`). Internal converter classes trigger CA1812 in consumers: doc examples use `public sealed class`.
- `ClientConfiguration.Resolve` without a region and a reader reads the machine's shared files: tests set a region or pass a reader.
- Docker: the Dynoxide tag on `ghcr` is `1.5.0` without `v`; Testcontainers also pulls `testcontainers/ryuk` from Docker Hub.

Behaviour by design (documented):
- After a lost response, a retried conditional or versioned write fails its own condition although the first attempt applied, and `RetryOnConflictAsync` then applies the change again; `.MaxAttempts(1)` on the write avoids it. The library cannot tell a duplicate delivery from its own retry; compare the item of `ReturnOldOnFailure()`.
- `IfVersion(n)` checks `long`, so an `int` version past its maximum stores a value that later fails to read; `Delete(entity)` with a `ulong` version above `long.MaxValue` throws before `IgnoreVersion()`.
- A custom converter on a plain key that writes binary, or a number without `IAddableConverter`, is created as a string key, and the first put fails (`ToRequest()` is the way out). `IncludingAll()` cannot tell two tables whose keys have the same names.
- An application-made credentials provider judges expiry on the local clock and recovers through one rejected request; `ExpiredToken` drops the credentials and resends once (codes from `credsExpiredCodes` of the AWS SDK for Go v1; botocore has none for DynamoDB). IPv6 IMDS and `AWS_SECURITY_TOKEN` are not supported. A `credential_process` whose credentials expire within 10 minutes runs on every call, as in botocore.

Authentication review (2026-11-08), checked and found correct:
- SigV4: the canonical request with and without the session token, the form content type with charset, host with port, IDN hosts in punycode, the key derivation, the scope of the global STS endpoint.
- Refresh cache: single flight, advisory and mandatory windows, the 30 s throttle, already-expired credentials refused, drop by reference, a failure handed to every waiter. Default chain: botocore's order, "does not apply" versus "broken". Profile chain: loops, self-reference with static keys only, credentials file over config file, MFA and `login_session` refused.
- Container endpoint: http only on the allowed hosts, no user info, no redirects, no line breaks in the token. IMDSv2 with the v1 fallback and its switch. `credential_process`: no shell, bounded output, timeout with a tree kill, stderr withheld. STS XML: no DTD, no resolver, 64 KB cap. SSO cache written atomically with its mode kept.
- Continuation tokens: HMAC-SHA256, constant-time compare, length-prefixed binding, 32-byte key minimum. No log, exception or `ToString` carries a secret. The pipeline resends on dropped credentials once per call and never retries a credential failure.

botocore and reference facts:
- botocore keeps inline `#` comments in values, inherits `[DEFAULT]`, matches `[profile x]` by prefix and splits it like a shell, and fails the whole file on a bad nested block. Chain: env, assume role, web identity, SSO, credentials file, login, process, config file, container, IMDS.
- Deviations: container and IMDS treat an empty variable as unset, trim a token file, reject `FULL_URI` with user info and never echo URIs; the SSO portal and OIDC get 3 attempts (botocore: 5); a missing `AWS_PROFILE` profile stops the chain; the environment is a source of its own ahead of the files; `duration_seconds` outside 900..43200 fails; session names default to `dynasharp-<unix time>`.
- botocore has no clock-skew correction (the oracle is the AWS SDKs), does not know `ConsumedCapacity.VectorIndexes`, validates `Limit >= 1` client-side, and parses only `Message` of `TransactionCanceledException`. The API reference's `BatchGetItem` response sample is invalid JSON, its `DescribeStream` sample shows a date string where the service sends epoch seconds.
- The AWS SDK (`AWSSDK.DynamoDBv2` 4.0.109, conversions V1 and V2) writes a `DateTime` as `ToUniversalTime()` in `yyyy-MM-ddTHH:mm:ss.fffZ` (`Unspecified` taken as local) and `DynamoDBContext` reads `Utc`; `SdkDateTimeConverter` does the same.

Real DynamoDB, from the first runs of `real-aws.yml`:
- A global index lags its table with no documented bound (1.6 to 1.9 s seen): `SettlingHandler` holds an index read 5 s after the last write, a table read 1.5 s. Recipes that read an index right after a write hold on emulators only.
- A scan rounds each partition up to 4 KB, so a new on-demand table of several partitions reports more units than a query of the same items.
- Batches leave keys and items unprocessed when a partition runs out of throughput, not only at 16 MB.
- An index added by `UpdateTable` takes 8.5 to 15 minutes even on a tiny table, and `DeleteTable` is refused (`ResourceInUseException`) meanwhile (`Tables.IndexBuildTime` 30 minutes). A second time-to-live change within an hour is refused; a deleted table stays `DELETING` with its name taken a while; a stream may have several shards (gaps `TimeToLiveChangesAgainAtOnce`, `TableGoneAtOnce`, `OneShardPerStream`).
- Two `UpdateTable` calls within a second throttled on all four attempts. An exception from `DisposeAsync` under `await using` replaces the test's own: `Sandbox` reports cleanup failures through `CleanupFailures` and its hook.
- Empty strings are accepted as members of `SS` and `BS`. TTL deletion lags "a few days"; one item takes about 1000 WCU/s; a write is billed on the larger of its before and after images.

## Code fixes (survey of 2026-11-08)

Each rule is (a) one obviously right edit, (b) a choice the message names, one code action per option, or (c) no fix: the edit needs a decision only the author can make, or the diagnostic has no place in source. Done: the rules marked (done) in the table; the rest are under Open in the order to build them. Count: 19 (a), 29 (b), 55 (c).

Generator rules (`DYNA0xx` and `DYNA1xx`, but `DYNA026`, `DYNA032`, `DYNA033` and `DYNA120` to `DYNA122`, which analyzers report) reach a code fix as an analyzer's do: `DiagnosticReporter` places them on a `Spot` in the user's tree (the type's identifier, the attribute or its argument), and `DiagnosticInfo.Fix` carries the one string a fix needs as the property `Fix`. Their fixes are tested from a generator run over a workspace's compilation, whose trees are the documents' (`DeclareDiscriminatorCodeFixTests`). No fix can stand where the place is `Location.None`: `DYNA037` (on no file), and any rule on a base type in another project of an IDE workspace.

| Class | Rules | The edit |
|---|---|---|
| a | `DYNA001` | `partial` on the type and its containing types (done) |
| a | `DYNA016` | the entity's accessibility on the type |
| a | `DYNA019` | `#nullable enable` at the top of the file (done) |
| a | `DYNA032` | `nameof(X)` into `[ComputedFrom]` |
| a | `DYNA036` | `[DynamoDbConverter(typeof(EpochSecondsConverter))]` on a `DateTimeOffset` |
| a | `DYNA043` | the name in full, from the message |
| a | `DYNA110` | remove `Sparse = true` |
| a | `DYNA113` | `:d<w>` with the digits of the largest member |
| a | `DYNA114` | `[ComputedFrom]` with what the getter reads |
| a | `DYNA117` | `required`, initializer removed |
| a | `DYNA123` | `[KeyOnly]` |
| a | `DYNA204` | the result assigned back to the variable (done) |
| a | `DYNA208` | the missing names into `Project(...)` (done) |
| a | `DYNA210` | `Attr(...)` around the string (done) |
| a | `DYNA220` | the word in a hole, `{Attr("...")}` |
| a | `DYNA224` | the index's key builder (done) |
| a | `DYNA233` | `Add` to `Set` (done) |
| a | `DYNA234` | `.ItemsAsync()` (or `.ValuesAsync()`) before the LINQ call (done) |
| a | `DYNA240` | `Including<T>()` for each entity left out (done) |
| b | `DYNA003` | `[DynamoDbIgnore]` (a converter needs a type) |
| b | `DYNA005` | `[DynamoDbConstructor]` on one constructor, one action each |
| b | `DYNA009` | remove one declaration, one action each |
| b | `DYNA012` | remove the attribute, or make the type an entity |
| b | `DYNA013` | remove one of the two attributes |
| b | `DYNA020` | the suggested value, `OptOut = true`, or the project's opt-out (done) |
| b | `DYNA021` | remove `[DynamoDbIgnore]` or the key |
| b | `DYNA024` | the property nullable |
| b | `DYNA029` | remove the second default, or the `?` |
| b | `DYNA030` | remove `[OmitEmpty]`, or `Sparse = true` |
| b | `DYNA033` | remove `[DynamoDbEnum]` |
| b | `DYNA034` | a `protected` setter, or `[DynamoDbIgnore]` |
| b | `DYNA035` | remove the initializer, or the property non-nullable |
| b | `DYNA042` | by value, or `in` |
| b | `DYNA102` | the properties of a close spelling |
| b | `DYNA104` | each format the message lists |
| b | `DYNA107` | `Sparse = true`, or the property `required` and non-nullable |
| b | `DYNA108` | `:plain`, or `:d<w>` |
| b | `DYNA116` | drop the format |
| b | `DYNA202` | each terminal the read has (done) |
| b | `DYNA203` | send it, assign it back, or `_ =` (done) |
| b | `DYNA206` | the version at 0, or `IgnoreVersion()` (done) |
| b | `DYNA207` | each declared index, `T.Indexes.X` (done) |
| b | `DYNA213` | the attributes of a close spelling (done) |
| b | `DYNA227` | `GetValueOrDefault()`, or a test of `Found` (done) |
| b | `DYNA235` | remove `IfVersion`, or `IgnoreVersion()` (done) |
| b | `DYNA236` | `IgnoreVersion()` on the batch (done) |
| b | `DYNA237` | `DateTimeKind.Utc`, or a `DateOnly` |
| b | `DYNA239` | `IgnoreVersion()`, or `IfVersion(version)` with a placeholder (done) |
| c: a name or a value to invent | `DYNA004`, `DYNA010`, `DYNA014`, `DYNA022`, `DYNA025`, `DYNA026`, `DYNA038`, `DYNA039`, `DYNA100`, `DYNA101`, `DYNA105`, `DYNA122`, `DYNA211`, `DYNA212`, `DYNA214`, `DYNA238` | a name, a value, a table name or a fix of the grammar |
| c: a design decision | `DYNA002`, `DYNA006`, `DYNA007`, `DYNA008`, `DYNA011`, `DYNA017`, `DYNA018`, `DYNA023`, `DYNA031`, `DYNA040`, `DYNA041`, `DYNA103`, `DYNA106`, `DYNA109`, `DYNA111`, `DYNA112`, `DYNA118`, `DYNA119`, `DYNA120`, `DYNA121`, `DYNA205`, `DYNA209`, `DYNA215`, `DYNA216`, `DYNA217`, `DYNA218`, `DYNA219`, `DYNA221`, `DYNA223`, `DYNA225`, `DYNA226`, `DYNA230`, `DYNA231`, `DYNA232` | the key schema, the model, the command or the read changes shape |
| c: nothing to change, or no place | `DYNA015` (a project setting), `DYNA037` (no file), `DYNA115`, `DYNA201`, `DYNA222` | information, the project file, or a rewrite of the expression |

## Stryker verdicts (run on `e78cb67`)

The record the next mutation run needs: what was judged equivalent, and why. Kills were checked by applying each mutant by hand and running the covering test classes.

`fluent` shard (163 survivors: 121 killed, 4 simplified, 38 equivalent):
- Simplified: `ParallelScan` no longer checks the token after the channel ends (`ReadAllAsync(token)` throws); `PageResponse` drops `skippedCount`; `BatchWriteOperation.AddDelete` by values drops `NoteBatchWrite` (only a table without a schema reaches it).
- Equivalent: the boundaries of the wait (`left < ShortestDelay`, `delay < left`, `delay * 2 < LongestDelay`; equal values give the same delay); the first `Step` of `Progress` (set before read); the early return of `PagedRead.TakeAsync` (saves a list, same page); the layout of a signed token's binding (`WriteBinding` lines, the tail and name/value switch of `WriteResolvedKeyCondition`, the length check the JSON check repeats; signer and verifier share it); `ContinuationToken.GetHashCode` returning 0; an empty handover in `ParallelScan`; `KeyInputChanges` capacity, key cache, unused text and the table-key branch of `ValueOf` (a table-key input cannot change); the `BatchWriteOperation` loop bound (an extra empty table) and `BatchTotals` empty list (read as empty); `CapacitySum` `sum ?? next` vs `next ?? sum` with one null; digit and dot boundaries of `AttributePath`, `ProjectionList` and `ExpressionWriter` no path or placeholder reaches; the order of unknown cursors in `FanOutMerge` (all read together before the first read); the `N` prefix in `FanOutPlan`; `TableSchema` `IsLocal` of a missing index and the type of a missing sort key; `ReadOptions.PageLimit` at `Limit == Take - Taken`.
- Static initializers (`MixedSchema`): all 9 killed by hand.

`core` shard (226 survivors, `src/DynaSharp` outside `Fluent/`):
- Simplified: the second JSON-object check of `SsoCredentialsProvider.TryParseCreatedToken`; `Read` before `Skip` on an unknown name in `CredentialsDocument` and `ProcessCredentialsProvider`; the length check before `~/` in `SharedConfigFiles.ResolvePath`; the point of a fixed point without fraction digits in `KeyReader`; the zero case of `KeyText.PlainDecimal`; the `_disposed` guard of `PooledByteBuffer.Dispose`.
- `ConfigureAwait(false)` is equivalent only where the code runs on the pool: the readers, watches and retries of `StreamMerge` (`Task.Run`), the retries of `ReadShardCoreAsync` with a `ShardRun`, `FailAsync`, `EnvironmentCredentialsProvider` (its cache completes at once), the body read of `CredentialsHttp` (already buffered). Not killed: the awaits of `CredentialProcess.RunAsync` on output and exit (a context test of a real program does not see them post).
- Equivalent:
  - `StreamMerge`: the second `ThrowIfCancellationRequested` (the channel's wait checks first), the `return` of `FailAsync` once stopped, `_stop.Dispose`, `[default]` for the list of children, `ChildrenOf` for every position (only `Latest` reads it), `>=` of two equal delays, the await of `_idle`.
  - `DynamoDbStreamsClient`: `await delayed` of a wait the caller cancelled (the next call throws the same exception before it sends).
  - `JsonValueWriter`: returns to `ArrayPool.Shared`, the size hints of `Grow`, the body of `Hash` (a constant hash still compares bytes), `text.Length > 0` (an empty name is refused earlier), `Current = -1` of a new frame.
  - `DynamoDbNumber` (13): comparisons whose equal case the JSON grammar rules out, the safe length `DecimalHoldsExactly` accepts anyway, `difference < 0` after `difference != 0`.
  - Credentials: `CreateNoWindow` (Windows only); the drain of stderr; `parsed = false` of the container and web identity judges; the region chain of `AssumeRoleCredentialsProvider` but the order of given and configured region; the `return default` of an emptied catch (`SharedConfigFiles`, `AssumeRoleCredentialsProvider.TryResolveRegion`, `SsoCredentialsProvider.ReadOidcError`); `_stsRegionResolved` and the initial `origin` of `ProfileChain`; `<=` at the edge of the IMDS refresh rate; "UTC" to "Z" of `SsoToken`; the `continue` of `StsDocument`; the header of `IniFile` on a failed parse; the second check of `RefreshingCredentialsCache` after the lock (a race only); `File.Delete` of the temporary file of `TextFiles`.
  - Keys: `>>>` on an unsigned mantissa, stack vs heap in `FormatsAs`, the room of `MinimumFormattableSpace`.
  - Others: `return true` of the `ConflictRetry` wrappers (discarded); `??=` of the explanation of `ConditionalCheckFailedException`; `+=` to `-=` of the hash codes of `AttributeValue` and `Item`; the shape fast path of `Item.Equals`; `"c"` of `TimeSpanConverter`; `skipInputValidation` and `Dispose` of the pooled values of `JsonBridge`; the empty catch of `ConverterChecks.ReadValue`; `string.Empty` as the shard of `ChangeRecord.As` and the ARN in `TableStreamExtensions` (replaced before use); the `Enabled` guards of the stream counters; `?? string.Empty` of `DiscriminatorSchema`.
- Flaky across runs: `ConfigureAwait` mutants of the SSO provider and `CredentialProcess` (the SSO ones have a test now); the messages of `DiscriminatorSchema.FindProblem` survive only because the schema is built in a static initializer (killed by hand by `ItemDocumentTests`); `DynamoDbStreamsClientReadStreamJoinedTests` failed once under load (not reproduced in 15 runs).

## Fidelity of the fake

Real DynamoDB is the spec; the emulators are witnesses with quirks. Tools: `tests/DynaSharp.IntegrationTests/Differential` (rules in `KnownDivergences.cs`), `Fidelity/FidelityTests.cs` on `real-aws.yml`, `Testing/Fake/Vectors` from `build/generate_fake_vectors.py` (DynamoDB Local 3.3.2), the gaps of `Backends/Gaps.cs`, and the Parity Suite. Differential runs: about 9,500 sequences per emulator while the harness grew and 3,100 more on DynamoDB Local; the last ones left no unexplained divergence (Dynoxide seeds 9001 to 11500, DynamoDB Local 9240 to 12339), and the CI default (120 from seed 1) is clean on both.

### Parity Suite score

`parity.yml` run 41811905906 on `aba8754`, suite `7a5d72f` v3.9.0, `!cloud-only`, 2026-11-04. Divergence is failed of total; the grade adds a third of the skipped.

| Tier | Total | Passed | Failed | Skipped | Divergence | With a third of skipped |
|---|---|---|---|---|---|---|
| tier1 | 497 | 496 | 1 | 0 | 0.2 % (A) | 0.2 % (A) |
| tier2 | 452 | 101 | 0 | 351 | 0 % (A) | 25.9 % |

The failure is `AttributesToGet` in `getItem/projection`, refused by decision. Skipped in tier2: `partiql` 275, `vectorSearch` 32, `multiAttributeKeys` 14, `tags` 8, `backups` 5, `account`, `contributorInsights`, `export` and `resourcePolicy` 2 each, `kinesis` 1. The suite's `results/dynamodb*.json` are answers of real AWS; 12 item bodies of its captures are in `real_aws_responses.json` (attribution in `NOTICE`).

### Answers of real DynamoDB

From `FidelityTests` (runs 42356511937 and 42361778673, 2026-11-07), asserted on AWS and the fake; an emulator that answers otherwise is skipped with its answer.
- A `TransactWriteItems` replayed with its token and another `ReturnConsumedCapacity` is `IdempotentParameterMismatchException`.
- A put over 400 KB in `TransactWriteItems` is a `ValidationException` of the request.
- An update that fails on its item (path into a missing map, `list_append` of a missing list, a magnitude out of range, a sum over 38 digits: "Number overflow. Attempting to store a number with magnitude larger than supported range") cancels with a `ValidationError` reason, also beside a failed condition.
- A key off the schema in `TransactWriteItems` or `TransactGetItems` cancels: `ValidationError` for it, `None` for the others. An update of a key attribute in a transaction cancels with one reason per action.
- `BatchGetItem` with keys `1` and `1.0` is accepted and returns the item once.
- Numbers come back in canonical plain digits (`1.0`, `01.50`, `1E+2`, `-0`, `1E+125`, `1E-130`) at the top level and in lists, maps and sets.
- A bare `size`, `Size` or `SIZE` outside a call is a name, although the API reference shows `Size = :myval` failing; `DYNA220` follows.
- `LastEvaluatedKey` when `Limit` ends at the last match: absent whenever the key condition has a sort key part; with the partition key alone present for one item with `Limit: 1` (both `ALL_ATTRIBUTES` and `COUNT`) and for two items with `Limit: 2` and `ALL_ATTRIBUTES`, absent for two items with `COUNT`. The fake leaves it out for a sort key part and sends it otherwise (Open). The Developer Guide says a reached `Limit` returns it; DynamoDB does not always.
- The reason of a failed `ConditionCheck` is `The conditional request failed`, no period; a cancelled `TransactGetItems` lists its reasons in the message.
- From the Parity results of AWS: `UPDATED_OLD`/`UPDATED_NEW` of a nested path are fragments of the named paths; `TransactGetItems` leaves `Item` out when the projection takes nothing; a replayed transaction reports twice a consistent read of each item; sets come back in DynamoDB's order (strings and binaries by bytes, numbers by value).

### What the fake follows

The fake's tests and `Vectors/*.json` pin each of these; listed so nobody "fixes" them.
- The same operand twice (`a = a`, `begins_with(a, a)`, ...), a `BETWEEN` bound that is no S, N or B, redundant parentheses and a legacy parameter beside an expression are `ValidationException`.
- The item of a write is checked before its condition; in a transaction a mistyped index key is a `ValidationError` whatever its condition, and an index key over 2048 (partition) or 1024 (sort) bytes fails the request.
- Capacity: a read is at least 1 unit (0.5 eventually consistent); with `INDEXES` an index read reports `Table` with 0; a condition check in a transaction costs 2; an index entry is sized without the 100-byte overhead. A 1 MB page ends with the item that passes 1 MB. A 39-digit number is a plain `ValidationException`, also in a transaction.
- A projection or filter of an index on attributes it does not project is a `ValidationException` in `Query` (a filter of a `Scan` of an index sees the table item); an index that `UpdateTable` creates leaves out items with an empty, mistyped or oversized index key.
- Paths: `SET l[11] = :b, l[10] = :a` on `[x, y]` appends in index order; `REMOVE l[i]` past the old end does nothing; `REMOVE a.b` without `a` is "The document path provided in the update expression is invalid for update".
- Expressions (from DynamoDB Local): `size` of a string counts UTF-16 code units; `size` of a scalar is no error (a filter matches nothing); `a <> :v` holds on a missing attribute or another type; constant operands evaluate, and a constant of the wrong type is rejected before the item, also for `<`; a key condition on a non-key attribute or on the sort key alone is "Query condition missed key schema element"; an empty sort key value is rejected like an empty partition key.
- Streams: records commit at the end of a request and drop with it; no record for an unchanged item; records trimmed after 24 hours, a closed stream gone 24 hours after closing, iterators expire after 15 minutes (exactly 15 is expired); `ListStreams` of something unknown answers `"Streams":[]`; `UpdateTable` checks the shape of `StreamSpecification` before the table's state.
- `OnDemandThroughput`: described only when a maximum is set; `-1` removes one; one member alone is allowed; 0, below -1 and above 40,000 are rejected; with provisioned capacity it is rejected.

### Emulator quirks (not followed)

Each is a rule in `KnownDivergences` or a row of `Gaps.cs`.
- DynamoDB Local 3.3.2: returns numbers as written inside lists, maps and sets; reports capacity by rules of its own (0 for a read that finds nothing, an index write for an item outside the index, 1 unit per transactional write, capacity nobody asked for); writes the `ConditionCheck` reason with a period; cancels a `TransactGetItems` with a key off the schema without `CancellationReasons` and fails such a write request; answers `{"Item":{}}` for a projection matching nothing; takes `1` and `1.0` as two items in one transaction; leaves `LastEvaluatedKey` out for `sk <= :end` but sends it for `begins_with`, `>=` on an index and with a filter; sends `Message` (Dynoxide `message`), empty for `IdempotentParameterMismatch`; answers concurrent DDL with `InternalFailure` (HTTP 500, retried) and fails a provisioned table with 2 global and 2 local indexes under parallel load (two tests are `[NotInParallel]`); returns `ACTIVE` from `CreateTable`; numbers all stream records from one counter, so `AT_SEQUENCE_NUMBER` of a shard's start can be `TrimmedDataAccessException`; never reports `SSEDescription`; answers "Nothing to update" to an `UpdateTable` of the table class or `SSESpecification` alone; keeps one database per access key and region without `-sharedDb`.
- Dynoxide 1.5.0: skips much validation (filters and projections an index does not project, operand types, `BETWEEN` bounds, `attribute_type` names, an empty `begins_with` prefix, an empty key in `TransactGetItems`, same-path operands); rejects `(a) = :v`, `a[ 0 ]`, `size(:v)` and a bare `size`, accepts `SIZE(a)`, `_a` and 101 `IN` operands; applies a key update inside a transaction; returns the key as `ALL_OLD` of an item an update creates; filters a scan of an index on the projected attributes; deletes from a number set by text; adds RCU/WCU to `TOTAL` capacity of transactions; adds keys to `TransactGetItems` projections; returns `CREATING` from `CreateTable`; ignores index capacity and `OnDemandThroughput`; has one stream shard, an `EndingSequenceNumber` on its open shard, no `KeySchema` in `DescribeStream`, drops a disabled stream and keeps a stream under the table name (a recreated table replays old records); runs transactions one at a time.
- Neither emulator produces `TransactionConflict`, `TransactionInProgress`, `UnprocessedItems` or `ExpiredTokenException` on demand: those paths are tested with stubs only.

### Open questions of the fake

Only real DynamoDB answers these; the fake's current choice is in brackets.
- Queries: the `LastEvaluatedKey` rule above; whether a filter on a non-projected attribute of a local index fetches from the table [rejected, as Local]; whether a `Scan` of an index rejects such a filter [evaluates on the table item]; the order of attributes in "Global secondary index <name> does not project [...]" [expression order]; `GetItem` of a projection that matches nothing [`{}`].
- Updates: the index of an appended element in `UPDATED_NEW`/`UPDATED_OLD`; the type check of a constant operand of `-`; whether an empty index key of a transaction is found before the conditions.
- Capacity: whether `TOTAL` splits a transaction's units into read and write units [only with `INDEXES`].
- `OnDemandThroughput`: an unset maximum as absent or -1 [absent]; -1 or one member in `CreateTable` [allowed]; the messages of an empty object and of an index `Update` without throughput [the fake's own]; on an index of a provisioned table [rejected]; the maximums over a switch to `PROVISIONED` and back.
- Encryption: `SSEType: AES256` in `CreateTable` [reports `KMS`]; `Enabled: false` with a key; an alias as `KMSMasterKeyId` [reported as the alias's ARN]; `SSESpecification` in `UpdateTable` naming the current encryption [accepted]; the description after switching back to the AWS-owned key [`SSEDescription` dropped].
- Streams: a sequence number above the last record of an open shard [`ValidationException`]; an unknown `ExclusiveStartStreamArn` while streams exist [empty page]; the message of a missing `SequenceNumber`; reading a child at its starting sequence number (Open).
- Numbers and names: exponent forms such as `1E-07` as `N` text (emulators accept and normalize; `+5`, `.5`, `5.`, `0005` pass, whitespace, `0x10`, `1_000`, `1,5` do not); a bare name starting with `_` [syntax error].
- Messages not mimicked: empty sets, duplicates in a set, an update past 400 KB; which fault of several is reported first differs in places (`docs/testing.md` lists the validation order as fine print).
- Harness limits: no local secondary index, no stream reads, no `ReturnItemCollectionMetrics`, GSI sort numbers from a small pool, the 1 MB cut compared only on ordered queries.
