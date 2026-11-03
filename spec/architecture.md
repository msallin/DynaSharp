# DynaSharp - architecture

How the code is built today, for a maintainer taking it over. `requirements.md` says why and what, `api.md` states the public contract,
`../docs/diagnostics.md` explains the compile-time rules to users, and `tasks.md` records decisions and findings. `history/` holds the design documents
as they were written; they are not updated and describe dropped designs next to live ones. Class remarks are the detail behind this document,
and it points at them instead of repeating them.

## 1. Module map

| Project | Target | Role | Depends on |
|---|---|---|---|
| `src/DynaSharp` | `net11.0` | The library, one assembly, internal by default | Nothing at run time; the generator as a build order only |
| `src/DynaSharp.Generators` | `netstandard2.0`, C# 12 | Source generator, analyzers and one code fix in one dll, packed into `DynaSharp` under `analyzers/dotnet/cs` | Roslyn 5.0.0 (a floor, see `Directory.Packages.props`), no project |
| `src/DynaSharp.Testing` | `net11.0` | `StubHandler`, `StubResponse`, `StubClient`, `RecordingHandler`, `RecordedRequest` and `RecordedTransactStep`, `StubSts` on a socket of its own, because the HTTP of a credentials provider is not behind `HttpHandler`, and the in-memory `FakeDynamoDb` with its internals in `Fake/`, see section 14 | `DynaSharp`, and its internals: `WireNames` and `TableJson` for the answers, `TableSchema` for what an entity declares, the internal constructors of the clients for a client that reads no environment, `AttributeValueConverter`, `DynamoDbReader`/`DynamoDbWriter` and `ErrorMapper` for the fake. It packs a `README.md` of its own, and `DynaSharp` packs the one of the repository. It grants `InternalsVisibleTo` to the unit tests |
| `tests/DynaSharp.UnitTests` | `net11.0` | Unit, generator and analyzer tests, the only coverage source | All three, the generator as analyzer and as assembly |
| `tests/DynaSharp.IntegrationTests` | `net11.0` | The same tests on each emulator, on demand on real AWS, and the recipes of `patterns.md` | Docker, Testcontainers |
| `tests/DynaSharp.AotSmoke` | `net11.0` | Publishes with Native AOT and runs | `DynaSharp`, `DynaSharp.Testing` |
| `tests/DynaSharp.DefaultsFixture` | `net11.0` | Entities of a project that sets `[assembly: DynamoDbDefaults]`, which holds for a whole assembly, so the unit and integration tests reference them | `DynaSharp`, the generator as analyzer |
| `tests/DynaSharp.PackageSmoke` | `net11.0` | Consumes the real nupkg; outside the solution | `artifacts/packages` |
| `benchmarks/`, `samples/` | `net11.0` | Comparison with other clients; a runnable tour | `DynaSharp` |

`InternalsVisibleTo` of `DynaSharp`: `DynaSharp.Testing`, `DynaSharp.UnitTests`, `DynaSharp.Benchmarks`, `DynaSharp.AotSmoke`. The generator grants it to the unit tests. A generated file
reaches only public members, which is why `ConverterChecks`, `KeyWriter` and the like are public.

The folders of `src/DynaSharp`, top to bottom. A folder is not always a namespace: `Fluent` holds the public commands in `DynaSharp` and their
machinery in the internal `DynaSharp.Fluent`, whichever of its folders a file is in, `Expressions` holds `Attr`, `Expr`, `Condition`, `SortKeyCondition` and their extensions in `DynaSharp` and the structs conditions compose from in `DynaSharp.Expressions`, and `CommandHandle` is in `DynaSharp.Expressions` too, in `Fluent/Commands`, as `PartitionKey<T>` is in `DynaSharp`, in `Keys`,
and `Shared` and `Exceptions` are in `DynaSharp`.

```text
DynamoDbClient, DynamoDbStreamsClient, Streams        public entry points
Fluent, Expressions           Requests, Operations    the two APIs, siblings
                  \           /
                   Http                               ClientCore, DynamoDbPipeline, IWireCall
     /        /       |       \        \
Signing  Credentials  Retries  Errors  Telemetry      what an attempt needs
Configuration                                         options, environment, shared files, into ClientConfiguration
Serialization, Mapping, Keys, Shared                  the codec: one reader, one writer, converters, encodings, values
```

| Folder | Holds | Mainly uses |
|---|---|---|
| `Http` | `ClientCore`, `DynamoDbPipeline`, `IWireCall<T>` and two calls, `CallBuffers`, `BoundedPool<T>`, `ConflictRetry` | Everything below it, and `Fluent` for `CommandPool` |
| `Operations` | One `IOperation<,>` struct per service call, `OperationJson`, `WireNames`, `TableJson`, `ReadTags` | `Http`, `Requests`, `Serialization`, `Telemetry`; also the streams operations |
| `Requests` | Records of the request API and `DynamoDbRequests` | `Operations`, `Http` |
| `Streams` | `DynamoDbStreamsClient`, its records | `Requests`, `Operations`, `Http` |
| `Fluent` | `Table<T>`, `Index<T>`, `TableExtensions`, `TableModel`, `DiscriminatorCheck`, `TableAdministration`, `TableSchema`, and the four folders below | `Expressions`, `Http`, `Mapping`, `Operations`, `Requests`, `Retries`, `Serialization`, `Telemetry` |
| `Fluent/Commands` | What a chain is built of: every command and step, their interfaces, the verbs of a transaction, and what sends one item: `CommandCall`, `ICommandOperation<T>`, the operations of a write, `WriteResult<T>`, `FailedCondition`, `RejectedRequest` | As `Fluent` |
| `Fluent/Reads` | What a read does after its clauses: operations of reads, `PagedRead`, `PageCall`, `PageResponse`, `Page<T>`, `ContinuationToken`, parallel scan, fan-out and mixed machinery, `FailedItem` | As `Fluent` |
| `Fluent/Actions` | What a command of several actions does: `ActionCall`, `ActionList`, `BatchDriver`, `Transaction`, their operations and results | As `Fluent` |
| `Fluent/State` | What a command holds while it is built: `CommandState`, `WriteState`, `ReadOptions`, `CommandPool`, `ExpressionWriter`, `WriteRules`, `EntityWrites`, `KeyInputChanges`, `HeldKey` | As `Fluent` |
| `Expressions` | Condition structs, `Attr`, `Expr`, `SortKeyCondition`, `ExpressionHandler` | `Fluent`, `Mapping`, `Serialization` |
| `Mapping` | Attributes and the contract of section 6 | `Serialization` |
| `Serialization` | `DynamoDbReader`, `DynamoDbWriter`, converters, `PooledByteBuffer`, `ItemShape`, `ShapeTable` | `Mapping` |
| `Keys` | `KeyWriter`, `KeyReader`, `KeyText`, `KeyTimeFormat`, `PartitionKey<T>`, `GuidExtensions` (the bounds of the version-7 values of a millisecond, `Guid.FirstVersion7` and `Guid.LastVersion7`, in namespace `DynaSharp`) | `Mapping` and `Shared` for `PartitionKey<T>`, and nothing of ours otherwise |
| `Shared` | `AttributeValue`, `Item`, capacity types, shared enums | `Mapping`, `Serialization`, `Requests` |
| `Retries` | `RetryPolicy`, `RetryQuota`, `FailureClassifier`, `ClockSkewCorrection` | Nothing of ours |
| `Credentials` | The providers, `IRefreshableCredentialsProvider`, `RefreshingCredentialsCache`, `CredentialsHttp`, the documents of the sources (`CredentialsDocument`, `StsDocument`), `CredentialsDependencies` (the seams a profile hands to the providers it builds), `CommandLine` and `CredentialProcess` (the program of a `credential_process`), `ProfileChain` (a profile to its provider, in botocore's order), `Sts` (what web identity and assume role share), `SsoToken` (the token cache of the AWS CLI) | `Configuration`, `Telemetry`, `Signing` (for `AssumeRole`) |
| `Configuration` | `ClientConfiguration`, `SharedConfigFiles` (profiles and `sso-session` sections), `IniFile`, the file seams `ReadTextFile` and `WriteTextFile` | `Credentials`, `Retries` |
| `Signing` | `SigV4Signer` | `Credentials`, `Http` |
| `Errors` | `ErrorMapper` | `Operations`, `Serialization` |
| `Telemetry` | `OperationTelemetry`, `DynaSharpTelemetry`, `TelemetryNames`, log messages | `Errors`, `Http`, `Requests` |

There are cycles between folders, because it is one assembly: `Mapping` and `Serialization` name each other, and `Http` and `Fluent` do through the
command pool. `src/BannedSymbols.txt` is what enforces no unsafe code and no reflection, and the analyzers fail the build on it.

## 2. The request path

Everything that reaches the wire goes through `DynamoDbPipeline.SendAsync<TCall, TResponse>`, the one retry loop. Its remarks and those of `IWireCall<T>` are the
reference. `ClientCore` owns the configuration, the `HttpClient`, the pipeline and the `CommandPool`, and both public clients are thin over it.

1. `call.GetTableName()`, the span and the start time, then a `CallBuffers` from a bounded pool.
2. `call.WriteBody(buffers)` once, inside the span. The body is hashed once with SHA-256. Every attempt sends these bytes.
3. `AttemptAsync`, per attempt: rent a timeout source, get credentials, take the corrected signing time, build and sign a new `HttpRequestMessage`, send with
   `ResponseHeadersRead`, read the body into the pooled `buffers.Response`, then `call.ReadResponse` or `MapError`.
4. On failure `Decide`: `FailureClassifier` sorts the exception, `RetryPolicy.Decide` answers, and the counters and warnings happen here. A retry waits on the client's `TimeProvider`.
5. After the loop `RecordResponse`, outside it on purpose, so a failure there is not retried.
6. In `finally`: duration, span, buffers, then `call.Release()`, exactly once and last.

Rules that are easy to break:

- The pipeline is generic over a struct, so a call has no delegate and no allocation of its own. A default interface member on `IWireCall<T>` would box. A new member goes to all five implementations.
- `SendAsync` and `AttemptAsync` use `PoolingAsyncValueTaskMethodBuilder`, so their `ValueTask` is awaited exactly once. That is why terminals are never awaited twice and never stored.
- `Parse` turns `JsonException`, `InvalidOperationException` and `FormatException` into an unrecognized response. `DynamoDbMappingException` derives from `Exception` and goes through unchanged, and it is not retried. Its `Key` is filled where items are read: `GetOperation` from the request, `PageResponse` and `BatchGetOperation` through `FailedItem`, from a copy of the reader per array. With `ReadOptions.OnMappingFailure` set, `PageResponse` goes on from that copy past the failed item, drops its entry from the `FanOutKeys` of a fan-out, and puts the exception into `PageData.Skipped`; `PagedRead.SendAsync` then hands each to the callback after the call returned, so what the callback throws is neither wrapped by `Parse` nor retried, and counts it in `dynasharp.client.operation.skipped_items`. Without the option `SendAsync` returns the task of the call as it is.
- `SetActivityTags`, `SetResponseTags`, `GetConsumedCapacity` and `DescribeFailure` must not throw: the span is already open.
- The catch of `SendAsync` hands every `DynamoDbException` to `call.DescribeFailure`, before `Release`, so that the bytes of the body are still there; every other failure, and every success, passes it by. `CommandCall` explains a `ValidationException` through `RejectedRequest` and fills `ConditionalCheckFailedException.Key` through `FailedCondition`, which also adds the line of the message that names the condition, `ActionCall` explains a `ValidationException` and names the failed actions of a `TransactionCanceledException` through `CanceledTransaction`, `PageCall` only explains, `RequestCall` takes the key from `TOperation.GetKey`, a `static virtual` that `UpdateItemOperation` and `DeleteItemOperation` override and that is null otherwise, and `RawCall` does nothing. The key stays on the exception: telemetry reads the error code alone.
- Clock skew travels through a private `ClockCorrectedException`, which never leaves the pipeline. A resend counts against `MaxAttempts`, uses no wait and no quota.
- `ClockSkewCorrection` is a `TimeProvider`, created once per client in `ClientConfiguration.Resolve`. The pipeline signs on it and corrects it, and the default chain that `Resolve` creates judges expiry on it, because an expiry is a time of the service. Its timers and timestamps are those of the local clock. A provider the application creates knows nothing of it and uses the local clock.
- `ExpiredTokenException` and `ExpiredToken` are permanent for `FailureClassifier`, and yet `MapError` tells a provider with the optional `IRefreshableCredentialsProvider` to drop the credentials of the request, whether or not an attempt is left. If it says that this helps, a private `CredentialsDroppedException` carries the rejection to the loop, which resends under the rules of clock skew, plus one: once per call, because the new credentials may be just as stale. The fetch is not part of this: the next attempt looks up credentials as always, inside its timeout, so a failure surfaces as `CredentialsUnavailableException`.
- `RefreshingCredentialsCache.Drop` compares instances, so credentials that were replaced in the meantime are left alone, and it forgets the credentials instead of fetching, so the concurrent requests that fail together all end in the one flight of the cache. `DefaultCredentialsProvider` forwards to its chosen source and never moves on to another one.
- `CreateRequest` adds the content type as raw text, because the signature covers it as sent. The signer has no other caller, so it validates nothing.
- `WebIdentityCredentialsProvider` calls STS without a signature, on `CredentialsHttp.GetClientFor`, because STS may sit behind an egress proxy, unlike the link-local endpoints of the shared client, and an endpoint from `AWS_ENDPOINT_URL_STS` or `AWS_ENDPOINT_URL` on loopback cannot. `ClientConfiguration.FindEndpointInEnvironment` reads those variables for it and for the client alike, and an endpoint passed to its constructor goes ahead of them, checked by the same `FindEndpointProblem`. The token is a bearer credential, so that client follows no redirect, and the `Message` of an STS error goes nowhere: `StsDocument` does not even keep it, because the service may quote the request in it.
- `StsDocument` reads with `ReadElementContentAsString`, which moves past the element. Its loop calls `Read` only for what it does not consume, or it would skip every sibling that follows without whitespace.

| Call | Kind | Body lives in | Who releases what |
|---|---|---|---|
| `RequestCall<TOperation, TRequest, TResponse>` | Request API | `CallBuffers.Request` | Nothing to release |
| `RawCall` | `SendRawAsync` | `CallBuffers.Request`, copied from the caller | Nothing to release |
| `CommandCall<TOperation, TResponse>` | One-item command | The `CommandState` | The call releases the state |
| `PageCall<TOperation, T>` | One page of a read | The `CommandState` | Nothing: the state outlives the page, and the read releases it |
| `ActionCall<TOperation, TResponse>` | Request of a batch or transaction | The state, or `CallBuffers` for a batch | The call if `TOperation.EndsCommand`, otherwise `BatchDriver` |

Retries are AWS standard mode, tuned: `FailureClassifier` lists what is throttling, transient or a timeout, and the remarks of `DynamoDbClientOptions.MaxAttempts` are its public
description, so a change in one changes the other. `FailureClassifier.IsTransient` answers `DynamoDbException.IsTransient` from `Classify`, plus the conflicts of a transaction, so the two cannot disagree. `RetryPolicy` holds the numbers. The per-call `.MaxAttempts(n)` reaches the loop through `IWireCall<T>.MaxAttempts`, with 0 for
the client's value.

`RetryOnConflictAsync`, on `DynamoDbClient` and on `Table<T>`, which reaches the same `ClientCore` through its `TableModel`, is the one retry a caller has to ask for, and it sits above the pipeline, which never sees it: `ConflictRetry` runs a delegate, `FailureClassifier.IsConflict` says what runs it
again, of the `ConflictKind` the call names, and `RetryPolicy.GetConflictDelay` says how long it waits, on the client's clock, with the jitter of the policy and nothing from the quota. A run is any number of calls with their own attempts. The remarks of the method are the contract.

Telemetry is one span per request (`DynamoDB.GetItem`), `db.client.operation.duration`, `dynasharp.client.operation.retries` and `.throttles`,
`dynasharp.client.conflict.retries` and `.exhausted`, and `dynasharp.client.consumed_capacity`, all from `OperationTelemetry`, one per client. A batch of three requests is three spans. A conflict retry has no span of its own: the
failed calls of a run have theirs under the span of the caller, so another one would add a level and no fact, and its counters take the operation and the table from the exception. Logs exist for what no single request sees: resolved configuration, an endpoint over plain http to a host that is not loopback (`ClientConfiguration.IsUnencryptedRemote`, once per client and once per web identity provider),
retries given up, a corrected clock, a batch that gave up, a conflict retry that gave up, credential refresh. Neither ever carries a key or a value.

The request API adds nothing to this: an `IOperation<TRequest, TResponse>` is a stateless struct with `Name`, `WriteRequest` and `ReadResponse`, and `RequestCall` forwards to it.

## 3. The command layer

### Handles and state

A command is a `readonly ref struct` of three fields: a `CommandState`, a version and a `TableModel`. The state is a pooled class holding the request as UTF-8, and it is what
crosses the `await`. The remarks on `CommandState` explain the version protocol and the ownership of a state, and they stay the reference.

- `CommandState.Enter` checks the version and consumes the handle; each fluent method does `Enter`, changes the state and returns a new handle.
- `CommandState.Check` checks and leaves the handle valid. `ExpressionHandler` uses it, because the compiler runs the handler before the method it belongs to.
- `ToRequestJson` is a terminal like `SendAsync`. `Settle` and `Finish` change the state for good: they append conditions, close the open item and write the options into `Body`, and a transaction or a batch ends its actions. Leaving the command usable would take a copy of a state with its open writer, key inputs and actions, on every path that renders, which is more than a debug aid is worth, so the stale message names rendering instead. It cannot be told from the state which way a handle went stale, because the state goes back to the pool at once.
- `Release` is the only way back to the pool, and a compare-and-swap on the version lets one holder through. A new field goes into `Release` as well, and a new member of `WriteState` or `ReadOptions` needs nothing there, because `Release` puts their default values back.
- A command that is built and abandoned is collected with its arrays. It costs one pool entry, not a leak.
- A state keeps arrays up to 16 KB when idle, and the `CommandPool` of a client keeps up to 64 idle states.

`TableModel` holds what never changes in pre-encoded form (`BodyStart`, `KeyStart`, `SortKeyStart`), so a command copies bytes. `Table<T>` is a `TableModel` with a type; `Of<T>()` caches
one view per type. `TableModel.Writes` is an `EntityWrites` only for an entity with a version or a key template, so every other command pays one null check. It also finds the indexes of literals, once, in `LiteralKeys`.

### Body and expressions

The fixed parts go to `CommandState.Body` as they are known. An item goes through `DynamoDbWriter` on `BodyJson` and stays open until `Finish`, so `With` can add attributes.
Expressions go to separate segments, because one call feeds a clause and two maps at once. `Finish` closes the item and appends options and expressions in one pass: `WriteMembers`, then `}`.

`ExpressionWriter` holds one buffer per `Clause` (`Set`, `Remove`, `Add`, `Delete`, `Condition`, `KeyCondition`, `Filter`, `Projection`), the name map, the value map and a pending buffer. Names and values are
deduplicated by comparing bytes, with no hash and no box. An interpolated expression lands in pending first, because its handler runs before the method that says which clause it is for;
`CommitPending` then checks that it names an attribute, and joins conditions as `(a) AND (b) AND (c)`, with `_chains` saying which clause is such a chain. Inside one condition, `ExpressionSink.WriteGroup` flattens a chain of one operator: a struct condition cannot ask what type its term is without a box, so the sink remembers where the last group it wrote starts and ends, and a term that is exactly that text loses its parentheses, while text of a custom condition stays. `AppendPath` is the path syntax, and `PathSyntax` in the analyzer is a copy of it. The discriminator of an update and the keys of an index of literals use `SetMissingAttribute`,
which walks the clauses to see whether an action already targets the attribute.

A paged read writes its members once and remembers the offset in `_pageMark`; each page truncates back to it and appends a new `ExclusiveStartKey`. A transaction writes
a set of expressions per action and clears them in `EndAction`, so `#n0` means something else in each action.

`RejectedRequest` explains a `ValidationException`: it re-reads the body as sent and maps placeholders to names, and to the types of values. `FailedCondition` does the same for a `ConditionalCheckFailedException`: its `Key` is the `Key` member of the body, or the attributes of `TableModel.ItemKeyNames` in `Item`, so a command keeps no key that a write that gets through would pay for. A failed condition is routine in a create-if-absent, so it walks forward with `DynamoDbReader` and stops at the key, where `RejectedRequest`, on an error path that is rare, parses all of the body with `JsonDocument`. `FailedCondition.Explain` names the condition in the message without reading anything: the write rules note each part they write, and `If` notes the caller's, in `WriteState.Conditions`, a byte that `Settle` leaves and `Release` clears. The exception keeps those kinds and the names of the table and the version attribute, and `FailedCondition.Line` makes the text when `Message` is first read, so a routine failure that code catches allocates nothing for it. It names the version by its attribute and the rest by kind. `CanceledTransaction` names each failed action of a transaction by its position, its verb, which it reads back as the first member of the action in the body, and its table from `ActionList`, so a transaction keeps nothing for it either.

### Write rules

`WriteRules` is where a clause cannot decide alone. `Table<T>.Put`, `Update`, `Delete` and their transaction forms mark `WriteState`, a struct in one field of `CommandState`, which `CommandState.Write` hands out by reference, later clauses may change the verdict, and
`WriteState.Settle` decides at the terminal and at the end of each transaction action (`CommandState.EndAction`). The state column below names its members.

| Marked by | State | Settled as |
|---|---|---|
| `WriteItem` of a versioned entity | `UnsettledVersion`, `EntityVersion`, `StoredVersion`, `PutEntity`, `WritePutEntity` | `AppendVersion`. `IgnoreVersion` restarts the item without the bump, through `CommandState.RestartItem` |
| `IfNotExists` of a put or a step | `ItemMustNotExist` | Throws if `UnsettledVersion` holds a version above 0, which no missing item can have |
| `ExpectVersion` for a write by key | `UnsettledVersion` | Throws unless `IfVersion` or `IgnoreVersion` came |
| `IfVersion(0)` of an entity with `EntityWrites.AllowsMissingVersion` | `VersionMustReadAsZero` | `AppendVersionZero`, the version missing or at 0, unless `IfNotExists` came. It waits because that replaces it, and a version from an entity decides the same way |
| `ExpectVersionOf` for `Delete(entity)` | `UnsettledVersion`, `EntityVersion` | `AppendVersion` of what the entity held |
| `ExpectUpdate` | `OwesLiteralKeys`, `OwesDiscriminator` | `SetMissingAttribute` per key of `EntityWrites.LiteralKeys`, then for the discriminator, last |
| `Set`, `Remove`, `NoteUnknownValue` by handle | `KeyInputs` | `KeyInputChanges.Apply`: checks every template, writes or removes derived attributes and keys of literals beside them, checks sparse siblings and copies one that is the text of an attribute. Values of the table key come from `CommandState.ReadKey` and `DynamoDbKeyTemplate.Parse`, or from `HeldKey` for an entity with `EntityWrites.HoldsKey` |
| `StoreKeyInputs` | `StoresKeyInputs` | `KeyInputChanges.StoreTableInputs`: `SetMissingAttribute` per `EntityWrites.StoredTableInputs`, with the value from `HeldKey`, or from `Parse` of the key text unless `DynamoDbKeyInput.IsCutOff` says that the text holds less. It needs no read, and it throws what a key given as strings cannot give back |
| `NoteBatchWrite` | `UnsettledVersion` | Throws unless `IgnoreVersion` came |

The order in `Settle` matters: the version first, then keys, then stored inputs, then indexes of literals, then the discriminator, so that each sees the actions before it and the discriminator ends the `SET`. `ForgetEntity` exists because a transaction reuses the state for its next action.
`VersionChecked` is what makes `IgnoreVersion` after `IfVersion` throw. Raw paths go past all of this on purpose. `api.md` section 8 is the contract.

Building a one-item command, a batch or a transaction, and reading one attribute, allocates nothing once the pool is warm, except the box of a struct entity with a version on `Put`, boxed values and a key string on an update of a key input, and an `AttributeValue` per input of an update with `StoreKeyInputs`, with a box of the value when a generated key gives it. A generated key an update has to keep goes into a `HeldKey` that the state fills again, because the clause that wants it can come after the verb, so an update that never asks pays nothing for it. `CommandAllocationTests` pins that per
command shape, and `CommandStressTests`, `BatchStressTests`, `PagedReadStressTests` and `FanOutStressTests` hammer the pooled state.

### Table administration

`DescribeAsync`, `WaitUntilActiveAsync`, `DeleteTableAsync`, `CreateTableCommand` and what it does for time to live run the operations of the request API through `ClientCore.ExecuteAsync`, so they have its spans and metrics and write no JSON of their own. `TableAdministration` holds them. A wait looks at the table and its global secondary indexes, because the status of a table says nothing about a backfill, and at the indexes the entities declare, because a list does not show what it does not hold yet: `TableModel.Entity` gives those on a table, and `TableSchema.GlobalIndexNames` on `CreateTableCommand`. Only the wait of `CreateTableCommand` passes `unlistedIsFinal`, which ends it at `TableAdministration.CatchUpLooks` looks in a row without a declared index, the same five looks over 1.5 seconds that the create loop gives `DescribeTable` to catch up, because nothing but an earlier `UpdateTable` adds an index there.

`CreateTableCommand` is the one command that is an ordinary class over no pooled state: it crosses `await`, it can be kept, and a clause changes it and returns it. That is deliberate, because this is not a hot path, and it is why it has no `ToRequestJson` but `ToRequest`, whose record goes into the request API. `TableSchema` holds the union of the entities: `TableModel.Entity` starts it, and each `Including<T>` adds to it and throws at that call on a disagreement. A type of `ScalarAttributeType.Unknown` stays open, so a type stated by an entity fills it in and never conflicts, and what stays open is a string in the request. That is how `Table<Item>`, a type written by hand and a custom converter behave alike. Attribute names and the kind of an index are what entities have to agree on; templates are not compared, because an overloaded index is several templates over one attribute. `IfNotExists` describes before it creates, so that a table that exists takes no `dynamodb:CreateTable` and leaves no failed call, and a table it finds ends the command there, ahead of a wait that the same description may already satisfy. A missing or `DELETING` table goes into the create loop, which describes what `CreateTable` found before it counts as success, because `ResourceInUseException` is the answer for a `DELETING` table too. Such a table rethrows. A table that describe does not find is either gone or one another process just created, which `DescribeTable` does not see yet, so the command creates again, five times at most over 1.5 seconds, and that settles which of the two it was. `SendAsync` returns `CreateTableResult`: the create loop says whether its `CreateTable` succeeded or found a table, and `EnsureTimeToLiveAsync` whether its `UpdateTimeToLive` was accepted. Time to live comes last, in `TableAdministration.EnsureTimeToLiveAsync`, behind a wait of its own unless one was asked for, because DynamoDB documents `ResourceInUseException` and a `ResourceNotFoundException` for a table that is not active. It looks before it changes anything, because a second change within the hour is refused, and it looks again after a `ValidationException`, because that refusal is also what the loser of two instances gets.

## 4. Reads

**Get.** `GetOperation<T>` calls `T.ReadAttributes` on the `Item` member, so `Item` and an entity go the same way. `GetValueOperation<T>` backs `ItemAsync`. Like every typed read it adds what it needs to a projection at the terminal, through `TableModel.AddRequiredNames`, and so does `BatchGet`.

**Query and Scan.** What a read does about items of other types is one `DiscriminatorCheck` of `TableModel` per kind of read: `DiscriminatorOnGet`, `DiscriminatorOnQuery` and `DiscriminatorOnScan` say `None`, `Filter` or `CheckEachItem`. A mixed read asks `TableModel.IsKey` itself, because it reads through a table without a schema, and the attribute is that of the mix. `TableModel.BeginQuery` and `BeginScan` write the key condition and add the discriminator filter. `BeginKeyQuery` does that for a key that writes itself: the key goes through the body and leaves it,
and `ExpressionWriter.AppendKeyCondition` copies its names and values into the maps, because they are JSON already. Terminals are in `PagedRead<TOperation, T>`, which owns the state from the moment
it gets it. `PageCall.ReadResponse` copies `LastEvaluatedKey` into `CommandState.PageKey` only after the whole response parsed, so a retry of a page sends what the first attempt sent. `PageResponse`
locates that key and does not parse it. `OnceEnumerable<T>` makes an enumeration single-use and releases the state however it ends, also for an enumerator disposed before its first `MoveNextAsync`. `ToPage` makes a
`ContinuationToken` from the raw key. A token is client input: `ContinuationToken.Parse` accepts a known format version and exactly one JSON object of attribute values, because that object reaches the body as raw bytes. The version is the first byte of the text, for page and fan-out tokens alike, and the JSON of a token from before there was one starts with a brace, which is no version. A client with `DynamoDbClientOptions.TokenSigner` writes version 2, `[2][key][HMAC-SHA256]`: `ContinuationToken.FromRawKey(key, state, plan)` signs `[2][key][binding]`, and `ThrowIfNotFor(state, plan)` in `PagedRead.PageAsync` and `FanOutRead.PageAsync` writes the binding again and checks it before `CopyKeyTo`. The binding is the length of the key, `TableModel.BodyStart` (table and index), `ExpressionWriter.WriteResolvedKeyCondition`, which puts the names and values in place of their placeholders, and for a fan-out the partition keys of the plan. `Parse` checks the shape of both versions and marks a plain token as text, so a client that signs rejects it, and takes a plain one this process made (`FromKey`, `Next` of a client that does not sign). Equality stays the text alone. Without a signer the plain path reads one field more and allocates nothing more.

**One attribute.** `Select` is `CommandState.SelectAttribute`, which writes the projection once, because no clause can follow it, and `GetSelectCommand`, `QuerySelectCommand` and `ScanSelectCommand`, which have terminals and nothing else, named `ValuesAsync` and `ValueAsync` because they give values and no items. A page reads each item through an `IItemReader<T>`, a static member of a struct, so `PagedRead` and `PageCall` take any `T`: `DocumentItem<T>` is
`T.ReadAttributes` and `SelectedItem<TValue, TConverter>` is the attribute, found by `AttributePath`, which `UpdateValueOperation` shares. `ReadOptions.ChecksDiscriminator` is the answer `CheckEachItem`, that no filter kept other types away, which is a `Get` and a query keyed by the discriminator, and then a copy of the reader walks the item once more. What a missing attribute means is `IDynamoDbConverter<T>.TryReadMissing`, and which converter that is the overload of `Select` decides: the
wrappers of section 6 make it `NullableConverter<T, TConverter>` or `NullableReferenceConverter<T, TConverter>`, whose `T?` is what the command gives. `AttributePath` reads through `DynamoDbReader.TryReadValue`, so a value that `CountsAsMissing` counts is an attribute that is not there, for `Select` and for `Return` alike. `TableModel.ReadTypeName` is the name generated code gives its type, so that a failure reads as that of the entity.

**Parallel scan.** `ParallelScan` runs one `CommandState` per segment from `StartSegment`, each on a task handing pages over a channel with room for one, so at most n requests and about 2n pages exist.
Its remarks say how failures and an early end work. `MixedScanCommand` shares it.

**Fan-out.** `FanOutQueryCommand<T>` holds a template state that never goes out, and a `FanOutPlan`. `FanOutRead<T>` has the terminals, `FanOutMerge<T>` the k-way merge over a heap, `FanOutCursor<T>` one partition,
`FanOutKeys` the keys of held items in pooled buffers, read from the JSON and not from the items. `NumberOrder` writes DynamoDB numbers in a byte-comparable form, so the merge compares only bytes.
`FanOutToken` is the composite position inside an ordinary `ContinuationToken`. `BoundedReads` limits concurrency. `StartPartition` copies the template with `CopyRead`, which assigns `ReadOptions` by value,
so a new read option comes along by itself. The remarks of `FanOutMerge` explain why nothing reads ahead.

**Mixed reads.** `Mixed<T1, ...>` implements `IDynamoDbDocument<...>`, so the normal page path reads it. `MixedSchema` is built once per type combination, holds the `IN` filter, the required projection and
`FindType`, which reads a copy of the reader as a checkpoint to find `_type` and then lets the type read from the start. A problem with a combination is stored and thrown at `Of<...>()`, never from a type
initializer. The mixed commands repeat the read clauses by hand per arity, and a test keeps them forwarding every clause. `Item.Is<T>()` asks a schema of one type, `MixedSchema.Of<T>`, whose
`FindType(Item)` applies the same rule to an item that is already read and answers -1 where the wire overload throws.

**`Item`.** `AttributeValueConverter` reads an `Item` as parallel arrays over a shared `ItemShape`, found through the process-wide `ShapeTable` of 2048 slots, so a page of alike items holds its names once. Nothing
in the table grows, and its remarks say what happens when shapes outnumber its slots.

## 5. Batches and transactions

Both are commands of several actions on one state, with bytes in `Body` and offsets in `ActionList`, which also holds the distinct tables, `Carry`, the token and what a batch read so far. `IActionOperation<TResponse>`
differs from `ICommandOperation<TResponse>` in that a request can name several tables and in `EndsCommand`.

**Batches.** `BatchWriteOperation.AddPut` and `AddDelete` write each action once. `BatchDriver.SendAsync` owns the state and releases it. It cuts actions into requests with `FindEnd`, 100 keys or 25 writes, the latter also by
bytes. Each request is an `ActionCall` with its own attempts. DynamoDB returns what it left in the form of `RequestItems`, so `Carry` holds that JSON and the next request sends it back unparsed. A response replaces `Carry` only once it is read to the end: one that fails to read (a mapping failure, a malformed body) leaves `Carry` as what was sent, takes back the items it added, sets `ReadFailed` and counts as a request, so the incomplete exception reports that request as unprocessed and not what earlier rounds got through. Progress resets the stall
count, and `MaxStalledRounds` bounds rounds in a row without progress, because a large `BatchGet` legitimately takes many rounds. `BackoffAsync` reuses the throttling backoff on the client's clock and takes nothing from the
retry quota. A throttle on the last attempt of a request becomes the incomplete exception, and so does any other failure after a request went through, with the failure as its `InnerException`, because the caller would otherwise lose what was already written. The first request has nothing to lose, so anything but a throttle stays raw there, and a cancellation always does. `GiveUp` parses `Carry` only then.

**Transactions.** `Transaction.AddAction` ends the action before it and starts the next, so an action stays open for its clauses, and it enforces the limit of 100. The verbs are extension methods of `TransactWriteExtensions` over
`ITransactWriteChain<T>` and `ITransactWriteStep<T>`, whose members are internal, because instance methods would repeat every verb on four structs. `TransactWriteOperation.End` writes `ClientRequestToken` into the
body once per send, which is what makes retries idempotent. `TransactMarker` and `TransactSlot<T>` are action indexes. `TransactGetOperation` copies the response to its own array, because `TransactGetResult` outlives the
pipeline's buffer and parses on demand. `TableModel.ForTransactions` stands for the tables of a transaction that has no table of its own.

## 6. The mapping contract

Generated code lives in the consumer's assembly and compiles against public members of `DynaSharp`. Generator and runtime ship in one package, so this contract has no compatibility rule across versions, but
every member below is reached from text in an emitter, and a rename shows up in snapshots and in nothing else.

| Runtime member | Who uses it |
|---|---|
| `IDynamoDbDocument<T>.WriteAttributes`, `ReadAttributes` | `Table<T>`, `DocumentConverter<T>`, `Item.From`, `To<T>`. Implemented by generated types, `Item` and `Mixed<...>` |
| `IDynamoDbDocument<T>.WriteNextVersion`, `IDynamoDbEntity<T>.ReadVersion` | `WriteRules`. `static virtual`, and static so that a struct entity is not boxed for them. Emitted only for a `[Version]` |
| `Attrs.__VersionConverter` | `Select`, through `TryReadMissing`, which gives 0. Emitted only for a `[Version(Missing = VersionMissing.Allow)]`, nested in `Attrs` like the forwarder of an enum, and it forwards to the number converter |
| `IDynamoDbEntity<T>.Info`, `WriteKey` | `DynamoDbClient.CreateTable<T>`, `Get(entity)`, `Delete(entity)`, `Index(name)` |
| `IDynamoDbDiscriminated<T>` | A marker; the constraint of mixed reads. `EntityEmitter` puts it on the declaration when the entity has a discriminator value |
| `IKey`, `IKey<T>` | `Keys.PrimaryKey`, which writes its key when a command asks, so a key costs no string. `ValueOf` exists only for an entity with `DynamoDbKeyTemplate.LosesValues`, and `HeldKey` keeps such a key for the terminal without a box |
| `SortKeyCondition` | The conditions of the generated key builders, over `Format`, `Prefix`, `UpperBound` and `Of` |
| `PartitionKey<T>` | The generated `Keys.X.Of`, through constructors hidden from IntelliSense, one for the string of a template and one for the `AttributeValue` of a plain key, each with the attribute it is for; `TableExtensions.Query`, which takes it and compares that attribute with what it reads |
| `DynamoDbEntityInfo`, `DynamoDbIndexInfo`, `DynamoDbKeyTemplate`, `DynamoDbKeyInput` | Metadata, read into `TableModel`, `EntityWrites` and `TableSchema`, and by `TableExtensions` for the name of a key builder and the kind of an index. `DynamoDbKeyInput.ComputedFromAttributes` is what `KeyInputChanges` asks for a computed input: a change at or inside one of those attributes is a change of the input to a value only the getter knows. `ToAttributeValue` is `ConverterChecks.ToAttributeValue` over the converter of an input of the table key with an attribute of its own, and `IsCutOff` says that this template holds less of it than the value |
| `Attr<T, TConverter>` | Generated `Attrs`. A handle is not tied to an entity: `EntityWrites.IsKeyInput` finds a template by path |
| `OmitEmptyConverter<TConverter>`, `OmitEmptyCollectionConverter<T, TElement, TConverter>`, `OmitEmptySetConverter<T, TElement, TConverter>`, `IDynamoDbConverter<T>.IsOmitted` | The type of the handle of a property with `[OmitEmpty]`, and `WriteRules`, which ask it through the type parameter. `static virtual`, and false by default, so that a struct converter compiles the question away. A collection needs to say what it enumerates, because a list and a read-only list have no interface in common that holds a count, and `OmitEmptySetConverter` is the same thing for a converter that `Add` takes, so that a set keeps its `Add` and a list does not get one. `ModelBuilder.OmitEmptyConverter` picks the wrapper. The write of a property with `[OmitEmpty]` is a generated `{ Length: > 0 }` or `{ Count: > 0 }` and goes through its own converter. `WriteEmitter` adds `IsOmitted` to the same condition where `ConverterResolver` finds a `[DynamoDbConverter]` that implements it, by way of `NullableHandleConverter` or `NullableReferenceHandleConverter`, which reach an explicit member through their type parameter. `EntityKeyBuilder` takes that back for a property that feeds a key, and `ModelBuilder.KeepWhatAReadNeeds` for one that a read throws without |
| `IDynamoDbConverter<T>.TryReadMissing` | `SelectedItem<TValue, TConverter>`. `static virtual`, and false by default, as `IsOmitted`. The null wrappers answer with null and the set converters with an empty set, which is what `MissingBehavior` says for an entity |
| `IDynamoDbConverter<T>.CountsAsMissing`, `DynamoDbReader.TryReadAttribute<T, TConverter>` | Generated reads, and `AttributePath` through `TryReadValue`. `static virtual`, and false by default, as `IsOmitted`. It is a question before the read, on a reader that comes by `in`, because the interface has no self type, so a default could not reach `Read`, and the read asks and then skips or reads, so that a converter cannot leave the value half read. `ReadEmitter` emits `TryReadAttribute` in place of `ReadAttribute` where `ConverterResolver` finds a `[DynamoDbConverter]` that implements it, or the generated converter of an enum with `[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]`, which is `ConverterModel.CountsAsMissing`, a flag and no forwarder, because both calls go through a type parameter. That converter has no symbol to ask, so `EnumRegistry` reads the attribute into `EnumModel.CountsUnknownAsMissing`, `EnumEmitter` gives the `file` struct a `CountsAsMissing` over `TryParseName`, and `AttrsEmitter` forwards it from the handle's struct; a `[Flags]` enum, stored as a number, never gets one. A value counted as missing leaves `__set` alone, so the initial value, the throw for a `required` member and the fallback key follow from what a missing attribute does. `EntityKeyBuilder` takes the flag back for a plain key. The null wrappers answer false for `AttributeType.Null`, and they and the wrappers of a handle pass the question on. What reads an element, `ElementReader` and `MapEntries`, does not ask |
| `NullableHandleConverter<T, TConverter>`, `NullableReferenceHandleConverter<T, TConverter>` | The type of the handle of a nullable property, which `AttrsEmitter` writes, and `AttrsEmitter.NullableHandle()` picks which of the two from the text of the read converter. A handle holds `T` without the `?`, for `Set` and the conditions, and a `string?` is a `string` at run time, so this is the one place that tells such a handle from another. A signature that names a converter has to see through it: the six overloads of `Select`, two each on `GetCommand<T>`, `QueryCommand<T>` and `ScanCommand<T>`, the overloads of `Add` for `IAddableConverter`, those of `AttrExtensions.BeginsWith`, and `DYNA216` |
| `SetElementValueConverter<T, TElement>` | The operand of `AttrExtensions.Contains` on a set: one element as a value of its own, the descriptor of the scalar type of the set before what `TElement.WriteElement` writes inside a set, so an element converter of a consumer needs nothing more. Public because the return type of those overloads names it |
| `DynamoDbReader`, `DynamoDbWriter`, `IDynamoDbConverter<T>`, built-in converters | Every generated read and write |
| `KeyWriter`, `KeyReader`, `KeyTimeFormat`, `KeyFormatException` | The generated key builders |
| `ConverterChecks.ExpectDiscriminator`, `TryFindString`, `DynamoDbMappingException` | Generated reads |

Reader contract: `ReadAttributes` starts after the opening brace and consumes the closing one, so one method serves an item, a list element and a map. Converters are static abstract and structs,
so a generic instantiation gets its own code. An enum gets a `file` struct in the generated file, and `Attrs` nests a public forwarder because no public signature can name a `file` type.

`KeyTemplates` in `Info` holds every key attribute made of properties, once: a template of the table without a delegate, an index template with `static values => Keys.X.Format(...)`, a plain index key without one, and a key of
literals of any index. A plain key of the table is left out, so an entity with neither pays nothing, unless an index template repeats it. A key of the table with an input an index template repeats, or with an input that has an attribute of its own, carries `Parse`, which reads the input from the key. A key of an index that is the stored text of an attribute carries `CopyOf`, that attribute, from `KeyTemplateEmitter.CopySource` and `KeyEncoding.HasTextOf`. `KeyTemplateEmitter` writes this and its remarks explain each case. The value of an input is boxed until the terminal, which is the
price of one delegate shape for all templates.

`Info` also says how each key is stored, for `TableSchema`. A template is a string. A plain key is what its converter writes, which `ConverterResolver.WireTypeOf` finds at compile time and `KeyModel.WireType` carries. A
custom converter gives that away only through `IAddableConverter`, which makes the key a number, so any other is `Unknown`: the generator cannot read what `Write` does.

Writes go `WriteKey`, then index keys, then the discriminator, then attributes, as the snapshot `SectionFour.Shop.Order.g.verified.cs` shows. A versioned entity gets `__WriteAttributes(writer, entity, version)`,
which both write paths call, so `WriteAttributes` stays a plain copy. The next version goes through a `checked` cast. A read keeps `var __item = reader`, a struct copy, and after the loop parses the
fallback key of what is unset. A generated constructor taking `in __Fields` is what assigns `init`, `required` and private setters conditionally and keeps initializers; `ConstructorEmitter` says why.

## 7. The generator

`DynaSharpGenerator` registers two pipelines, for entities and documents, each from `ForAttributeWithMetadataName`. Attributes are ordinary public types in `DynaSharp.dll`, found by metadata name in `WellKnownNames`.

| Stage | Code | Output |
|---|---|---|
| Parse | `KeyTemplateParser`: text to nodes, pure and tested on text alone | `TemplateParse` |
| Collect | `ProjectDefaults`, `TypeRules.Check`, `PropertyCollector`, `ConstructorBinder`, `ConverterResolver`, `EnumRegistry` | `PropertyFacts`, the constructor, converters |
| Bind | `KeyTemplateBinder`, `KeyInputCatalog`, `KeyInputs/*`, `EntityKeyBuilder`: placeholders to encodings, delimiters, buffer length, and all rules across keys and indexes | `KeyModel`, `IndexModel`, `EntityModel` |
| Model | `ModelBuilder.Compose` | `TypeModel` in a `BuildResult` with diagnostics |
| Emit | `EntityEmitter` owns the file. `ConstructorEmitter`, `WriteEmitter`, `ReadEmitter`, `KeysEmitter` with `KeyBuilderEmitter`, `PlainKeyBuilderEmitter` and `AttrsEmitter`, `KeyTemplateEmitter`, `EnumEmitter` fill it. Every generated member carries `[GeneratedCode]` (`CodeText.GeneratedCode`), the partial declaration never does: it merges with the user's, so it would exclude hand-written members from coverage and clash with a `[GeneratedCode]` of the user's own. The entity metadata sits in a nested `__Metadata` class, so the user's type gets no static initializer that coverage would count | One `.g.cs` per type |

`ModelBuilder.Build` runs for every matching type on every compilation change, so it stays cheap, and nothing it returns refers to a symbol or a syntax tree. An error yields a null model and no output for that type, except `DYNA020`, which `DiagnosticBag.ReportKeepingTheModel` reports beside the model of an opt-out, so that the build fails on it alone and not on every use of the type. All problems are
reported at once through `DiagnosticBag`, with places as `Spot` values. `KeyEncoding` subclasses in `Model/Encodings` are polymorphic and not a switch, because an arm that throws on a closed set is a branch no test
can reach. The same goes for dictionaries in `ConverterResolver`.

`ProjectDefaults` reads `[assembly: DynamoDbDefaults]` and `[assembly: DynamoDbDefaultConverter]` inside the transform, from the assembly that declares the type, so that `ModelBuilder.ModelOf` gives an analyzer the names a type from metadata was built with. It holds symbols and lives no longer than the transform. `NameCasing` is a copy of the rules of `JsonNamingPolicy`, which a generator on netstandard cannot call, and `NameCasingTests` holds it against them. What is wrong with these attributes is no problem of a type, so it comes from a step of its own over `CompilationProvider`, once, and that step has no item for a clean compilation.

Incrementality rests on value equality: `EquatableArray<T>` compares by content, and an equal `BuildResult` stops everything downstream. The transform of `ForAttributeWithMetadataName` is not tied to the file of the type: Roslyn combines the nodes with the compilation, so it reruns on any change, in any file, and that is what `Unchanged` in `IncrementalityTests` says, where `Cached` would mean that it did not run. So an edit of an assembly attribute reaches every model on that edit without a node of its own. Models and diagnostics take separate paths. Only the diagnostic paths combine with
`CompilationProvider`, and `DiagnosticReporter` is the only code after the transform that touches the compilation, resolving a `Spot` to a location in a real tree so that `#pragma` and `.editorconfig` work. `HintNames.FindClashes` looks at all
types together through `Collect()` and yields one value that is nearly always empty, so a new type does not touch the output of the others. `IncrementalityTests` pins this per kind of edit, and `TrackedOutputWalker`
checks that no tracked output holds a symbol or a plain array.

Emitters write text through `IndentedCode`, which holds the depth, with `\n` and never `SyntaxFactory`, so snapshots do not move with the compiler. Generated code targets C# 11, is compiled by consumers with implicit usings off, and fully qualifies every name. Its limits are in the
remarks of `KeysEmitter`. `#pragma warning disable` in the header covers `CS0612`, `CS0618`, `CS1591` and `CS8618`.

The generator stays on C# 12 and on Roslyn 5.0.0 on purpose. Consumers target net11, so the floor only has to be at or below their compiler. Dependabot ignores that package, and test code compiles against the same version.

## 8. Key grammar

`requirements.md`, section "Key grammar", is the contract and is not repeated here. Pointers:

| Topic | Code |
|---|---|
| Template syntax | `KeyTemplateParser`, whose remarks show the grammar with an example |
| Formats per type | `KeyInputCatalog`, `Parsing/KeyInputs`, `Model/Encodings` |
| Encodings, both directions | `KeyWriter`, `KeyReader`, `KeyText` |
| Compile-time rules (`DYNA100` to `DYNA120`) | `KeyTemplateBinder`, `EntityKeyBuilder`, `EmptyDefault` |
| Generated builders | `KeyBuilderEmitter`, `PlainKeyBuilderEmitter`. Which conditions exist is `KeyBuilderEmitter.IsInOrder` and `EmitRanges`, and `PlainKeyBuilderEmitter.OrderedText`, for the reasons in `api.md` section 6 |
| `KeyFormatException` | `Keys/KeyFormatException.cs`, in namespace `DynaSharp`; it names the placeholder, never the value. A generated builder catches it around its writer and throws `InTemplate(template, delimiters)`, which names the template and, for a delimiter, the delimiter set, because `KeyWriter` gets the delimiters of a call and not the text of the template. A catch costs a key that is valid nothing |
| `TryOf` and the check of `Keys.Of` | `KeyWriter`, whose `Reject` is the one place a value is turned away, and which with `throwOnInvalid: false` sets `IsInvalid` there. The generated `Encode` is the same for `Format`, `Of`, `TryOf` and `Check`, so they cannot disagree. `KeyEncoding.CanReject` says which keys have any of it |

The encoded form is an on-disk contract. `Parse` and `Format` must stay exact inverses, and changing an encoding is a data migration.

## 9. Analyzers and what they mirror

Analyzers ship in the generator dll. `DynaSharpTypes` tells what DynaSharp declares by namespace, so a new command needs no list. `docs/diagnostics.md` is the user-facing page, reached by the help link every rule has in `Descriptors`.

| Rule | Analyzer | Mirrors | Kept in step by |
|---|---|---|---|
| `DYNA026` | `DiscriminatorAnalyzer` | The generator's reading of `[Discriminator]`, on symbols, at the end of a compilation, because only that sees all entities | `DiscriminatorAnalyzerTests` |
| `DYNA032`, and `DYNA120` and `DYNA121` through `[ComputedFrom]` | `ComputedGetterAnalyzer`, on `EntityModel.ComputedInputs` through `ModelCatalog`, over the operations of the getter | `KeyInputChanges.Check`, which follows only what the attribute names, and `KeyWriter`, which refuses a delimiter where a template is strict, whatever the getter made of the value. A pass-through is `=> X`, `X.ToUpperInvariant()`, `X.ToLowerInvariant()` and `X ?? Y`, and a throw hands nothing on; a call on a member of the type is not followed | `ComputedGetterAnalyzerTests` |
| `DYNA033` | `EnumAttributeAnalyzer`, on the symbol of the enum, once, on the attribute. On a property it is the generator's, `ModelBuilder.ReportUnknownNamesThatThrow`, because only the model knows what throws | `ConverterResolver`, where a `[DynamoDbConverter]` on the enum wins over the generated converter, and `EnumRegistry.CountsUnknownAsMissing`, which a `[Flags]` enum never is. On a property, `ConverterResolver.CountsUnknownNameAsMissing`, `MissingOf` and the plain keys of `EntityKeyBuilder.KeepKeysFromMissing` | `EnumAttributeAnalyzerTests`, `TypeDiagnosticsTests` |
| `DYNA122` | `KeyValueAnalyzer`, on the generator's model through `ModelCatalog` | `KeyWriter.WriteString`, which refuses an empty value and a delimiter of the template, and the signatures that `KeysEmitter` and `KeyBuilderEmitter` write: `Keys.Of` by parameter name, a builder by position, with both bounds of `Between` and `Range` on the last placeholder | `KeyValueAnalyzerTests` |
| `DYNA201` | `CommandLifetimeAnalyzer`, `CommandFlow` | The compiler's ref struct rules, CS4007, CS4012, CS9202, CS8175, CS9108. CS4007 comes from lowering, after analyzers ran, so the flow is rebuilt on the control flow graph. CS8345 stops the compiler first and has docs only | `CommandLifetimeAnalyzerTests`, the `Mistakes` project of `PackageSmoke` |
| `DYNA202`, `DYNA234` | `ReadTerminalAnalyzer` | Which types are reads, known by the internal marker `IReadCommand` and not by a terminal, which differs between them. An internal interface at the top level of a namespace is imported from a reference, so the marker reaches a consumer's compilation. `DYNA234` takes the names of `System.Linq.AsyncEnumerable` and `System.Linq.Enumerable` from the compilation, less the members of the read itself | `ReadTerminalAnalyzerTests`, which also holds the marker to the ref structs that stream a terminal, the `Mistakes` project of `PackageSmoke` |
| `DYNA230`, `DYNA231`, `DYNA232`, `DYNA233` | `TypeArgumentAnalyzer`, on syntax: the generic names that are written, and the candidates of a call that failed to bind | The constraints of the generic verbs, by metadata name: `IKey<T>`, the three interfaces of a mapped type, `IAddableConverter` and `struct`. A type argument that was inferred comes with the candidate where the compiler's inference held; where it failed, as for `Set(handle, value)`, `Infer` reads it from the arguments. `DYNA231` leaves a type with `[DynamoDbEntity]` or `[DynamoDbDocument]` to the generator, which reports why it could not map it | `TypeArgumentAnalyzerTests`, which also pins the compiler's error beside each rule, `CompileGuardTests`, the `Mistakes` project of `PackageSmoke` |
| `DYNA203` | `DiscardedCommandAnalyzer` | The value of an expression statement, by `DynaSharpTypes.IsCommand` | `DiscardedCommandAnalyzerTests` |
| `DYNA204` | `CommandReuseAnalyzer`, `CommandFlow` | `CommandState.Enter`, which throws for a stale handle | `CommandReuseAnalyzerTests` |
| `DYNA205` | `EntityCommandAnalyzer`, on the generator's model through `ModelCatalog` | `KeyInputChanges.Check`, which throws for an input of a template of the table key, and DynamoDB, which rejects a write of a plain key, where nothing here throws. By name, the members of `IUpdatable<TSelf>` that take a handle | `EntityCommandAnalyzerTests`, `AnalyzerListTests` |
| `DYNA219` | `EntityCommandAnalyzer`, on `EntityModel.ComputedInputs` through `ModelCatalog` | `KeyInputChanges.Check`, which throws for what a computed input is derived from and for such an input with an attribute of its own, by the same members of `IUpdatable<TSelf>`, through `At` | `EntityCommandAnalyzerTests`, `AnalyzerListTests` |
| `DYNA206` | `EntityCommandAnalyzer` | `WriteState.Settle`, which throws for the same put | `EntityCommandAnalyzerTests` |
| `DYNA207` | `EntityCommandAnalyzer` | `TableExtensions.Index`, which throws for the same name | `EntityCommandAnalyzerTests` |
| `DYNA208`, `DYNA209` | `ProjectionAnalyzer`, on the generator's model through `ModelCatalog` | `MissingBehavior.Throw` and what generated reads restore from a key, `PropertyModel.OmitEmptyConverter` of a property that is not nullable, `ConverterModel.CountsAsMissing` and `ConverterModel.AnswersMissing`, which say what `CountsAsMissing` and `TryReadMissing` of a converter do at run time, `TableModel.AddRequiredNames` for which keys a read adds | `ProjectionAnalyzerTests`, and every test of the repository that projects an entity and reads it from an emulator |
| `DYNA221`, `DYNA222` | `QueryKeyAnalyzer`, on the generator's model through `ModelCatalog` | DynamoDB, which rejects a filter on a key of the read, `KeyEncoding.IsLossless` for what a key holds of a placeholder, and what an update writes of the table key, as for `DYNA209`. Which index a query reads it takes from `Index(name)` with a constant in the chain | `QueryKeyAnalyzerTests` |
| `DYNA223` | `UpdatePathAnalyzer` | `ExpressionWriter`, which sends every action as it comes, `WriteRules.SetIfNotNull` and `WriteRules.Set`, which turn a value that counts as null into nothing and into a `REMOVE`, and DynamoDB, which rejects overlapping paths | `UpdatePathAnalyzerTests` |
| `DYNA224` | `QueryKeyAnalyzer` | `TableExtensions.ThrowIfOtherAttribute`, by the attribute the builder of a key writes, `KeyModel.BuilderName` | `QueryKeyAnalyzerTests`, `QueryKeyCheckTests` for what the run time throws |
| `DYNA225` | `EntityCommandAnalyzer` | `TableSchema.Add`, by the key names of the table and of the entity included first | `EntityCommandAnalyzerTests` |
| `DYNA226` | `IndexKeyInputsAnalyzer`, on the generator's model through `ModelCatalog`, over a chain from its verb to where it is sent | `KeyInputChanges.Apply`: `Derive`, which throws for a key of a template that it has some inputs of, and `RequireSibling`, for a sparse index without its other key, unless that key has `CopyOf`, which it copies with `ExpressionWriter.CopyAttribute` and the analyzer asks `KeyTemplateEmitter.CopySource` for. An input of the table key counts as known, as `ValueInKey` gives it; an index with a removed input, one of `Add`, `SetIfNotExists` or `Delete`, a `Set` of a value that may be null, and what `DYNA205` and `DYNA219` report are left alone. `UpdatePathAnalyzer` and `EntityCommandAnalyzer` lend their reading of a chain and a handle | `IndexKeyInputsAnalyzerTests`, and the tests of `Fluent/KeyInputs` that throw, which suppress it |
| `DYNA227` | `SelectResultAnalyzer`, on the operations of the member that reads `Value` | `SelectResult<TValue>.Value`, which throws `InvalidOperationException` when `Found` is false. By member and not by flow: any `Found` of a `SelectResult` in the member, a property pattern included, keeps it quiet | `SelectResultAnalyzerTests` |
| `DYNA210`, `DYNA211`, `DYNA212` | `ExpressionAnalyzer`, `ExpressionLexer`, `ExpressionParser` | DynamoDB's condition and update grammar, and the keyword list of `ExpressionWriter` | `ExpressionCorpus`, a test comparing the two keyword lists |
| `DYNA213` | `PathAnalyzer` for a path argument, `ExpressionAnalyzer` for a bare name that a path starts with in the text, both through `AttributeCatalog` | The generator's model, through `ModelBuilder.ModelOf`, which also reads types from metadata, with the defaults of their own assembly | `PathAnalyzerTests`, `ExpressionAnalyzerTests` |
| `DYNA220` | `ExpressionAnalyzer`, `ExpressionParser`, `ReservedWords` | The 573 reserved words AWS publishes, as data, which DynamoDB rejects as a bare name at any depth of a path | `ExpressionParserTests`, `ReservedWordsTests` |
| `DYNA214` | `PathAnalyzer`, `PathSyntax` | `ExpressionWriter.AppendPath` | `PathSyntaxTests`, which runs both |
| `DYNA215` | `HandleOwnerAnalyzer` | The shape of generated `Attrs`, a static property of a nested class of a mapped type | `HandleOwnerAnalyzerTests` |
| `DYNA216` | `ExpressionAnalyzer`, on the generator's model through `ModelCatalog` | Which built-in converters store no text, the overloads of `AttrExtensions.BeginsWith` that do not compile, and `EnumModel.StoreAsNumber` for what no overload can name | `ExpressionAnalyzerTests`, `CompileGuardTests`, and `AnalyzerListTests` for the list of converters against the overloads |
| `DYNA217` | `ConflictRetryAnalyzer` | `ConflictRetry.RunAsync`, which rethrows the last conflict, and `FailureClassifier.IsConflict`, which takes it for a conflict of the retry around it. By name, any `RetryOnConflictAsync` of the library. A retry in a method that a run calls is two sightings, joined as they come, because a second semantic model is `RS1030` | `ConflictRetryAnalyzerTests`, with reflection over `DynaSharp.dll` for the types that declare the retry, and `DynamoDbClientConflictRetryTests` for what a retry that gave up throws |
| `DYNA218` | `ConflictRetryAnalyzer`, on the generator's model through `ModelCatalog`, with `SemanticModel.AnalyzeDataFlow` over the delegate, only once it holds a write with a version | `ConflictRetry.RunAsync`, which runs the same delegate again unless a constant `retryOn` leaves out `ConflictKind.ConditionalCheckFailed`, whose value the analyzer holds as a number, and `WriteRules`, which sends the version an item or `IfVersion` holds | `ConflictRetryAnalyzerTests`, which runs its snippets over a `StubHandler`: the mistake sends one body 5 times, the fix gets through on the second run |

A path parameter is known by its name, `path` or `paths`, so a new method that takes a path follows that or goes unchecked. `DYNA212` and `DYNA214` are warnings for 0.x: the grammar is a copy of the service's from 2026, and a false
error in a consumer's build can only be fixed by a new package. `ExpressionParser` accepts everything DynamoDB might, for the same reason. `WrapInAttrCodeFix` is the only class that needs Workspaces, and only an IDE loads
it; the note on `RS1038` in the generator's project file explains why that holds.

## 10. Tests and oracles

Gates: build with warnings as errors, formatting included through `IDE0055`, `dotnet format whitespace --verify-no-changes` for line endings, which the build does not see, unit tests with 100 % line and branch coverage over `DynaSharp`, `DynaSharp.Generators` and `DynaSharp.Testing` (`coverage.config`,
`build/CoverageGate.cs`), the C# blocks of both READMEs compiled against the projects (`build/ReadmeCheck.cs`), integration tests on both emulators, the AOT publish and the package smoke test. Stryker has a break threshold of 90 % and runs before a milestone closes.
The Stryker pass and the AOT publish are paused by the owner until the mandatory pass before the first push; see the decision in `tasks.md`.

A test that derives its expectation from the code proves little, so each area has an independent source of truth.

| Area | Test | Oracle |
|---|---|---|
| SigV4 | `Signing/SigV4SignerTests` | `generate_sigv4_vectors.py`, botocore `SigV4Auth` |
| Operation wire format | `Operations/*OperationTests` | `generate_operation_vectors.py`, botocore's serializer and parser |
| Answers of `StubResponse` | `Testing/StubResponse*Tests`, through a client and as spelled-out bodies | Bodies written by hand from botocore's service models and examples, and checked once against the models for unknown members, types and enum values; `Testing/RecordedRequestExpressionTests` holds the texts against requests a client really sent |
| `required` on response records | `Operations/ResponseRecordsTests` | `generate_required_members.py`, the `required` lists of botocore's service models, both ways, from each `XResponse` down through its properties |
| Shared config and credentials files | `Credentials` | `generate_shared_file_vectors.py`, botocore |
| `AssumeRole` | `Signing/SigV4SignerFormTests`, `Credentials/AssumeRoleCredentialsProviderTests`, `Credentials/ProfileChainTests` | The body botocore's sts client serializes and the `Authorization` its `SigV4Auth` adds at a fixed time, written into the tests; the role checks by reading botocore's `AssumeRoleProvider` |
| SSO | `Credentials/SsoCredentialsProviderTests`, `Credentials/SsoTokenTests`, `Credentials/ProfileChainTests` | The requests botocore's `sso` and `sso-oidc` clients send for `get_role_credentials` and `create_token`, the SHA-1 names of `SSOTokenLoader` and the saved token of `SSOTokenProvider`, written into the tests |
| Split of a `credential_process` | `Credentials/CommandLineTests` | The words of botocore's `_windows_shell_split` and `shlex.split` for the same texts, written into the test |
| Web identity wire format | `Credentials/StsVectorTests`, `Credentials/WebIdentityEndToEndTests` over a socket on loopback | `generate_sts_vectors.py`: a botocore STS client with the `query` protocol sends, parses and classifies, over a hook that answers |
| `FakeDynamoDb` | `Testing/Fake/*Tests`, through `Answer`, through a client and through the direct API | Request and response bodies written by hand from the API reference, and limits, sizes and orders from the Developer Guide; `FakeWireTests` holds the registry to every `IOperation` of the library |
| `StubSts` | `Testing/StubStsTests`, over a socket on loopback | The same vectors: its bodies equal those botocore parsed, byte for byte, and it reads back the forms botocore sent |
| Retry classification | `Retries` | botocore's `retries/standard.py`, by reading. Clock skew and expired tokens have no such source: botocore neither corrects nor refreshes on a response, so the oracle is the AWS SDKs |
| DynamoDB JSON | `Serialization` | `real_aws_responses.json`, recorded from real DynamoDB by the Parity Suite, with attribution in `NOTICE` |
| Key encodings | `Keys/KeyVectorTests`, `Key*PropertyTests`, `Keys/GuidExtensionsTests` | `generate_key_vectors.py` with Python `decimal`; properties with a fixed seed: order is preserved, parse inverts format |
| Key order against storage | Integration, `Keys` and `Mapping` | The emulators sort, so the ordering claim is checked by something else |
| Sort key conditions | `Mapping/GeneratedSortKeyConditionTests`, integration `Keys/SortKeyConditionTests` | The order of the values, in `decimal`, `DateTimeOffset` and tuples, against real keys compared by UTF-8 bytes, and the rows the emulators return |
| Command bodies | `Fluent/*Tests`, `Fluent/WriteRules`, `Fluent/KeyInputs`, `DiscriminatorWriteTests`, `CommandVectorTests` | Bodies written by hand from `api.md`, never from the writer, and the command vectors of `generate_operation_vectors.py` |
| Generated code | `Generators/Snapshots/*.verified.cs`, compiled at C# 11 and latest | The reviewed snapshot, and compile-and-run tests in `Mapping` with hand-written JSON |
| Public surface | `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt` in `DynaSharp` and `DynaSharp.Testing` | `RS0016` and `RS0017` of `PublicApiAnalyzers`, build time only. `RS0026` and `RS0027` are off in `.editorconfig`, and a pair of overloads that differ only in `T?` of a value type share a line |
| Compile guards | `Fluent/CompileGuardTests` | The compiler, asked in-process whether a misuse fails and which overload binds |
| Analyzer grammar | `Generators/Analyzers/ExpressionCorpus` | The Developer Guide, botocore's examples, DynamoDB Local 3.3.2 |
| Expression engine of the fake | `Testing/Fake/Expressions` | The same `ExpressionCorpus`, which the fake must parse or reject as DynamoDB Local does; evaluation and update tables written by hand from the Developer Guide; the word lists against the client's and the generator's |
| Behavior of the service | `tests/DynaSharp.IntegrationTests` | Dynoxide and DynamoDB Local, and DynamoDB itself on demand; the fake runs the same tests, as a client of them, not as an oracle. `Backends/Gaps.cs` names what each lacks, and such a test is skipped there |
| Patterns | `IntegrationTests/Recipes` | One test class per recipe of `patterns.md`, on both emulators and the fake |
| Native AOT | `tests/DynaSharp.AotSmoke` | The ILC: any trim or AOT warning fails the publish |
| Packaging | `tests/DynaSharp.PackageSmoke`, `Mistakes` | A real restore from the nupkg; each rule must show in a consumer's build |

The entities of the fluent tests are in `Fluent/Entities`, from the generator, so that a test meets what the entity of a consumer does, and the analyzers read them. `Fluent/HandWritten` holds what the generator does not emit: a discriminator on a key of the table, metadata without types, a key with no input and no format, and an entity that names a version and forgets to write it. `Fluent/WriteRules` and `Fluent/KeyInputs` hold one class per rule, and `WriteRulesFixture` and `KeyInputChangesFixture` what the classes of a folder share.

A test of the run-time guards makes on purpose what the analyzers report: it drops a command with `_ =`, it reuses a handle through a copy in a second variable, which `DYNA204` does not follow, it writes a key by a handle in a variable, which `DYNA205` does not follow, it nests a retry through a delegate in a variable, which `DYNA217` does not follow, it writes a stale item from a parameter, which `DYNA218` cannot tell from a new one, and it queries by a key of another attribute in a variable, which `DYNA224` does not follow. A helper of a test does not do for that, because `DynaSharpTypes` goes by the namespace and a method under `DynaSharp.IntegrationTests` counts as the library. `AnalyzerRun.ReportAsync` leaves out `DiscardedCommandAnalyzer` and `CommandReuseAnalyzer`, because a snippet of another rule is a statement whose value nothing uses.

Snapshots use `SnapshotComparer`, a local comparer: a mismatch writes `*.received.cs`, and `DYNASHARP_ACCEPT_SNAPSHOTS=1` accepts. `*.verified.*` stay LF. `KeysEmitterTests` cuts the file of `KeyShapes.KeyFormats` into a snapshot per key builder, `KeyShapes.KeyFormats.Keys.*`, and one for the rest. Allocation claims live in `CommandAllocationTests` and the benchmarks
in `benchmarks/`; `BenchmarkDotNet` builds from the process start directory, so run it from the worktree. A full `dotnet format`, which no gate runs, runs no generator that is not built, so a generated type is no entity to it, and a variable of `PartitionKey<T>` passed to `Query` is a `CS1503` it reports, which is why such code in this repository passes the key inline or through a generic helper. Neither emulator produces `UnprocessedItems`, `TransactionConflict` or several shards, so these paths are tested with stubs only.

`DYNASHARP_BACKENDS=aws` selects `Backend.RealAws`, and `.github/workflows/real-aws.yml` runs it by hand, on a role of its own. The workflow fetches the OIDC token of GitHub into a file and replaces the file while it runs, so it is IRSA end to end with no AWS SDK: `Backends/RealAws.cs` uses the default chain and region. All table names start with `Tables.Prefix`, which is what the role is limited to. A sandbox deletes its tables, the end of a run deletes what is left of them, and the start of a run deletes the tables of runs older than six hours that died. `Gaps.cs` says how each gap stands on real AWS, and has the gaps that go the other way: what only an emulator does. `SettlingHandler` holds back eventually consistent reads for a moment after a write, because the tests read right after they write, which an emulator allows. Real DynamoDB is no oracle in the gates, as nothing runs it automatically.

`Backend.Fake` (`fake`, the default of a local run, part of `all` and of the pull request pipeline) is the in-memory `FakeDynamoDb` with no container: `Backends/FakeBackend.cs` makes one fake per run, the raw clients call `FakeDynamoDb.Answer`, and `Emulator.CreateHandler` gives a client a handler of the fake where an emulator gives a `SocketsHttpHandler`. `Sandbox` and every test that watches the wire through a `DelegatingHandler` (`OperationLog`, `LostResponseClient`, the counting client of `FanOutQueryTests`) wrap that handler, so they run on every backend. A `Gaps.cs` row of the fake names only what `docs/testing.md` lists as outside the fake: a second shard of a stream, PartiQL, tags, the 16 MB cut of a batch read.

## 11. How to add

**An operation of the request API.**
1. `Requests/XRequest.cs`, `XResponse.cs`, with member names from botocore's service model.
2. `Operations/XOperation.cs`, implementing `IOperation<,>`, with shared parts in `OperationJson`.
3. `Requests/DynamoDbRequests.cs`, or `Streams/DynamoDbStreamsClient.cs`.
4. New enums in `Operations/WireNames.cs`, with `Unknown` or `Unspecified` as the zero value.
5. `tests/DynaSharp.UnitTests/Operations/generate_operation_vectors.py` and `XOperationTests.cs`; the operation in `OPERATIONS` of `generate_required_members.py`; an integration test in `IntegrationTests/Operations`.
6. `api.md` section 1 if it adds a namespace or a rule, `requirements.md` if it changes scope.

**A command for an operation.**
1. `Fluent/Commands/XCommand.cs`, a `readonly ref struct` copying the shape of `PutCommand<T>`: `Enter`, the `CommandHandle` conversion, `DebuggerText`, `ToRequestJson`, `GetAwaiter`.
2. `Fluent/Commands/XOperation.cs` for `ICommandOperation<T>`, `Fluent/Reads` for a read, or `Fluent/Actions` for `IActionOperation<T>` and several actions.
3. The verb on `Table<T>` or `Index<T>`, or in `TableExtensions` when it depends on `T`.
4. Tests by hand-written body, `CommandAllocationTests`, `CompileGuardTests`; an integration test in `IntegrationTests/Commands`.
5. `api.md`. A read implements `IReadCommand` to count as one for `DYNA202`.

**An option of a command.**
1. A read option: a property in `ReadOptions` and its output in `ReadOptions.WriteMembers`. `Release` and `CopyRead` take the struct as a whole. Any other option: a property in `CommandState`, its reset in `Release`, its output in `WriteMembers` or `WriteRequestOptions`, and for an option every command has, as `MaxAttempts` and `ReturnCapacity`, a line in `CopyRead`.
2. The clause on each command that has it, with `Enter`. An interface in `Fluent/Commands/I*.cs` if helpers should reach it.
3. A read option: the clause on `FanOutQueryCommand<T>` and all six `Mixed*Command` files.
4. A per-call setting of the loop: a member on `IWireCall<T>` and all five calls.
5. Tests, and the clause table of `api.md` section 5.

**A converter.**
1. `Serialization/Converters/XConverter.cs`, a `public readonly struct` implementing `IDynamoDbConverter<T>`. Add `IAddableConverter` for a number or set. Use `ConverterChecks.Expect`.
2. A built-in: register it in `KeyScalars`, `OtherScalars` or the collection tables of `Parsing/ConverterResolver.cs`. `KeyScalars` holds what can be a DynamoDB key, and one that does not write a string goes into `WireTypeOf` as well. One that writes text in the order of its values goes into `OrderedText` of `PlainKeyBuilderEmitter`. An opt-in one, named in `[DynamoDbConverter]` and registered nowhere, is a built-in to `WireTypeOf(PropertyFacts)` by its namespace, so one that writes a number where its type has none is named there, as `EpochSecondsConverter` and `BoolAsNumberConverter` are, and one in order goes into `OrderedText`, as `SdkDateTimeConverter` does.
3. `tests/DynaSharp.UnitTests/Serialization/Converters/XConverterTests.cs`, every wrong-type path and a counting pool for rented arrays. A built-in that refuses a value of its type on write throws an exception of its own that `ConverterChecks.IsRefusedValue` knows, and goes into `RefusingConverters` of `WriteEmitter`, so that the generated write names the property.
4. An integration case in `Support/ConverterCase.cs`, a snapshot if the emitted call differs, and the AOT smoke if it brings a new generic shape.

**An operation of the fake.**
1. `src/DynaSharp.Testing/Fake/Operations/X.cs`, a static class with `Name` and `Handle(FakeDatabase, JsonElement, Utf8JsonWriter)`. It names every member it takes in `RequestJson.Members`, reads each, and answers anything with an effect it does not implement as `FakeError.Unsupported` before it changes state.
2. Its line in `FakeWire`, out of `FakeWire.NotImplemented`; `FakeWireTests` checks every `IOperation` of the library against the two.
3. Tests in `tests/DynaSharp.UnitTests/Testing/Fake/XTests.cs` with bodies written by hand from the API reference.
4. The operation in the tables of `docs/testing.md` and the README of the package.

**A project default.** A new member of `DynamoDbDefaultsAttribute` needs a reading in `ProjectDefaults` and a place where the attribute on the type or the property still wins. It holds for a whole project, so a test of what it does at run time needs an entity in `tests/DynaSharp.DefaultsFixture`, the one project that sets the defaults.

**A key format.**
1. `requirements.md`, the format table and encodings, first. It is a data contract.
2. `Keys/KeyWriter.cs`, `KeyReader.cs`, `KeyText.cs` if it validates text.
3. `Model/Encodings/XEncoding.cs`, a `KeyEncoding` with `WriteCall`, `TryReadCall`, `IsLossless` and `CanReject`, which is false only when every value of the type fits every template and so no key of it alone has a `TryOf`; the width is what decides delimiter checks, and whether a key has ranges of it: a fixed width has to sort by value, see `KeyBuilderEmitter.IsInOrder`.
4. `Parsing/KeyInputs/*` and `Parsing/KeyInputCatalog.cs`, with `DYNA104` or `DYNA105` for what does not fit.
5. `tests/DynaSharp.UnitTests/Keys/generate_key_vectors.py` first, then `KeyVectorTests`, a `Key*PropertyTests`, a shape in `KeyShapes` with its snapshots, `BETWEEN` and `begins_with` in `IntegrationTests`.

**A diagnostic.**
1. A field in `Diagnostics/Descriptors.cs`, listed in `All`.
2. `AnalyzerReleases.Unshipped.md`, which is where every new rule starts. `DescriptorTests` compares severity, category and title.
3. Report it from `TypeRules`, `EntityKeyBuilder`, `KeyTemplateBinder` or an analyzer's `SupportedDiagnostics`.
4. A positive and a negative test in `Generators/Diagnostics` or `Generators/Analyzers`, and the `Descriptors` snapshot.
5. A help link, to the section of its family in `docs/diagnostics.md`, which names the rule: `DescriptorTests` checks both. A rule that a consumer's build must show also goes into `PackageSmoke/Mistakes` and its project file.

## 12. Glossary

| Word | Meaning |
|---|---|
| Action | One put, update, delete, check or get inside a batch or transaction |
| Carry | What the last batch response left unprocessed, as raw JSON (`ActionList.Carry`) |
| Chain, step | `TransactWriteCommand` or a `PutStep`, `UpdateStep`, `ConditionStep`: a link of a transaction |
| Claim | `AttributeClaim`, who already uses an attribute name while a type is modeled |
| Clause | One expression segment of `ExpressionWriter` (`Clause` enum). Also a fluent method that adds to a command |
| Command | A ref struct handle over a `CommandState` |
| Document, entity | A mapped type without keys, and one with keys |
| Enter, Check | Use up a handle, or only validate it (`CommandState`) |
| Fallback key | The lossless key a read parses when a stored input is missing (`PropertyModel.FallbackKey`) |
| Fan-out | One query per partition key, merged by sort key |
| Floor | Roslyn 5.0.0, the oldest compiler API the generator builds on |
| Gate | The nullable inputs a key of a sparse index needs before it is written (`KeyModel.Gates`) |
| Handle | The value a fluent call returns; valid while its version matches the state |
| Hint | In a fan-out token, the key of a partition's next item, for the merge only. In the generator, the name of a generated file |
| Hole | A `{...}` of an interpolated expression |
| Input | A property that feeds a key template |
| Index of literals | An index whose keys are literals or keys of the table, so it holds every item of the entity, and every update writes its literals (`EntityWrites.LiteralKeys`) |
| Key of literals | A template without placeholders, such as `FEATURED` |
| Marker | `TransactMarker`, which finds an action in a cancelled transaction. Also the item `Unique` writes |
| Mark | The offset where a paged body ends before its start key (`_pageMark`) |
| Oracle | An independent source of expected results |
| Owes | What a write must still do before it goes out: `OwesDiscriminator`, and "what a write owes" in `WriteRules` |
| Pending | The buffer an interpolated expression lands in before its clause is known |
| Plan | `FanOutPlan`, the partitions and key attributes of a fan-out, apart from its request |
| Recipe | A design pattern as a scenario test, not a type |
| Round, stalled | One request of a batch; one that made no progress |
| Sandbox | The test helpers in `IntegrationTests` that give each test its own table |
| Segment | A clause's buffer. In a scan, one of `TotalSegments` |
| Settle | `WriteState.Settle`, which turns what a write marked into expressions or a throw at the terminal |
| Shape | `ItemShape`, the attribute names of an `Item`, shared across items |
| Sink | `ExpressionSink`, what a condition renders into |
| Slot | `TransactSlot<T>`, where a read of `TransactGet` is found. Also an entry of `ShapeTable` |
| Spot | A file path and a span as a value, standing in for a `Location` in the generator |
| State | `CommandState`, the pooled wire form of a command |
| Template | A key template. In a fan-out, the state every partition copies |
| Terminal | The method that ends a chain: `SendAsync`, `ItemsAsync`, `ValuesAsync`, `ToRequestJson` and the like |
| View | `Table<TOther>` from `Of<T>()`, the same table seen by another type |
| Wire call | An `IWireCall<T>` struct on the pipeline |

## 13. Decisions you must not undo without reading why

Each entry names where the reasons are. "Decisions" and "Findings" are sections of `tasks.md`, and dates are its rows.

1. **Commands are ref structs over versioned pooled state.** Safety, not bytes: a plain handle over pooled state corrupts across requests. Decisions 2026-10-31, and `history/api-design.md` section 3.
2. **The request API is a sibling, and no layer stacks on another.** Decisions 2026-10-31, API decision by the owner.
3. **One serialization path.** `DynamoDbReader`, `DynamoDbWriter` and converters are the only DynamoDB-JSON code. `requirements.md`, key design rules, and the plan review at the end of Findings.
4. **The handler is the test seam, with no interface on the client or the table.** Decisions 2026-10-31.
5. **Handles, no expression trees.** Allocation and AOT. Decisions 2026-10-31, and `history/api-layers-proposal.md` for the measurements.
6. **Key values are rejected, never escaped, and `_type` is a required dedicated attribute.** Key injection, and the silent migration of a default name. Decisions 2026-10-31, key grammar rows.
7. **Batch retries are bounded by rounds without progress, and a throttle on the last attempt, or any failure after a request went through, surfaces as incomplete.** Decisions 2026-11-01, and `api.md` section 10.
8. **`TransactWrite` verbs are extension methods.** Otherwise every verb repeats on four structs. The price is a worse error, which `DYNA202` repairs. Decisions 2026-11-01.
9. **The generated entity has no seam to the key emitter.** A partial-method guard would silently drop keys. Decisions 2026-11-01.
10. **`#_type = if_not_exists(#_type, :t)`, not a plain `SET`.** An update through the wrong table must not rewrite another type. `history/generator-design.md`, amendments from the recipes.
11. **A `char` converts to a string value, with no `[Obsolete]` on the operator.** The attribute broke consumers' TUnit tests. Decisions 2026-11-01.
12. **Snapshots use a local comparer, not Verify.** Verify 33 needs a declaration only the owner can make. Decisions 2026-11-01.
13. **`HttpClient` stays, and the response stream is read synchronously.** The saving of `HttpMessageInvoker` is not worth another disposal contract. Decisions 2026-11-01.
14. **A token is client input, and signed only on request.** A key the application holds signs it, in client options, so that every instance of a service checks what another one handed out, and plain tokens stay what they were. Decisions 2026-11-01 and 2026-11-03.
15. **`DYNA212` and `DYNA214` are warnings for 0.x.** Findings, maintainability review.
16. **`src/` stays parseable by Roslyn 5.9, and the generator stays on C# 12.** Stryker compiles with that version. `AGENTS.md`, and `history/generator-design.md` section 9.
17. **No string `switch` of 7 or more cases, and no throwing arm on a closed set.** Hidden branches break the coverage gate. Findings, entry C, and the remarks of `KeyEncoding`.
18. **Pooled buffers are returned uncleared.** All of them: request and response bodies, the SigV4 buffer with the session token, and the arrays of `KeyWriter`. The threat model is in-process. The same keys, items and credentials live as immutable strings on the GC heap, where nothing can clear them, and anything that can rent from the shared pool already runs inside the process and can read that heap. The runtime does not promise that zeroing managed memory removes the data either, since the GC copies objects as it compacts. Clearing would cost on every call and protect nothing. Decisions 2026-10-31.
19. **A skew resend counts against `MaxAttempts`.** 1 must mean one request. M1b review findings, L2.
20. **`Query` by `PartitionKey<T>` is an extension method.** An extension applies only when no member does, so no call that compiled before can change what it binds to, which `CompileGuardTests` pins. The cost is a worse error on a receiver that has no `Query` at all. `api.md` section 6.

## 14. The fake

`FakeDynamoDb` runs the client's path in reverse: it reads request JSON and writes response JSON, which the library never does, so it shares the codec and nothing of the operations. Everything but the facade is internal, in `src/DynaSharp.Testing/Fake/`, namespace `DynaSharp.Testing.Fake`. It is not a hot path: `JsonDocument` for envelopes, LINQ, copies.

| Folder | Holds |
|---|---|
| `Wire` | `FakeWire`, the registry of `OperationHandler`s and the one entry: target to handler, body parsed, the handler under `FakeDatabase.Gate`, a `FakeError` rendered as the body of DynamoDB, any other exception passed through as a bug. `FakeAnswer`, `FakeError` (`ValidationException`, `SerializationException`, `ResourceNotFound`, `ResourceInUse`, `LimitExceeded`, `ConditionalCheckFailed`, `TransactionCanceled`, `IdempotentParameterMismatch`, `ExpiredIterator`, `TrimmedDataAccess`, `UnknownOperation`, `Unsupported`), `RequestJson`, `ResponseJson` |
| `Storage` | `FakeDatabase` (tables, streams, clock, gate, `IdempotencyCache` of the client request tokens, the sequence numbers), `FakeTable`, `FakeStream` with its `FakeStreamRecord`s (one shard; changes staged by `FakeTable.Put` and `Remove`, committed per request), `FakeIndex`, `ItemStore` (partition, then `SlotKey`), `KeySchema`, `ItemKey`, `KeyComparer`, `KeyRange`, `SortKeyRange`, `ItemSize`, `AttributeRules`, `Segments` |
| `Expressions` | The expression engine, below: `ExpressionTokenizer`, `ExpressionParser` and its trees, `PlaceholderScope`, `ConditionEvaluator`, `UpdateApplier`, `KeyConditionCompiler`, `Projection`, `ReservedWords`, with `DecimalNumber` and `AttributeComparer`, behind the entry points `Expressions`, `ParsedCondition`, `ParsedUpdate` and `ParsedProjection` |
| `Operations` | One static class per operation: `GetItem`, `PutItem`, `DeleteItem` with `ItemRequests` and `SingleWrite`, `Query` and `Scan` with `ReadRequest` (the members both read, and the answer), `TableOperations`, `TimeToLive`, `DescribeLimits`. `PageBuilder` cuts the lazily enumerated items of `FakeTable.Range` or `ScanAll` into a `Page` (`Limit` before the filter, 1 MB by `ItemSize`, `LastEvaluatedKey` with the index key, `Select`, fetches of a local index) and checks `ExclusiveStartKey`; it takes the filter and the projection as delegates. `ReadCapacity` is the read arithmetic and the `ConsumedCapacity` of a read. `UpdateItem` shares `SingleWrite` (options, condition, answer) and `ExpressionMembers` (the expressions and the scope of a write or a get) with `PutItem` and `DeleteItem`; `BatchGetItem`, `BatchWriteItem`, `TransactGetItems` and `TransactWriteItems` share `Batches` (lengths, one action per element, keys twice). `WriteOutcome` is what a write did, from which `WriteCapacity` (write and index units, the `ConsumedCapacity` of a write and the per-table array of a batch or a transaction, summed from `CapacityShare`s) and `WriteReturns` (`ReturnValues`, item collection metrics) answer. `WriteJournal` takes back the writes of a batch or a transaction that a later write fails. `Streams/StreamOperations` answers `ListStreams`, `DescribeStream`, `GetShardIterator` and `GetRecords`, and `Streams/ShardIterator` is the iterator, which carries its position and its time, so the fake keeps no iterators |

Rules that are easy to break:

- No member is ignored. `RequestJson.Members` names every member an object may have, nested objects too, and an option with an effect the fake does not implement is `Unsupported` before the request changes anything.
- One storage path. The wire and the direct API both go through `FakeTable.Put`, `Get` and `Remove`, which normalize with `AttributeRules`, check keys with `KeySchema`, the size with `ItemSize`, the keys of every index, and reindex. The table records go the same way: the wire reads `CreateTableRequest` and `UpdateTableRequest` and `TableOperations.Create` and `Update` check them.
- Numbers are stored in the canonical text of `DecimalNumber`, so equal numbers are equal as text, and `ItemKey` and the stores compare them by value through `AttributeComparer` anyway. Strings order by UTF-8 bytes, which `AttributeComparer.CompareStrings` gets from UTF-16 without encoding.
- The table description has one writer, `ResponseJson.WriteTableDescription`; the direct API reads it back with `TableJson.ReadTable`, so it returns what a client reads.
- All or nothing. A batch or a transaction validates every element before it writes, a transaction checks every condition on the items as they were, and a write that fails after others undoes them through `WriteJournal`; a transaction reports it as a `ValidationError` reason, a batch as the `ValidationException` itself.
- Stream records are all or nothing per request. `FakeTable.Put` and `Remove` stage every change on the enabled stream of the table, also the writes `WriteJournal` takes back, and every entry, `FakeWire.Handle`, the direct API and `TimeToLive.Expire`, ends with `FakeDatabase.CommitChanges` when it went through and `DiscardChanges` in any case. A commit drops a change whose new item equals the old one.
- The direct API throws `FakeError.ToDynamoDbException`, which renders the body and maps it through `ErrorMapper`, the mapper of the client.
- Independent of the client on purpose: the request readers, the response writers, numbers, ordering and sizes. Reused: `AttributeValue`, `Item`, `AttributeValueConverter`, `WireNames`, `TableJson` writers, `ErrorMapper`, `TableSchema`, the handler seam.

### The expression engine

`Fake/Expressions` parses and evaluates the expressions of a request with code of its own. It echoes neither the client's `ExpressionWriter` nor the generator's parser, so that a mistake in an expression the client writes fails against the fake as it would against DynamoDB. `ExpressionTokenizer` reads the text into tokens, and `ExpressionParser` builds the trees by recursive descent: `ConditionNode` for conditions, filters and key conditions, `UpdateAction` for updates, `DocumentPath` for projections. It resolves each placeholder through the `PlaceholderScope` of the request, which the operation asks once, after the last expression, for entries no expression used, and it raises what DynamoDB raises before it reads an item: syntax, reserved words, function operands, constant operand types, `BETWEEN` bounds, overlapping paths.

`ConditionEvaluator` evaluates a condition on an item, `UpdateApplier` makes the new item and, through `Projection`, the paths the update named that `UPDATED_OLD` and `UPDATED_NEW` return, `KeyConditionCompiler` turns a key condition into the `KeyRange` a read walks, and `Projection` takes paths out of an item. Numbers go through `DecimalNumber`, order and equality through `AttributeComparer`. `Expressions` is the entry the operations call. The keywords, functions and reserved words are copies, which `ExpressionTokenizerTests` and `ReservedWordsTests` keep equal to the client's and the generator's.

### How the fake is checked

Three measures, none of them derived from the fake. The integration suite runs on it as the backend `fake` (section 10). `Testing/Fake/FakeVectorTests` replays `Testing/Fake/Vectors/*.json`, sequences of requests that `build/generate_fake_vectors.py` sends with botocore to DynamoDB Local, through `FakeDynamoDb.Answer`, and compares status, code and body. The Parity Suite runs against `tools/DynaSharp.FakeServer`, an `HttpListener` on `127.0.0.1` in front of one fake, through `build/parity.sh` or `build/parity.ps1` and `.github/workflows/parity.yml` by hand.
