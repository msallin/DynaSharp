# DynaSharp - architecture

How the code is built today, for a maintainer. `requirements.md` says why and what, `api.md` is the public contract, `../docs/` the user guide (`../docs/diagnostics.md`
for the compile-time rules, `../docs/design.md` for the reasons a user sees), and `tasks.md` holds decisions and findings. Class remarks are the detail behind this document,
which points at them instead of repeating them.

## 1. Module map

| Project | Target | Role | Depends on |
|---|---|---|---|
| `src/DynaSharp` | `net11.0`, C# 14 | The library, one assembly, internal by default | Nothing at run time; the generator as a build order only |
| `src/DynaSharp.Generators` | `netstandard2.0`, C# 12 | Source generator, analyzers and two code fixes in one dll, packed into `DynaSharp` under `analyzers/dotnet/cs` | Roslyn 5.0.0, a floor (`Directory.Packages.props`); no project |
| `src/DynaSharp.Testing` | `net11.0`, C# 14 | The stubs and recorders of `api.md` section 16, `StubSts` on a socket of its own (a credentials provider's HTTP is not behind `HttpHandler`), and `FakeDynamoDb` (section 14) | `DynaSharp` and its internals (below), so its nuspec pins `DynaSharp` to `[X]`, the version it was packed with (target `PinDynaSharpVersion`) |
| `src/DynaSharp.Coordination` | `net11.0`, C# 14 | `LockTable`, `LockTableOptions`, `Lease`, `LeaseTransactExtensions`, and `Testing/` (`LockTableTesting`, `LockState`, namespace `DynaSharp.Coordination.Testing`); `api.md` section 17 | The public API of `DynaSharp` only, so its nuspec says `DynaSharp >= X` |
| `tests/DynaSharp.UnitTests` | `net11.0` | Unit, generator and analyzer tests, the only coverage source | The four projects of `src`, the generator as analyzer and as assembly |
| `tests/DynaSharp.IntegrationTests` | `net11.0` | The same tests on each backend, real AWS on demand, the recipes of `docs/patterns.md` | Docker, Testcontainers |
| `tests/DynaSharp.AotSmoke` | `net11.0` | Publishes with Native AOT and runs | The three runtime projects |
| `tests/DynaSharp.DefaultsFixture` | `net11.0` | Entities of a project with `[assembly: DynamoDbDefaults]`, which holds for a whole assembly | `DynaSharp`, the generator as analyzer |
| `tests/DynaSharp.PackageSmoke` | `net11.0` | Consumes the real nupkg; outside the solution | `artifacts/packages` |
| `benchmarks/`, `samples/` | `net11.0` | Comparison with other clients; a runnable tour | `DynaSharp` |

`DynaSharp` grants `InternalsVisibleTo` to `DynaSharp.Testing`, `DynaSharp.UnitTests`, `DynaSharp.Benchmarks` and `DynaSharp.AotSmoke`, the other projects of `src` to the
unit tests. Generated code reaches only public members, which is why `ConverterChecks`, `KeyWriter` and the like are public. `DynaSharp` packs the repository's README, the
other two packages one of their own.

**Internals `DynaSharp.Testing` uses.** `WireNames`, `EnumWireNames`, `TableJson`, `OperationJson`, `WireJson` and the `Name` of each request-API operation, for answers and
recorded requests; `WireProtocol` for the headers; `TableSchema`; the internal client constructors, for a client that reads no environment; `ClientCore.CreateOwnedHandler` for a
`RecordingHandler`; `DynamoDbStreamsClient.NextStreamChange`; `DeclaredTable.Of<T>()`; `ErrorMapper`; `CreateTableCommand.KeepsExistingTable` and `TimeToLiveAttributeName`;
`IDynamoDbDocument<T>.EntityInfo`, an `internal static virtual` that `IDynamoDbEntity<T>` implements with `Info`, for the discriminator of `Items<T>`, whose constraint is the document.

**`DynaSharp.Coordination`.** The loop of `RunAsLeaderAsync` is the internal `Leadership`, which sets `Lease.Current`, an `AsyncLocal`, around the work of a term and clears it
in its `finally`. `LockItem` is the stored format; `CoordinationTelemetry` and `CoordinationLog` the spans, counters and logs. `LockTable` keeps sightings in a
`ConcurrentDictionary` by lock name; one older than ten leases counts as none, and `SweepSightings` removes those at most once every 10 seconds on the client's clock.
`LockTableTesting` waits for a refusal through an internal hook of `LockTable`. It reads `Table<T>.Client.Options` for the `TimeProvider` and `LoggerFactory`, so `LockTable`
takes a table alone; `Client.Table(name, pk, sk)` opens the table of `LockTable.For` and `LockTable.Open`, and `Table<Item>.CreateTable()` is `LockTable.CreateTable`. It and
`DynaSharp.Testing` do not reference each other, so a test of a lock table on the fake creates it from `LockTable.CreateTable()` or with `LockTable.PartitionKeyName`.

A folder of `src/DynaSharp` is not always a namespace: `Fluent` holds the public commands in `DynaSharp` and their machinery in the internal `DynaSharp.Fluent`; `Expressions`
holds `Attr`, `Expr`, `Condition`, `SortKeyCondition` and their extensions in `DynaSharp` and the structs conditions compose from in `DynaSharp.Expressions`, with `CommandHandle`
(in `Fluent/Commands`); `PartitionKey<T>`, `Shared`, `Exceptions` with its internal `ConditionParts`, `CredentialsUnavailableException`, `DynamoDbMappingException`,
`KeyFormatException` and `Streams/Table.Streams.cs` (the mixed readings of a stream, members of the partial `Table<T>`) are in `DynaSharp`.

```text
DynamoDbClient, DynamoDbStreamsClient, Streams        public entry points
Fluent, Expressions           Requests, Operations    the two APIs, siblings
                  \           /
                   Http                               ClientCore, DynamoDbPipeline, IWireCall
     /        /       |       \        \
Signing  Credentials  Retries  Errors  Telemetry      what an attempt needs
Configuration                                         options, environment, shared files, into ClientConfiguration
Serialization, Mapping, Keys, Shared                  the codec: one reader, one writer, converters, encodings, values
Exceptions                                            what the service and the client throw
```

| Folder | Holds |
|---|---|
| `Http` | `ClientCore` (also hands out its `HttpClient`, handler and logger), `DynamoDbPipeline`, `IWireCall<T>`, `RawCall` (of `SendRawAsync`), `CallBuffers`, `BoundedPool<T>`, `ConflictRetry`, `WireProtocol` (the fixed parts of the protocol, shared with `Operations`, `Fluent` and `DynaSharp.Testing`) |
| `Operations` | One `IOperation<,>` struct per service call, `RequestCall<,,>`, `OperationCalls.ExecuteAsync` (an extension of `ClientCore` that runs one), `OperationJson`, `WireNames`, `EnumWireNames`, `TableJson`, `BatchJson`, `ReadTags`, the envelopes of the responses both APIs read, `IResponseMember` |
| `Requests` | Records of the request API and `DynamoDbRequests` |
| `Streams` | `DynamoDbStreamsClient` and its records, `StreamMerge` (the shards of `ReadStreamAsync`), `ShardRun` (retries and signal of a shard), `TableStreamExtensions`, `Table.Streams.cs`, `Change<T>`, `MixedChange<...>`, `ChangeTyping` with `IChangeTypes<TOut>` and `SingleChange<T>` |
| `Fluent` | `Table<T>`, `Index<T>`, `ItemTableExtensions`, `EntityTableExtensions`, `EntityWriteExtensions`, `TableModel`, `DiscriminatorCheck`, `TableAdministration`, `TableSchema` |
| `Fluent/Commands` | Every command and step, their interfaces, the verbs of a transaction, and what sends one item: `CommandCall`, `ICommandOperation<T>`, the write operations, `WriteResult<T>`, `FailedCondition`, `RejectedRequest` |
| `Fluent/Reads` | The read operations, `PagedRead`, `PageCall`, `PageResponse`, `Page<T>`, `ContinuationToken`, parallel scan, fan-out and mixed machinery, `FailedItem` |
| `Fluent/Actions` | `ActionCall`, `ActionList`, `BatchDriver`, `Transaction`, their operations and results |
| `Fluent/State` | `CommandState`, `WriteState`, `ReadOptions`, `CommandPool`, `ExpressionWriter` with `Clause`, `PlaceholderMaps`, `ProjectionList`, `ExpressionWords`, `WriteRules`, `EntityWrites`, `KeyInputChanges` with `KeyInputChange`, `HeldKey`, `TypedHeldKey<TKey>` |
| `Expressions` | Condition structs, `Attr`, `Expr`, `SortKeyCondition`, `ExpressionHandler` |
| `Mapping` | Attributes and the contract of section 6, `DeclaredTable`, `DiscriminatorSchema` (the one rule by which an item finds its type: `Item.Is<T>()`, a typed change, a mixed read) |
| `Serialization` | `DynamoDbReader`, `DynamoDbWriter`, converters, `PooledByteBuffer`, `WireJson`, `ItemShape`, `ShapeTable`, `NameTable`, `ArrayBuilder`, `BuilderPools`, `InlineBuffer`, `DynamoDbNumber` with `NumberFit`, the exceptions of a refused value, `JsonBridge` and `JsonValueWriter` |
| `Keys` | `KeyWriter`, `KeyReader`, `KeyText`, `KeyTimeFormat`, `PartitionKey<T>`, `KeyFormatException`, `GuidExtensions` (`Guid.FirstVersion7`, `Guid.LastVersion7`) |
| `Shared` | `AttributeValue`, `Item`, capacity types, shared enums |
| `Retries` | `RetryPolicy`, `RetryQuota`, `FailureClassifier`, `ClockSkewCorrection` |
| `Credentials` | The providers, `IRefreshableCredentialsProvider`, `RefreshingCredentialsCache`, `CredentialsHttp` (the HTTP clients of the endpoints and their one retry loop), `CredentialsLog`, `CredentialsDocument`, `StsDocument`, `CredentialsDependencies` (the seams a profile hands to its providers), `CommandLine` and `CredentialProcess`, `ProfileChain` (a profile to its provider, in botocore's order), `Sts`, `SsoToken` (the AWS CLI's token cache) |
| `Configuration` | `ClientConfiguration`, `SharedConfigFiles`, `IniFile`, the file seams `ReadTextFile` and `WriteTextFile` |
| `Signing` | `SigV4Signer` |
| `Errors` | `ErrorMapper`, `ErrorBody` |
| `Exceptions` | `DynamoDbException` and the exceptions of service and client, `TransactionFailure`, `TransactionActionFailure`, `TransactMarker`, `ConditionParts` |
| `Telemetry` | `OperationTelemetry`, `DynaSharpTelemetry`, `TelemetryNames`, `ConsumedCapacityTelemetry`, `ClientLog` |

The folders are no layers: by the types each names, the 18 folders (the root counted as one) are one strongly connected component, one assembly with folders by topic. Some edges
go one way on purpose and stay so: `Shared` uses nothing of `Fluent` (`Item.Is<T>()` asks `DiscriminatorSchema` in `Mapping`), `Exceptions` nothing of `Fluent` (`ConditionParts`
and `TransactMarker` live in `Exceptions`), `Http` knows no `IOperation` (`RequestCall` and `OperationCalls` are in `Operations`) and no `TableDrift` (`TableAdministration.LogDrift`
logs on `ClientCore.Logger`), and `Credentials` and `Telemetry` use nothing of each other (`CredentialsLog` is in `Credentials`). `src/BannedSymbols.txt` bans unsafe code and
reflection, and the analyzers fail the build on it.

## 2. The request path

Everything that reaches the wire goes through `DynamoDbPipeline.SendAsync<TCall, TResponse>`, the one retry loop; its remarks and those of `IWireCall<T>` are the reference.
`ClientCore` owns the configuration, the `HttpClient`, the pipeline and the `CommandPool`; both public clients are thin over it.

1. `call.GetTableName()`, the span and the start time, then a `CallBuffers` from a bounded pool.
2. `call.WriteBody(buffers)` once, inside the span, hashed once with SHA-256. Every attempt sends these bytes.
3. `AttemptAsync` per attempt: rent a timeout source, get credentials, take the corrected signing time, build and sign a new `HttpRequestMessage`, send with
   `ResponseHeadersRead`, read the body into `buffers.Response`, then `call.ReadResponse`, or for an error status `call.TryReadFailure` and else `MapError`.
4. On failure `Decide`: `FailureClassifier` sorts the exception, `RetryPolicy.Decide` answers, counters and warnings happen here. A retry waits on the client's `TimeProvider`.
5. After the loop `RecordResponse`, outside it so a failure there is not retried.
6. In `finally`: duration, span, buffers, then `call.Release()`, exactly once and last.

| Call | Kind | Body lives in | Who releases what |
|---|---|---|---|
| `RequestCall<TOperation, TRequest, TResponse>` | Request API | `CallBuffers.Request` | Nothing |
| `RawCall` | `SendRawAsync` | `CallBuffers.Request`, copied from the caller | Nothing |
| `CommandCall<TOperation, TResponse>` | One-item command | The `CommandState` | The call releases the state |
| `PageCall<TOperation, T>` | One page of a read | The `CommandState` | Nothing: the read releases the state |
| `ActionCall<TOperation, TResponse>` | Request of a batch or transaction | The state, or `CallBuffers` for a batch | The call if `TOperation.EndsCommand`, else `BatchDriver` |

Rules that are easy to break:

- The pipeline is generic over a struct, so a call has no delegate and no allocation of its own. A default interface member on `IWireCall<T>` would box: a new member goes to all five calls.
- `SendAsync` and `AttemptAsync` use `PoolingAsyncValueTaskMethodBuilder`, so their `ValueTask` is awaited exactly once: a terminal is never awaited twice or stored.
- `Parse` turns `JsonException`, `InvalidOperationException` and `FormatException` into an unrecognized response. `DynamoDbMappingException` derives from `Exception`, passes unchanged and is not retried. Its `Key` is filled where items are read: `GetOperation` from the request, `PageResponse` and `BatchGetOperation` through `FailedItem`, from a copy of the reader per array. With `ReadOptions.OnMappingFailure`, `PageResponse` reads on past the failed item from that copy, drops its entry from the `FanOutKeys` of a fan-out and puts the exception into `PageData.Skipped`; `PagedRead.SendAsync` hands each to the callback after the call returned, so what the callback throws is neither wrapped by `Parse` nor retried, and counts it.
- `SetActivityTags`, `SetResponseTags`, `GetConsumedCapacity` and `DescribeFailure` must not throw: the span is open.
- `TryReadFailure` lets a call take an error as its answer before `MapError`; the attempt is then a success, unseen by the retry loop, `DescribeFailure`, the span's error status and `error.type`. Only `CommandCall` answers, through the `static virtual` `ICommandOperation<T>.TryReadFailure`, which `Put`, `Update` and `Delete` override: with `CommandState.ToleratesFailedCondition` (set by `TrySendAsync`) and the code `ConditionalCheckFailedException`, it reads `Item` into a `WriteResult<T>` with `ConditionMet` false. `ReturnValueOperation<TValue, TConverter>` (`TrySendAsync` after `Return(attr)`) gives a `ReturnResult<TValue>` without a value. The request bytes are those of `SendAsync`, so `ToRequestJson` knows nothing of it.
- The catch of `SendAsync` hands every `DynamoDbException` to `call.DescribeFailure` before `Release`, while the body bytes exist: `CommandCall` explains through `RejectedRequest` and `FailedCondition`, `ActionCall` through `CanceledTransaction`, `PageCall` only explains, `RequestCall` takes the key from the `static virtual` `TOperation.GetKey`, `RawCall` does nothing. The key stays on the exception: telemetry reads the error code alone.
- `AttemptAsync` turns every `OperationCanceledException` into one of two: the caller's token cancelled stays an `OperationCanceledException` with that token; anything else (the attempt timeout, or a handler giving up on its own, as `SocketsHttpHandler.ConnectTimeout` does) becomes a `TimeoutException` around it. So `FailureClassifier` sees a handler timeout as `Timeout`, and a caller never gets a cancellation it did not ask for. Exception: `Dispose` cancels the requests in flight, which stay `OperationCanceledException`, not retried.
- Clock skew travels through a private `ClockCorrectedException` that never leaves the pipeline; the resend counts against `MaxAttempts` (decision 19) and uses no wait and no quota. `ClockSkewCorrection` is a `TimeProvider`, one per client from `ClientConfiguration.Resolve`: the pipeline signs on it, and the default chain judges expiry on it, because an expiry is a time of the service. A provider the application creates uses the local clock.
- `ExpiredTokenException` and `ExpiredToken` are permanent for `FailureClassifier`, yet `MapError` tells an `IRefreshableCredentialsProvider` to drop the request's credentials, attempt left or not; if that helps, a private `CredentialsDroppedException` makes the loop resend under the rules of clock skew, once per call, because new credentials may be as stale. The next attempt fetches as always, inside its timeout, so a failure surfaces as `CredentialsUnavailableException`. `RefreshingCredentialsCache.Drop` compares instances, so credentials replaced meanwhile stay, and forgets instead of fetching, so concurrent requests that fail together end in one flight. `DefaultCredentialsProvider` never moves from its chosen source to another.
- `CreateRequest` adds the content type as raw text, because the signature covers it as sent. The signer has no other caller and validates nothing.
- `WebIdentityCredentialsProvider` calls STS unsigned through `CredentialsHttp.GetClientFor`, which takes the proxied client, because STS may sit behind an egress proxy, unlike the link-local endpoints, except for a loopback endpoint. The token is a bearer credential, so that client follows no redirect, and `StsDocument` drops the `Message` of an STS error, which may quote the request. `ClientConfiguration.FindEndpointInEnvironment` reads `AWS_ENDPOINT_URL_STS` and `AWS_ENDPOINT_URL` for it and the client alike; an endpoint passed to its constructor goes first, checked by the same `FindEndpointProblem`.
- `StsDocument` reads with `ReadElementContentAsString`, which moves past the element, so its loop calls `Read` only for what it does not consume, or it skips every sibling that follows without whitespace.

**Retries** are AWS standard mode, tuned. `FailureClassifier` lists throttling, transient and timeout, and the remarks of `DynamoDbClientOptions.MaxAttempts` are its public
description: a change in one changes the other. `FailureClassifier.IsTransient` answers `DynamoDbException.IsTransient` from `Classify` plus the conflicts of a transaction, so
the two cannot disagree. `RetryPolicy` holds the numbers. `.MaxAttempts(n)` reaches the loop through `IWireCall<T>.MaxAttempts`, 0 meaning the client's value.

`Table<T>.RetryOnConflictAsync` is the one retry a caller asks for, above the pipeline, which never sees it: `ConflictRetry` runs a delegate, `FailureClassifier.IsConflict`
decides from the `ConflictKind` the call names whether it runs again, `RetryPolicy.GetConflictDelay` how long it waits (client's clock, the policy's jitter, nothing from the
quota). A run is any number of calls with their own attempts. A delegate with or without the run number goes into one loop as state with a static invoker, so neither allocates
a closure. `onConflict` runs inside the `catch` of a conflict another run follows, after the metric and before the wait, so what it throws replaces the conflict. The method's
remarks are the contract.

**Telemetry** (names in `TelemetryNames`, the list for users in `docs/observability.md`): one span per request (`DynamoDB.GetItem`), so a batch of three requests is three spans,
and every instrument from `OperationTelemetry`, one per client. A conflict retry has no span: the failed calls of a run have theirs under the caller's, so another would add a
level and no fact; its counters take operation and table from the exception. Logs only for what no single request sees: resolved configuration, http to a non-loopback host
(`ClientConfiguration.IsUnencryptedRemote`, once per client and once per web identity provider), retries given up, a corrected clock, credential refresh, the shards of a stream,
the drift `CreateTable().IfNotExists()` found. Never a key or a value.

**Streams.** `ReadStreamAsync` is `StreamMerge`, whose remarks hold the protocol: one task per shard, each the loop of `ReadShardAsync` with a `ShardRun`, writing into one bounded
channel of 16, completed by a count of running readers (a reader counts its children before itself) or by the first failure; the enumerator's `finally` stops what runs and waits
for it, so no reader outlives the enumeration.

- A shard has a span, unlike a conflict retry, because it is a unit of work with a lifetime no call shows. `OperationTelemetry.StartShard` puts it under the span current at the start of the enumeration and links it to the parent shard's span instead of nesting, so the trace of a stream read for days does not grow for days; the reader first clears `Activity.Current`, which flowed from the parent's task.
- Under `Latest` a shard closed at the start gets `Watch` (`StartChildrenAsync` on its own task, the children listed at the start as known), because a split in progress at the start would otherwise lose the child DynamoDB lists late, with all its descendants. A stop of a watch goes to `FailAsync`, which reports nothing once the reading stops: a watch has no span to tell a stop from a failure.
- `LaunchChild` starts a child at the trim horizon while `OpenedAt` (read from its id) says it is younger than `YoungShardAge` (23 hours), else at `AT_SEQUENCE_NUMBER` of its starting number. Not always `AT_SEQUENCE_NUMBER`, because DynamoDB Local answers it with `TrimmedDataAccessException` when records of other streams took the numbers between a shard's start and its first record; real DynamoDB is unverified (`GetShardIterator_AtTheStartingSequenceNumberOfAShard_ReadsFromItsFirstRecord` runs there).
- `ShardRun` retries a transient failure while the outage is younger than `RetryWindow` (15 minutes, the life of an iterator), inside the call that failed, so the position cannot move: `PageAsync` resends the same iterator, `IteratorAsync` the same position, `StreamMerge.DescribeAsync` the same look. A page or a look ends the outage (`Progressed`); a new iterator does not, or a shard that fails on every page would never fail. `ReadShardAsync` and the discovery pass no `ShardRun`, so their failures surface at once.
- Both idle waits (`EmptyPageDelay`, the next look for children) go through `DynamoDbStreamsClient.IdleAsync`: `Task.Delay` on the client's clock unless the internal `NextStreamChange` is set, which only `FakeDynamoDb.CreateStreamsClient` does. The reader then takes the task of the fake's next change before the call and waits for the first of it and the delay, so a write between the answer and the wait still ends it. The requests stay those DynamoDB would get; only the wait is shorter.

**Typed changes** add nothing to the reading. `ChangeTyping.Split<TTypes, TOut>` types each image and makes one change, or a removal and an insert when the type changed.
`IChangeTypes<TOut>` is an internal interface of static abstract members, so `TableStreamExtensions.ReadTypedAsync` is one generic iterator for every arity with no delegate per
record; `SingleChange<T>` and each `MixedChange<...>` ask a `DiscriminatorSchema`, so a mixed read, a typed change and `Item.Is<T>()` share one rule. The mixed readings are
members of `Table<T>` (`Streams/Table.Streams.cs`), because an extension over `Table<TTable>` cannot be called with the types of the reading alone. `MixedChange` holds the change
as `object` plus an index, because a change is a class and a field per type only adds size.

**The request API** adds nothing to the path: an `IOperation<TRequest, TResponse>` is a stateless struct with `Name`, `WriteRequest` and `ReadResponse`, and
`OperationCalls.ExecuteAsync` checks the request and sends it as a `RequestCall`, so `Http` knows calls, not operations. A response both APIs read goes through one envelope in
`Operations` (`GetEnvelope<T>`, `WriteEnvelope<T>`, `PageEnvelope`, and those of batches and transactions), which owns the loop over the members and reads what both read alike, so
a new member is added once. A member the two read in forms of their own (page items, `LastEvaluatedKey`, `Responses`, unprocessed requests) goes to an `IResponseMember` struct,
passed as a type argument so the loop calls it without a delegate. A command only locates a key or the unprocessed requests, because the next request sends them back as they are.

## 3. The command layer

### Handles and state

A command is a `readonly ref struct` of a `CommandState`, a version and a `TableModel`; `FanOutQueryCommand<T>` adds its `FanOutPlan`, a mixed command holds the command of its
`Mixed<...>` it forwards to, and `CreateTableCommand` is a class (below). The state is a pooled class holding the request as UTF-8, and it is what crosses the `await`. The
remarks of `CommandState` are the reference for the version protocol and ownership.

- `CommandState.Enter` checks the version and consumes the handle; each fluent method `Enter`s, changes the state and returns a new handle. `Check` validates and leaves the handle valid, for `ExpressionHandler`, which the compiler runs before the method it belongs to.
- `ToRequestJson` is a terminal like `SendAsync`: `Settle` and `Finish` change the state for good. Leaving the command usable would take a copy of the state with its open writer, key inputs and actions on every rendering path, more than a debug aid is worth, so the stale message names rendering.
- `Release` is the only way back to the pool; a compare-and-swap on the version lets one holder through. A new field of `CommandState` needs its reset in `Release`; a new member of `WriteState` or `ReadOptions` needs nothing, because `Release` restores their defaults.
- A command built and abandoned costs one pool entry, not a leak. An idle state keeps arrays up to 16 KB, the `CommandPool` of a client up to 64 idle states.

`TableModel` holds what never changes, pre-encoded (`BodyStart`, `KeyStart`, `SortKeyStart`), so a command copies bytes. `Table<T>` is a `TableModel` with a type; `Of<T>()`
caches one view per type. `TableModel.Writes` is an `EntityWrites` only for an entity with a version or a key template, so every other command pays one null check.

The members of `Table<T>` and `Index<T>` take generated keys only; what depends on `T` is an extension: the forms taking key values on `Table<Item>`, `Index<Item>`,
`BatchGetCommand<Item>` and `BatchWriteCommand<Item>` in `ItemTableExtensions`, those taking a `PartitionKey<T>` (which check its attribute) in `EntityTableExtensions`, the version
and `StoreKeyInputs` clauses in `EntityWriteExtensions`. Transaction steps and `TransactGetCommand.Get` take a `Table<Item>` for key values, so an analyzer reads the entity of a
step from the type of its `table` parameter (`DynaSharpTypes.TableTypeOf`), not a type argument.

### Body and expressions

Fixed parts go to `CommandState.Body` as they are known. An item goes through `DynamoDbWriter` on `BodyJson` and stays open until `Finish`, so `With` can add attributes;
`StartItem` and `CloseItem` note its bounds, so `ReturnNew()` of a put reads the item back from the body (`ReadWrittenItem<T>`), since DynamoDB has no `ALL_NEW` for a put and
what it stored is what the body holds. Expressions go to separate segments, because one call feeds a clause and two maps at once; `Finish` appends options and expressions in one
pass.

`ExpressionWriter` holds one buffer per `Clause`, a pending buffer and the maps, in three parts testable alone: `PlaceholderMaps` (a struct handed out by reference; parses paths,
gives placeholders, deduplicates by comparing bytes with no hash and no box; `AppendPath` is the path syntax that the analyzer's `PathSyntax` copies), `ProjectionList` (a
`ref struct` that keeps out a path named twice and a name a path starts with, and resolves the projection to names for the span) and `ExpressionWords` (the keywords and functions
of a condition, which repeat the generator's, and the check that an expression names an attribute).

- An interpolated expression lands in pending first, because its handler runs before the method that says its clause; `CommitPending` checks it names an attribute and joins conditions as `(a) AND (b) AND (c)`.
- `ExpressionSink.WriteGroup` flattens a chain of one operator: a struct condition cannot ask its term's type without a box, so the sink remembers where its last group starts and ends, and a term that is exactly that text loses its parentheses; a custom condition's text keeps them.
- The discriminator of an update and the keys of an index of literals use `SetMissingAttribute`, which adds nothing when an action already targets the attribute's root, because DynamoDB rejects two actions on one path and a raw action (`.SetIfNotExists("_type", ...)`) must stay the only one. A condition or a right side that reads the attribute does not count.
- A paged read writes its members once and remembers `_pageMark`; each page truncates back to it and appends a new `ExclusiveStartKey`. A transaction clears its expressions in `EndAction`, so `#n0` means something else in each action.

**Failure messages cost the failure path only.** `RejectedRequest` explains a `ValidationException` by re-reading the body as sent with `JsonDocument`, mapping placeholders to
names and value types. `FailedCondition` takes `ConditionalCheckFailedException.Key` from the body's `Key`, or the attributes of `TableModel.ItemKeyNames` in `Item`, walking forward
with `DynamoDbReader` because a failed condition is routine in a create-if-absent, so a command keeps no key that a successful write would pay for. It names the condition from
`WriteState.Conditions`, a byte where the write rules and `If` note the parts they wrote, which `Settle` leaves and `Release` clears; `FailedCondition.Line` makes the text when
`Message` is first read, so a failure code catches allocates nothing for it. `CanceledTransaction` names a failed action by position, verb (its first member in the body) and table
(from `ActionList`).

### Write rules

`WriteRules` is where a clause cannot decide alone. `Put`, `Update`, `Delete` and their transaction forms mark `WriteState` (a struct field of `CommandState`, by reference
through `CommandState.Write`), later clauses may change the verdict, and `WriteState.Settle` decides at the terminal and at the end of each transaction action (`EndAction`).

| Marked by | State | Settled as |
|---|---|---|
| `WriteItem` of a versioned entity | `UnsettledVersion`, `EntityVersion`, `StoredVersion` | `AppendVersion`. The item always holds the next version, so `IgnoreVersion` only drops the condition |
| `IfNotExists` of a put or a step | `ItemMustNotExist` | Throws if `UnsettledVersion` holds a version above 0, which no missing item can have |
| `ExpectVersion` for a write by key | `UnsettledVersion` | Throws unless `IfVersion` or `IgnoreVersion` came |
| `IfVersion(0)` of an entity with `EntityWrites.AllowsMissingVersion` | `VersionMustReadAsZero` | `AppendVersionZero` (missing or 0), unless `IfNotExists` came, which replaces it; a version from an entity decides the same way |
| `ExpectVersionOf` for `Delete(entity)` | `UnsettledVersion`, `EntityVersion` | `AppendVersion` of what the entity held |
| `ExpectUpdate` | `OwesLiteralKeys`, `OwesDiscriminator` | `SetMissingAttribute` per key of `EntityWrites.LiteralKeys`, then for the discriminator |
| `IgnoreVersion(isUpdate: true)` of a versioned entity | `OwesNextVersion` | `AdvanceMissingVersion`, `SET #v = if_not_exists(#v, :zero) + :one`, last, unless an action already changes the attribute |
| `Set`, `Remove`, `NoteUnknownValue` by handle | `KeyInputs` | `KeyInputChanges.Apply`: checks every template, writes or removes derived attributes and literal keys beside them, checks sparse siblings, copies one that is the text of an attribute. Table key values from `CommandState.ReadKey` and `DynamoDbKeyTemplate.Parse`, or from `HeldKey` for `EntityWrites.HoldsKey` |
| `StoreKeyInputs` | `StoresKeyInputs` | `KeyInputChanges.StoreTableInputs`: `SetMissingAttribute` per `EntityWrites.StoredTableInputs`, from `HeldKey` or `Parse` of the key text unless `DynamoDbKeyInput.IsCutOff`; no read, and it throws for what a key given as strings cannot give back |
| `NoteBatchWrite` | `UnsettledVersion` | Throws unless `IgnoreVersion` came. `BatchWriteOperation.AddPut` writes the next version, because a batch that is sent has `IgnoreVersion` |

The order in `Settle` matters, so each sees the actions before it: version condition, keys, stored inputs, indexes of literals, discriminator, and last the next version of an
update without the check. `EntityVersion` is cleared when the version settles, because a transaction reuses the state for its next action. `VersionChecked` makes `IgnoreVersion`
after `IfVersion` throw. Raw paths go past all of this on purpose. `api.md` section 8 is the contract.

Building a one-item command, a batch or a transaction, and reading one attribute, allocates nothing once the pool is warm, except the box of a struct entity with a version on
`Put`, boxed values and a key string on an update of a key input, and an `AttributeValue` (and a box, from a generated key) per input of an update with `StoreKeyInputs`. A
generated key an update keeps goes into a `HeldKey` that the state reuses, because the clause that wants it can come after the verb, so an update that never asks pays nothing.
`CommandAllocationTests` pins this per shape; `CommandStressTests`, `BatchStressTests`, `PagedReadStressTests` and `FanOutStressTests` hammer the pooled state.

### Table administration

`DescribeTableAsync`, `WaitUntilActiveAsync`, `DeleteTableAsync` and `CreateTableCommand` run request-API operations through `OperationCalls.ExecuteAsync`, so they have its
spans and metrics and write no JSON of their own; `TableAdministration` holds them, and the remarks of `CreateTableCommand` its contract.

- `DescribeWithinAsync` bounds one `DescribeTable` by a source on the client's `TimeProvider`, linked with the caller's token, and turns its own expiry into a `TimeoutException` naming the table.
- A wait looks at the table, its global indexes (a table's status says nothing of a backfill) and the indexes the entities declare (a list does not show what it does not hold yet). Only `CreateTableCommand` passes `unlistedIsFinal`, ending after `CatchUpLooks` looks (five over 1.5 seconds) without a declared index, because nothing but an earlier `UpdateTable` adds one there.
- `CreateTableCommand` is an ordinary class over no pooled state: it crosses `await`, can be kept, and a clause changes and returns it, because this is not a hot path. So its seam is `ToRequest`, a request-API record; `ToRequestJson` writes that record and does not end the command.
- `TableSchema` is the union of the entities, from `TableModel.Entity` and each `Including<T>`, which throws at the call on a disagreement. `ScalarAttributeType.Unknown` stays open, so a type an entity states fills it without conflict, and what stays open is a string in the request: `Table<Item>`, a hand-written type and a custom converter behave alike. Entities must agree on attribute names and index kinds; templates are not compared, because an overloaded index is several templates over one attribute.
- `IfNotExists` describes before it creates, so an existing table takes no `dynamodb:CreateTable` permission and leaves no failed call. The create loop describes what `CreateTable` found before it counts as success, because `ResourceInUseException` also answers a `DELETING` table, and creates again (five times over 1.5 seconds) when describe does not find the table, which is gone or just created elsewhere and not yet visible.
- A table found goes through `TableSchema.Check` before the wait: `InvalidOperationException` for a key that differs, else the drift of the indexes (a type compared only where both sides know it), plus stream and time to live, logged once (event 14) and returned in `CreateTableResult`.
- Time to live comes last, behind a wait of its own, because DynamoDB documents `ResourceInUseException` and `ResourceNotFoundException` for a table that is not active. It looks before it changes, because a second change within the hour is refused, and again after a `ValidationException`, which is also what the loser of two instances gets. Time to live on another attribute is left on and reported, because moving it is two changes an hour apart.

## 4. Reads

**Get.** `GetOperation<T>` reads through `GetEnvelope<T>`, which calls `T.ReadAttributes` on `Item`, so `Item`, an entity and the request API's `GetItem` go one way.
`GetValueOperation<T>` backs `ItemAsync`. Every typed read, `BatchGet` included, adds what it needs to a projection at the terminal (`TableModel.AddRequiredNames`).

**Query and Scan.** What a read does about items of other types is one `DiscriminatorCheck` per kind of read (`DiscriminatorOnGet`, `DiscriminatorOnQuery`, `DiscriminatorOnScan`): `None`, `Filter` or
`CheckEachItem`. A mixed read asks `TableModel.IsKey` itself, because it reads through a table without a schema. `BeginQuery` and `BeginScan` write the key condition and the
discriminator filter; for a key that writes itself, `BeginKeyQuery` routes it through the body and `ExpressionWriter.AppendKeyCondition` copies its names and values, which are JSON
already. `PagedRead<TOperation, T>` has the terminals and owns the state from the moment it gets it.

- `PageCall.ReadResponse` copies `LastEvaluatedKey` into `CommandState.PageKey` only after the whole response parsed, so a retry of a page sends what the first attempt sent. `PageResponse` locates the key and does not parse it.
- `Take(n)` is `ReadOptions.Take` with `Taken`: `PageLimit` writes `Limit` as the smaller of the clause and what is still to give, so no response holds more than the read gives and its key is the position after the last item given. `PagedRead.TakeAsync` gathers the responses of one `PageAsync` into one `Page<T>`.
- `OnceEnumerable<T>` makes an enumeration single-use and releases the state however it ends, also for an enumerator disposed before its first `MoveNextAsync`.

**Tokens** are client input (decision 14). `ContinuationToken.Parse` accepts a known format version, the first byte of the text (a token from before versions starts with a brace,
which is no version), and exactly one JSON object of attribute values. A client with `DynamoDbClientOptions.TokenSigner` writes version 2, `[2][key][HMAC-SHA256]`, signing
`[2][key][binding]`; `ThrowIfNotFor(state, plan)` in `PagedRead.PageAsync` and `FanOutRead.PageAsync` rebuilds the binding and checks it before `CopyKeyTo`. The binding is the
key length, `TableModel.BodyStart` (table and index), the key condition with names and values in place of placeholders (`WriteResolvedKeyCondition`), and for a fan-out the
partition keys of the plan. A signing client rejects a plain token unless this process made it (`FromKey`, `Next` of a client that does not sign). Equality is the text alone.
Without a signer the plain path reads one field more and allocates nothing more.

**One attribute.** `Select` writes the projection once (`CommandState.SelectAttribute`), because no clause can follow, and its commands have only the terminals `ValuesAsync`
and `ValueAsync`, because they give values, not items. A page reads each item through an `IItemReader<T>`, a static member of a struct: `DocumentItem<T>` is `T.ReadAttributes`,
`SelectedItem<TValue, TConverter>` the attribute, found by `AttributePath` (shared with `UpdateValueOperation`).
`ReadOptions.ChecksDiscriminator` is `CheckEachItem` where no filter kept other types away (a `Get`, a query keyed by the discriminator): a copy of the reader walks the item once
more. What a missing attribute means is `TryReadMissing` of the converter that the overload of `Select` picks: the null wrappers make it `T?`. `AttributePath` reads through
`DynamoDbReader.TryReadValue`, so a value `CountsAsMissing` counts is a missing attribute for `Select` and `Return` alike. `TableModel.ReadTypeName` is the name generated code gives
its type, so a failure reads as the entity's.

**Parallel scan.** `ParallelScan` runs one `CommandState` per segment, each on a task handing pages over a channel with room for one, so at most n requests and about 2n pages
exist; its remarks cover failures and an early end. `MixedScanCommand` shares it.

**Fan-out.** `FanOutQueryCommand<T>` holds a template state that never goes out and a `FanOutPlan`. `FanOutRead<T>` has the terminals, `FanOutMerge<T>` the k-way merge over a
heap, `FanOutCursor<T>` one partition, `FanOutKeys` the keys of held items in pooled buffers, read from the JSON. `NumberOrder` writes numbers in a byte-comparable form, so the
merge compares only bytes. `FanOutToken` is the composite position inside a `ContinuationToken`; `BoundedReads` limits concurrency. `StartPartition` copies the template with
`CopyRead`, which assigns `ReadOptions` by value, so a new read option comes along by itself. The remarks of `FanOutMerge` say why nothing reads ahead.

**Mixed reads.** `Mixed<T1, ...>` implements `IMixed<TSelf>`, an `IDynamoDbDocument<TSelf>` with an internal static abstract `Schema`, so the normal page path reads it and one
`MixedQueryCommand<TMixed>` and one `MixedScanCommand<TMixed>` serve every arity. `MixedSchema`, built once per combination, holds the `IN` filter and the required projection and
extends `DiscriminatorSchema`, whose `FindType` reads a copy of the reader to find `_type` and then lets the type read from the start. A problem with a combination is thrown at
`Of<...>()`, never from a type initializer. The mixed commands repeat the read clauses by hand, and `MixedForwardingTests` keeps them forwarding every clause. `Item.Is<T>()` applies
the same rule to an item already read and answers -1 where the wire overload throws.

**Plain JSON.** `JsonBridge` goes through the wire format both ways, so the reader and the writer stay the only DynamoDB-JSON code (decision 3). In: `JsonValueWriter` writes one
JSON value with a `DynamoDbWriter` and checks what DynamoDB refuses, so a failure names its JSONPath: numbers (`DynamoDbNumber.FitsDynamoDb`), 32 levels, and duplicate names,
hashed with FNV-1a and not `HashCode`, because a test needs two names with one hash to reach the byte comparison. `AttributeValue.FromJson` reads its own output back with
`AttributeValueConverter`, so a value from JSON has the shape of one from a response. Out: `JsonBridge.Read` writes a number raw after `DynamoDbNumber.IsJsonNumber`, or through a
`JsonElement` for an indented writer, since a raw value gets no indentation. A refused value is an `UnstorableJsonException`, which `ConverterChecks.IsRefusedValue` knows, so the
generated write names the property.

**`Item`.** `AttributeValueConverter` reads an `Item` as parallel arrays over an `ItemShape` from the process-wide `ShapeTable` of 2048 slots, so a page of alike items holds its
names once. The table never grows; its remarks say what happens when shapes outnumber the slots.

## 5. Batches and transactions

Both are commands of several actions on one state, with bytes in `Body` and offsets in `ActionList`, which also holds the distinct tables, `Carry`, the token and what a batch read
so far. `IActionOperation<TResponse>` differs from `ICommandOperation<TResponse>` in that a request can name several tables, and in `EndsCommand`.

**Batches.** `BatchDriver.SendAsync` owns the state and releases it, and cuts the actions into requests with `FindEnd` (100 keys, or 25 writes, also by bytes), each an
`ActionCall` with its own attempts.

- DynamoDB returns what it left in the form of `RequestItems`, so `Carry` holds that JSON and the next request sends it back unparsed; `GiveUp` alone parses it.
- A response replaces `Carry` only once read to the end. One that fails to read (a mapping failure, a malformed body) leaves `Carry` as sent, takes back the items it added, sets `ReadFailed` and counts as a request, so the incomplete exception reports that request as unprocessed, not what earlier rounds got through.
- Progress resets the stall count, and `MaxStalledRounds` (8) bounds the rounds in a row without progress (decision 7). `BackoffAsync` reuses the throttling backoff on the client's clock and takes nothing from the retry quota.
- A throttle on the last attempt of a request, and any failure after a request went through, become the incomplete exception with the failure inside. On the first request a failure `FailureClassifier.Classify` calls permanent applied nothing and stays raw; one it retries (throttle, timeout, lost connection, 5xx) becomes the incomplete exception with the whole batch unprocessed, because a request without an answer may have been applied in part. A cancellation always stays raw.

**Transactions.** `Transaction.AddAction` ends the action before it and starts the next, so an action stays open for its clauses, and enforces the limit of 100. The verbs are
extensions of `TransactWriteExtensions` (decision 8). `TransactWriteOperation.End` writes `ClientRequestToken` into the body once per send, which makes retries idempotent.
`TransactMarker` and `TransactSlot<T>` are action indexes. `TransactGetOperation` copies the response to its own array, because `TransactGetResult` outlives the pipeline's buffer
and parses on demand. `TableModel.ForTransactions` stands for the tables of a transaction that has no table of its own.

## 6. The mapping contract

Generated code lives in the consumer's assembly and compiles against public members of `DynaSharp`. Generator and runtime ship in one package, so the contract needs no
compatibility across versions, but every member below is reached from text in an emitter, and a rename shows up in snapshots and nowhere else. "`static virtual`, false by
default" means a struct converter compiles the question away.

| Runtime member | Who uses it |
|---|---|
| `IDynamoDbDocument<T>.WriteAttributes`, `ReadAttributes` | `Table<T>`, `DocumentConverter<T>`, `Item.From`, `To<T>`. Implemented by generated types, `Item` and `Mixed<...>` |
| `IDynamoDbDocument<T>.WriteNextVersion`, `IDynamoDbEntity<T>.ReadVersion` | `WriteRules`. `static virtual`, so a struct entity is not boxed. Emitted only for a `[Version]` |
| `Attrs.__VersionConverter` | `Select`, through `TryReadMissing`, which gives 0. Emitted only for `[Version(Missing = VersionMissing.Allow)]`, nested in `Attrs` like the forwarder of an enum |
| `IDynamoDbEntity<T>.Info`, `WriteKey` | `DynamoDbClient.CreateTable<T>`, `Get(entity)`, `Delete(entity)`, `Index(name)` |
| `IDynamoDbDiscriminated<T>` | A marker, the constraint of mixed reads, on an entity with a discriminator value |
| `IKey`, `IKey<T>` | `Keys.PrimaryKey`, which writes its key when a command asks, so a key costs no string. `ValueOf` exists only for `DynamoDbKeyTemplate.LosesValues`, and `HeldKey` keeps such a key for the terminal without a box |
| `SortKeyCondition` | The conditions of the generated key builders, over `Format`, `Prefix`, `UpperBound` and `Of` |
| `PartitionKey<T>` | The generated `Keys.X.Of`, through constructors hidden from IntelliSense (a template's string or a plain key's `AttributeValue`, each with its attribute), `EntityTableExtensions.QueryByDiscriminator`, and `ItemTableExtensions.Query`, which compares the attribute with what it reads. An entity written by hand constructs one itself, because a table of an entity takes no key given as values |
| `DynamoDbEntityInfo`, `DynamoDbIndexInfo`, `DynamoDbKeyTemplate`, `DynamoDbKeyInput` | Metadata, read into `TableModel`, `EntityWrites`, `TableSchema` (which throws for two different attributes of time to live) and `EntityTableExtensions`. `DynamoDbKeyInput.ComputedFromAttributes` tells `KeyInputChanges` that a change at or inside those attributes changes a computed input to a value only the getter knows. `ToAttributeValue` converts an input of the table key that has an attribute of its own; `IsCutOff` says the template holds less of it than the value |
| `Attr<T, TConverter>` | Generated `Attrs`. A handle is not tied to an entity: `EntityWrites.IsKeyInput` finds a template by path |
| `OmitEmptyConverter<TConverter>`, `OmitEmptyCollectionConverter<T, TElement, TConverter>`, `OmitEmptySetConverter<T, TElement, TConverter>`, `IDynamoDbConverter<T>.IsOmitted` | The handle type of a property with `[OmitEmpty]`, for `WriteRules`. `static virtual`, false by default. A collection says what it enumerates, because a list and a read-only list share no interface with a count; the set wrapper keeps `Add`. The generated write is `{ Length: > 0 }` or `{ Count: > 0 }`, plus `IsOmitted` where `ConverterResolver` finds a `[DynamoDbConverter]` that implements it. `EntityKeyBuilder` takes `[OmitEmpty]` back for a property that feeds a key, unless it is a string that feeds only keys of sparse indexes (`PropertyFacts.OmitsEmptyByItsOwnAttribute`): such an input counts as absent when empty (`PlaceholderSegment.InputCanBeAbsent`) and gates its indexes the same way. `ModelBuilder.KeepWhatAReadNeeds` takes it back for one a read throws without |
| `IDynamoDbConverter<T>.TryReadMissing` | `SelectedItem<TValue, TConverter>`. `static virtual`, false by default. The null wrappers answer null, the set converters an empty set, as `MissingBehavior` says for an entity |
| `IDynamoDbConverter<T>.CountsAsMissing`, `DynamoDbReader.TryReadAttribute<T, TConverter>` | Generated reads, and `AttributePath`. `static virtual`, false by default. A question before the read, on a reader passed by `in`, because the interface has no self type to reach `Read` from a default; the read asks, then skips or reads, so a converter cannot leave a value half read. Emitted for a `[DynamoDbConverter]` that implements it and for an enum with `UnknownEnumName.CountsAsMissing` (`ConverterModel.CountsAsMissing`; `EnumRegistry`, `EnumEmitter` and `AttrsEmitter` carry it, never for a `[Flags]` enum, which is stored as a number). A value counted as missing leaves `__set` alone, so the initial value, the throw for a `required` member and the fallback key follow as for a missing attribute. `EntityKeyBuilder` takes it back for a plain key. What reads an element (`ElementReader`, `MapEntries`) does not ask |
| `NullableHandleConverter<T, TConverter>`, `NullableReferenceHandleConverter<T, TConverter>` | The handle type of a nullable property, which `AttrsEmitter.NullableHandle()` picks from the read converter. A handle holds `T` without the `?`, and a `string?` is a `string` at run time, so this is the one place that tells such a handle apart; a signature that names a converter sees through it: the six overloads of `Select`, the `Add` overloads for `IAddableConverter`, `AttrExtensions.BeginsWith`, `DYNA216`. They reach an explicit `IsOmitted` or `CountsAsMissing` through their type parameter |
| `SetElementValueConverter<T, TElement>` | The operand of `AttrExtensions.Contains` on a set: one element as a value of its own, so an element converter needs nothing more. Public because the return type of those overloads names it |
| `DynamoDbReader`, `DynamoDbWriter`, `IDynamoDbConverter<T>`, built-in converters | Every generated read and write |
| `KeyWriter`, `KeyReader`, `KeyTimeFormat`, `KeyFormatException` | The generated key builders |
| `ConverterChecks.ExpectDiscriminator`, `TryFindString`, `DynamoDbMappingException` | Generated reads |

- `ReadAttributes` starts after the opening brace and consumes the closing one, so one method serves an item, a list element and a map. Converters are static abstract structs, so a generic instantiation gets its own code. An enum gets a `file` struct, and `Attrs` nests a public forwarder, because no public signature can name a `file` type.
- `KeyTemplates` in `Info` holds every key attribute made of properties, once: a table template without a delegate, an index template with `static values => Keys.X.Format(...)`, a plain index key, and a key of literals of any index. A plain table key is left out unless an index template repeats it. A table key whose input an index template repeats, or that has an attribute of its own, carries `Parse`; an index key that is the stored text of an attribute carries `CopyOf` (`KeyTemplateEmitter.CopySource`, `KeyEncoding.HasTextOf`). The value of an input is boxed until the terminal, the price of one delegate shape for all templates. `KeyTemplateEmitter`'s remarks explain each case.
- `Info` says how each key is stored, for `TableSchema`: a template is a string, a plain key what its converter writes (`ConverterResolver.WireTypeOf`, `KeyModel.WireType`). A custom converter tells only through `IAddableConverter`, which makes a number, so any other is `Unknown`: the generator cannot read what `Write` does.
- Writes go `WriteKey`, index keys, discriminator, attributes (snapshot `SectionFour.Shop.Order.g.verified.cs`). A versioned entity gets `__WriteAttributes(writer, entity, version)`, which both write paths call, so `WriteAttributes` stays a plain copy; the next version goes through a `checked` cast. A read keeps `var __item = reader`, a struct copy, and after the loop parses the fallback key of what is unset, so the loop does not change and only an item that lacks something is walked twice. A generated constructor taking `in __Fields` assigns `init`, `required` and private setters conditionally; `ConstructorEmitter` says why.

## 7. The generator

`DynaSharpGenerator` registers two pipelines, for entities and documents, each from `ForAttributeWithMetadataName`, and one over all entities for `IncludingAll()`. The attributes
are public types in `DynaSharp.dll`, found by metadata name in `WellKnownNames`.

| Stage | Code | Output |
|---|---|---|
| Parse | `KeyTemplateParser`: text to nodes, pure and tested on text alone | `TemplateParse` |
| Collect | `ProjectDefaults`, `TypeRules.Check`, `PropertyCollector`, `ConstructorBinder`, `ConverterResolver`, `EnumRegistry` | `PropertyFacts`, the constructor, converters |
| Bind | `KeyTemplateBinder`, `KeyInputCatalog`, `KeyInputs/*`, `EntityKeyBuilder`: placeholders to encodings, delimiters, buffer length, the rules across keys and indexes | `KeyModel`, `IndexModel`, `EntityModel` |
| Model | `ModelBuilder.Compose` | `TypeModel` in a `BuildResult` with diagnostics |
| Emit | `EntityEmitter` (the file of a type), `EntitiesEmitter` (the file of `IncludingAll()`), filled by `ConstructorEmitter`, `WriteEmitter`, `ReadEmitter`, `KeysEmitter` with `KeyBuilderEmitter`, `PlainKeyBuilderEmitter` and `AttrsEmitter`, `KeyTemplateEmitter`, `EnumEmitter` | One `.g.cs` per type |

- Every generated member carries `[GeneratedCode]`; the partial declaration never does, because it merges with the user's and would exclude hand-written members from coverage and clash with a `[GeneratedCode]` of the user's. The metadata sits in a nested `__Metadata` class, so the user's type gets no static initializer that coverage would count. Optional arguments of `DynamoDbEntityInfo` (`timeToLiveAttributeName`, `versionMissing`) are named and emitted only for a type that has them.
- `ModelBuilder.Build` runs for every matching type on every compilation change, so it stays cheap, and nothing it returns refers to a symbol or a syntax tree. An error yields no model and no output for that type, except `DYNA020`, `DYNA104` and `DYNA105`, which `DiagnosticBag.ReportKeepingTheModel` reports beside a model (of an opt-out, or with the default encoding of the type), so the build fails on them alone and not on every use of the type. All problems are reported at once, with places as `Spot` values.
- Encodings are polymorphic `KeyEncoding` subclasses and `ConverterResolver` uses dictionaries, not switches (decision 17).
- `ProjectDefaults` reads `[assembly: DynamoDbDefaults]` and `[assembly: DynamoDbDefaultConverter]` inside the transform, from the assembly that declares the type, so `ModelBuilder.ModelOf` gives an analyzer the names a type from metadata was built with; it holds symbols and lives no longer than the transform. `NameCasing` copies `JsonNamingPolicy`, which netstandard cannot call, and `NameCasingTests` holds it against it. Problems of these attributes are no problem of a type, so a step over `CompilationProvider` reports them once.

Incrementality rests on value equality: `EquatableArray<T>` compares by content, and an equal `BuildResult` stops everything downstream. The transform of
`ForAttributeWithMetadataName` reruns on any change in any file, because Roslyn combines the nodes with the compilation (`Unchanged` in `IncrementalityTests`; `Cached` would mean
it did not run), so an edit of an assembly attribute reaches every model without a node of its own. Only the diagnostic paths combine with `CompilationProvider`, and
`DiagnosticReporter` is the only code after the transform that touches the compilation, resolving a `Spot` in a real tree so `#pragma` and `.editorconfig` work.
`HintNames.FindClashes` looks at all types through `Collect()` and yields one value that is nearly always empty, so a new type does not touch the others' output.
`IncrementalityTests` pins this per kind of edit, and `TrackedOutputWalker` checks that no tracked output holds a symbol or a plain array.

`IncludingAll()` is a second `Collect()` of `RegistryBuilder.Entry` per entity, combined with `RegistryBuilder.Clash` (a type of the assembly named `DynaSharp.DynamoDbEntities`, or
null). Both are values, so the tracked step `Registry` stops an edit that changes no entity. It yields an `EntityRegistry`: the class for `EntitiesEmitter`, which writes
`DynaSharp-DynamoDbEntities.g.cs` (no type's hint name has a dash), or why there is none (`DYNA037`, `DYNA038`). The entities are in ordinal order of their full names, so the file
does not move with the order of the files, each a direct `Including<T>()`, which needs no reflection. The class is `internal` in namespace `DynaSharp` whatever the assembly is
called. The same step reports `DYNA040` (`RegistryBuilder.KeyConflicts`); the model holds no place, which would change with every edit, so the `DiagnosticInfo` carries the
metadata name of its entity and `DiagnosticReporter` finds its `[DynamoDbEntity]` when it reports, the only symbol lookup after the transform.

The declared table is read in the transform (`TableNames.OnType`, else `ProjectDefaults.Table`) and checked by `ResourceNames.Problem` (`DYNA039`), DynamoDB's naming rule, which
`EntityKeyBuilder` applies to index names too. `EntityEmitter` writes it as `{ TableName = "..." }` on `DynamoDbEntityInfo`, and `KeysEmitter` as `public const string TableName`,
with `new` where `TypeRules.HidesTableName` finds an inherited one. `DeclaredTableAnalyzer` (`DYNA238`) reads the same attributes from the symbol at a call of one of
`DeclaredTableAnalyzer.Members`, which `AnalyzerListTests` holds against the members of `DynamoDbClient` and `FakeDynamoDb` that open an entity's table without a name. At run
time `DeclaredTable.Of<T>()` gives the name or throws, and `DynamoDbClient.Table<T>()` applies `TableNaming` through `ResolveTableName`, keeping the declared name where the
function returns null or empty.

Emitters write text through `IndentedCode`, with `\n` and never `SyntaxFactory`, so snapshots do not move with the compiler. Generated code targets C# 11, compiles with implicit
usings off and fully qualifies every name; its limits are in the remarks of `KeysEmitter`. The header of an entity's file disables `CS0612`, `CS0618`, `CS1591` and `CS8618`. The
generator stays on C# 12 and Roslyn 5.0.0 (decision 16): consumers target net11, so the floor only has to be at or below their compiler. Dependabot ignores that package, and test
code compiles against the same version.

## 8. Key grammar

`requirements.md`, section "Key grammar", is the contract. Pointers:

| Topic | Code |
|---|---|
| Template syntax | `KeyTemplateParser`, whose remarks show the grammar |
| Formats per type | `KeyInputCatalog`, `Parsing/KeyInputs`, `Model/Encodings` |
| Encodings, both directions | `KeyWriter`, `KeyReader`, `KeyText` |
| Compile-time rules (`DYNA100` to `DYNA120`, `DYNA123`) | `KeyTemplateBinder`, `EntityKeyBuilder`, `EmptyDefault` |
| Generated builders | `KeyBuilderEmitter`, `PlainKeyBuilderEmitter`. Which conditions exist is `KeyBuilderEmitter.IsInOrder` and `EmitRanges`, and `PlainKeyBuilderEmitter.OrderedText`, for the reasons in `api.md` section 6 |
| `KeyFormatException` | In namespace `DynaSharp`; names the placeholder, never the value. A generated builder catches it around its writer and throws `InTemplate(template, delimiters)`, because `KeyWriter` gets the delimiters of a call, not the template. The catch costs a valid key nothing |
| `TryOf` and the check of `Keys.Of` | `KeyWriter.Reject` is the one place a value is turned away, setting `IsInvalid` with `throwOnInvalid: false`. The generated `Encode` is the same for `Format`, `Of`, `TryOf` and `Check`, so they cannot disagree. `KeyEncoding.CanReject` says which keys have any of it |

The encoded form is an on-disk contract: `Parse` and `Format` stay exact inverses, and changing an encoding is a data migration.

## 9. Analyzers and what they mirror

Analyzers ship in the generator dll. `DynaSharpTypes` tells what DynaSharp declares by namespace, so a new command needs no list. Every rule's help link in `Descriptors` points at
`docs/diagnostics.md`, which also says what each rule leaves alone ("Not reported"). "Model" means the generator's model through `ModelCatalog`. The tests of a rule are
`<Analyzer>Tests`; the last column names what else keeps it in step.

| Rule | Analyzer | Mirrors | Also kept in step by |
|---|---|---|---|
| `DYNA026` | `DiscriminatorAnalyzer`, on symbols at the end of a compilation, the only point that sees all entities | The generator's reading of `[Discriminator]` | |
| `DYNA032`, `DYNA120` and `DYNA121` through `[ComputedFrom]` | `ComputedGetterAnalyzer`, on `EntityModel.ComputedInputs`, over the operations of the getter | `KeyInputChanges.Check`, which follows only what the attribute names, and `KeyWriter`, which refuses a delimiter where a template is strict, whatever the getter made. Pass-throughs: `=> X`, `ToUpperInvariant()`, `ToLowerInvariant()`, `X ?? Y`; a call on a member of the type is not followed | |
| `DYNA033` | `EnumAttributeAnalyzer` on the enum, once; on a property the generator's `ModelBuilder.ReportUnknownNamesThatThrow`, because only the model knows what throws | `ConverterResolver` (a `[DynamoDbConverter]` on the enum wins), `EnumRegistry.CountsUnknownAsMissing`, `MissingOf`, `EntityKeyBuilder.KeepKeysFromMissing` | `TypeDiagnosticsTests` |
| `DYNA035` | The generator's `ModelBuilder.ReportNullsThatReadBackAsInitialValues`, on syntax | `ModelBuilder.MissingOf`, which keeps the current value for a nullable property, and `ConstructorEmitter.Argument`, which passes a parameter's default for a missing attribute | `TypeDiagnosticsTests` |
| `DYNA041` | The generator's `ModelBuilder.MapProperty` through `Types.IsOblivious`, beside `DYNA019` on the type | The generated write of a non-nullable property, which throws for null, and `MissingOf`, which reads it as never null | `TypeDiagnosticsTests` |
| `DYNA042` | The generator's `ConstructorBinder.Bind` | `ConstructorEmitter.Argument`, which passes a field of `__Fields`, which a `ref`, `out` or `ref readonly` parameter cannot take | `TypeDiagnosticsTests` |
| `DYNA043` | The generator's `TypeRules.CheckTakenOverNames`, on syntax with the semantic model of the tree | C#'s lookup of a simple name, which finds a member of the type first, and the members `KeysEmitter` writes (`Attrs`, `Keys`, `Indexes`, `TableName`) | `TypeDiagnosticsTests` |
| `DYNA122` | `KeyValueAnalyzer`, on the model | `KeyWriter.WriteString`, which refuses an empty value and a delimiter, and the signatures `KeysEmitter` and `KeyBuilderEmitter` write | |
| `DYNA201` | `CommandLifetimeAnalyzer`, `CommandFlow` | The compiler's ref struct rules (CS4007, CS4012, CS9202, CS8175, CS9108). CS4007 comes from lowering, after analyzers ran, so the flow is rebuilt on the control flow graph; CS8345 stops the compiler first and has docs only | `PackageSmoke/Mistakes` |
| `DYNA202`, `DYNA234` | `ReadTerminalAnalyzer` | Which types are reads, by the internal marker `IReadCommand`, not by a terminal; an internal interface at the top level of a namespace is imported from a reference, so the marker reaches a consumer's compilation. `DYNA234` takes the names of `System.Linq.AsyncEnumerable` and `Enumerable` from the compilation, less the read's own members | `PackageSmoke/Mistakes` |
| `DYNA230` to `DYNA233` | `TypeArgumentAnalyzer`, on syntax: written generic names and the candidates of a call that failed to bind | The constraints of the generic verbs, by metadata name (`IKey<T>`, the three interfaces of a mapped type, `IAddableConverter`, `struct`). `DYNA230` also covers the key-value forms of `ItemTableExtensions` on a table that holds an entity (`StoredValues`), whichever error the compiler gave; where inference failed, `Infer` reads the type argument from the arguments | `CompileGuardTests`, `PackageSmoke/Mistakes` |
| `DYNA235` | `VersionClauseAnalyzer`, on syntax and the model | `PutCommand<T>` and `PutStep`, which have no `IfVersion`, because `WriteRules.WriteItem` takes the version from the entity. Only where the compiler found no `IfVersion` | `CompileGuardTests`, `PackageSmoke/Mistakes` |
| `DYNA203` | `DiscardedCommandAnalyzer` | The value of an expression statement, by `DynaSharpTypes.IsCommand` | |
| `DYNA204` | `CommandReuseAnalyzer`, `CommandFlow` | `CommandState.Enter`, which throws for a stale handle | |
| `DYNA205` | `EntityCommandAnalyzer`, on the model | `KeyInputChanges.Check`, which throws for an input of a table key template, and DynamoDB, which rejects a write of a plain key. By name, the members of `IUpdatable<TSelf>` that take a handle | `AnalyzerListTests` |
| `DYNA219` | `EntityCommandAnalyzer`, on `EntityModel.ComputedInputs` | `KeyInputChanges.Check`, which throws for what a computed input is derived from, and for such an input with an attribute of its own | `AnalyzerListTests` |
| `DYNA206` | `EntityCommandAnalyzer` | `WriteState.Settle`, which throws for the same put | |
| `DYNA207` | `EntityCommandAnalyzer` | `EntityTableExtensions.Index`, which throws for the same name | |
| `DYNA208`, `DYNA209` | `ProjectionAnalyzer`, on the model | `MissingBehavior.Throw`, what generated reads restore from a key, `ConverterModel.CountsAsMissing` and `AnswersMissing` for the run-time converters, `TableModel.AddRequiredNames` | Every test that projects an entity and reads it from a backend |
| `DYNA221`, `DYNA222` | `QueryKeyAnalyzer`, on the model | DynamoDB, which rejects a filter on a key of the read, and `KeyEncoding.IsLossless`. The index comes from `Index(name)` with a constant | |
| `DYNA223` | `UpdatePathAnalyzer` | `ExpressionWriter`, which sends every action as it comes, `WriteRules.SetIfNotNull` and `Set`, which turn a null into nothing or a `REMOVE`, and DynamoDB, which rejects overlapping paths | |
| `DYNA224` | `QueryKeyAnalyzer` | `EntityTableExtensions.ThrowIfOtherAttribute`, by `KeyModel.BuilderName` | `QueryKeyCheckTests` |
| `DYNA225` | `EntityCommandAnalyzer` | `TableSchema.Add`, by the key names of the table and the first included entity | |
| `DYNA236` | `EntityCommandAnalyzer`, on the model | `WriteRules.NoteBatchWrite` and `WriteState.Settle`, which throws at the terminal unless `IgnoreVersion` stands anywhere in the batch. A write is a call that returns the batch, `Delete(entity)` and `DeleteAll` included | `WriteRulesBatchTests` |
| `DYNA239` | `EntityCommandAnalyzer`, on the model | `WriteRules.ExpectVersion` (an `Update` or `Delete` by key) and `WriteState.Settle`. A step is followed up to the next verb, because every update of a transaction is an `UpdateStep` | `AnalyzerListTests`, `PackageSmoke/Mistakes`, `WriteRulesUpdateTests`, `WriteRulesDeleteTests`, `WriteRulesTransactionTests` |
| `DYNA226` | `IndexKeyInputsAnalyzer`, on the model, over a chain from its verb to its send | `KeyInputChanges.Apply`: `Derive`, which throws for a template it has some inputs of, and `RequireSibling` for a sparse index without its other key, unless that key has `CopyOf` (`KeyTemplateEmitter.CopySource`). An input of the table key counts as known (`ValueInKey`) | The tests of `Fluent/KeyInputs` that throw, which suppress it |
| `DYNA227` | `SelectResultAnalyzer` | `SelectResult<TValue>.Value`, which throws when `Found` is false. By member, not flow: any `Found` in the member keeps it quiet | |
| `DYNA237` | `DateTimeKindAnalyzer`, on the model | `DateTimeConverter.Write` and `KeyWriter.WriteTimestamp`, which refuse `DateTimeKind.Unspecified`, and generated `TryOf`, which marks it invalid instead | |
| `DYNA238` | `DeclaredTableAnalyzer`, on symbols, at a call of `DeclaredTableAnalyzer.Members` | `DeclaredTable.Of<T>()`, which throws for a null `DynamoDbEntityInfo.TableName`, and the generator's reading of the table attributes (`TableNames.Of`) | `AnalyzerListTests` |
| `DYNA240` | `LeftOutEntityAnalyzer`, from `CreateTable()` up the chain to an `await`, a call of another type or an `IReturnOperation`, against the entities of the compilation and of each reference that references `DynaSharp` (walked once, only when a chain is found), grouped by `TableNames.Of`. `IncludeEntitiesCodeFix` appends `Including<T>()` by the names in the diagnostic's property `Including` | `TableSchema`, the union of `CreateTable()` and each `Including<T>()`, which is all a table gets | `IncludeEntitiesCodeFixTests` |
| `DYNA210` to `DYNA212` | `ExpressionAnalyzer`, `ExpressionLexer`, `ExpressionParser` | DynamoDB's condition and update grammar, and the keywords of `ExpressionWords` | `ExpressionCorpus`, a test comparing the keyword lists |
| `DYNA213` | `PathAnalyzer` for a path argument, `ExpressionAnalyzer` for a bare name in the text, through `AttributeCatalog` | The model through `ModelBuilder.ModelOf`, also for types from metadata | |
| `DYNA220` | `ExpressionAnalyzer`, `ExpressionParser`, `ReservedWords` | The 573 reserved words AWS publishes, which DynamoDB rejects as a bare name at any depth of a path | `ExpressionParserTests` |
| `DYNA214` | `PathAnalyzer`, `PathSyntax` | `PlaceholderMaps.AppendPath` | `PathSyntaxTests`, which runs both |
| `DYNA215` | `HandleOwnerAnalyzer` | The shape of generated `Attrs`, a static property of a nested class of a mapped type | |
| `DYNA216` | `ExpressionAnalyzer`, on the model | Which built-in converters store no text, the overloads of `AttrExtensions.BeginsWith` that do not compile, `EnumModel.StoreAsNumber` | `CompileGuardTests`, `AnalyzerListTests` |
| `DYNA217` | `ConflictRetryAnalyzer` | `ConflictRetry.RunAsync`, which rethrows the last conflict, and `FailureClassifier.IsConflict`, which takes it for a conflict of the retry around it. A retry in a called method is two sightings, joined as they come, because a second semantic model is `RS1030` | Reflection over `DynaSharp.dll` for the types that declare the retry, `ConflictRetryTests` |
| `DYNA218` | `ConflictRetryAnalyzer`, on the model, with `SemanticModel.AnalyzeDataFlow` over the delegate once it holds a versioned write | `ConflictRetry.RunAsync`, which runs the delegate again unless a constant `retryOn` leaves out `ConflictKind.ConditionalCheckFailed` (held as a number), and `WriteRules`, which sends the version an item or `IfVersion` holds | Its snippets run over a `StubHandler`: the mistake sends one body 5 times |

A path parameter is known by its name, `path` or `paths`, so a new method that takes a path follows that or goes unchecked. `DYNA212` and `DYNA214` are warnings (decision 15),
and `ExpressionParser` accepts everything DynamoDB might, for the same reason. `WrapInAttrCodeFix` and `IncludeEntitiesCodeFix` alone need Workspaces, which only an IDE loads; the
note on `RS1038` in the generator's project file explains why that holds.

## 10. Tests and oracles

Gates (`.github/workflows/ci.yml`): the build with warnings as errors and formatting (`IDE0055`); `dotnet format whitespace --verify-no-changes` for the line endings the build
does not see; unit tests at 100 % line and branch coverage over the four projects of `src` (`coverage.config`, `build/CoverageGate.cs`); `build/ReadmeCheck.cs`, which compiles the
C# blocks of the READMEs and `docs/` and the `<example>` blocks against the projects (with `Microsoft.AspNetCore.App` for a `BackgroundService` recipe, which no project of `src`
references); `build/DocCheck.cs`; integration tests on Dynoxide, DynamoDB Local and the fake; the AOT publish and run; the package smoke test. Stryker breaks below 90 % and runs
on `workflow_dispatch` only, in shards, before a milestone closes.

A test that derives its expectation from the code proves little, so each area has an independent source of truth.

| Area | Test | Oracle |
|---|---|---|
| SigV4 | `Signing/SigV4SignerTests` | `generate_sigv4_vectors.py`, botocore `SigV4Auth` |
| Operation wire format | `Operations/*OperationTests` | `generate_operation_vectors.py`, botocore's serializer and parser |
| Answers of `StubResponse` | `Testing/StubResponse*Tests` | Bodies written by hand from botocore's service models and examples; `RecordedRequestExpressionTests` holds the texts against requests a client sent |
| `required` on response records | `Operations/ResponseRecordsTests` | `generate_required_members.py`, botocore's `required` lists, both ways, down through the properties |
| Shared config and credentials files | `Credentials` | `generate_shared_file_vectors.py`, botocore |
| `AssumeRole` | `SigV4SignerFormTests`, `AssumeRoleCredentialsProviderTests`, `ProfileChainTests` | The body botocore's sts client serializes and the `Authorization` it adds at a fixed time; the role checks by reading botocore's `AssumeRoleProvider` |
| SSO | `SsoCredentialsProviderTests`, `SsoTokenTests`, `ProfileChainTests` | botocore's `sso` and `sso-oidc` requests, the SHA-1 names of `SSOTokenLoader` and the saved token of `SSOTokenProvider` |
| Split of a `credential_process` | `CommandLineTests` | botocore's `_windows_shell_split` and `shlex.split` on the same texts |
| Web identity wire format | `StsVectorTests`, `WebIdentityEndToEndTests` | `generate_sts_vectors.py`: a botocore STS client sends, parses and classifies over a hook |
| `FakeDynamoDb` | `Testing/Fake/*Tests`, through `Answer`, a client and the direct API | Bodies written by hand from the API reference, limits, sizes and orders from the Developer Guide, and the measures of section 14 |
| `StubSts` | `StubStsTests` | The STS vectors: its bodies equal what botocore parsed, byte for byte |
| Retry classification | `Retries` | botocore's `retries/standard.py`, by reading. Clock skew and expired tokens: the AWS SDKs, since botocore neither corrects nor refreshes on a response |
| DynamoDB JSON | `Serialization` | `real_aws_responses.json`, recorded from real DynamoDB by the Parity Suite, attributed in `NOTICE` |
| Key encodings | `KeyVectorTests`, `Key*PropertyTests`, `GuidExtensionsTests` | `generate_key_vectors.py` with Python `decimal`; properties: order is preserved, parse inverts format |
| Invariants over the whole domain | `*PropertyTests` with `[FsCheckProperty]`, generators in `Properties/` | An oracle per property that shares no code with the subject: write then read is the identity, `BigInteger` arithmetic, a regex of RFC 8259, SigV4 from the specification, the fake's expression engine against direct evaluation, an ordered list as the model of `Item`. A fixed number of cases from `PropertyRun.Seed`; a failure prints the step for `Replay` |
| Key order against storage | Integration, `Keys` and `Mapping` | The emulators sort, so the ordering claim is checked by something else |
| Sort key conditions | `GeneratedSortKeyConditionTests`, integration `SortKeyConditionTests` | The order of the values against real keys compared by UTF-8 bytes, and the rows the emulators return |
| Command bodies | `Fluent/*Tests`, `Fluent/WriteRules`, `Fluent/KeyInputs`, `DiscriminatorWriteTests`, `CommandVectorTests` | Bodies written by hand from `api.md`, never from the writer, and the command vectors of `generate_operation_vectors.py` |
| Generated code | `Generators/Snapshots/*.verified.cs`, compiled at C# 11 and latest | The reviewed snapshot, and compile-and-run tests in `Mapping` with hand-written JSON |
| Public surface | `PublicAPI.*.txt` of the three runtime projects | `RS0016`, `RS0017`. `RS0026` and `RS0027` are off in `.editorconfig`; overloads differing only in `T?` of a value type share a line |
| Compile guards | `Fluent/CompileGuardTests` | The compiler, asked in-process whether a misuse fails and which overload binds |
| Analyzer grammar | `ExpressionCorpus` | The Developer Guide, botocore's examples, DynamoDB Local 3.3.2 |
| Expression engine of the fake | `Testing/Fake/Expressions` | `ExpressionCorpus`, parsed or rejected as DynamoDB Local does; evaluation and update tables by hand from the Developer Guide |
| Behavior of the service | `tests/DynaSharp.IntegrationTests` | Dynoxide, DynamoDB Local, and DynamoDB on demand; the fake runs the same tests as a client, not as an oracle. `Backends/Gaps.cs` names what each backend lacks, and such a test is skipped there |
| The fake against the emulators | `IntegrationTests/Differential` | Generated sequences answered alike by the fake and the emulators, section 14 |
| Where the backends disagree | `IntegrationTests/Fidelity` | DynamoDB itself (`real-aws.yml` run 42356511937), asserted on real AWS and the fake; an emulator that answers otherwise is skipped with its answer |
| Patterns | `IntegrationTests/Recipes` | One class per recipe of `docs/patterns.md`, on every backend |
| Native AOT | `tests/DynaSharp.AotSmoke` | The ILC: any trim or AOT warning fails the publish |
| Packaging | `tests/DynaSharp.PackageSmoke`, `Mistakes` | A real restore from the nupkg; each rule must show in a consumer's build |

- `Fluent/Entities` holds generated entities, so a test meets what a consumer's entity does; `Fluent/HandWritten` what the generator does not emit (a discriminator on a table key, metadata without types, a key with no input and no format, an entity that names a version and forgets to write it). `Fluent/WriteRules` and `Fluent/KeyInputs` hold one class per rule, with `WriteRulesFixture` and `KeyInputChangesFixture`.
- A test of a run-time guard makes on purpose what an analyzer would report, in a shape the analyzer does not follow (a copy of a handle in a second variable, a key or a delegate in a variable, a stale item from a parameter). A helper does not do for that, because `DynaSharpTypes` goes by namespace and a method under `DynaSharp.IntegrationTests` counts as the library. `AnalyzerRun.ReportAsync` leaves out `DiscardedCommandAnalyzer` and `CommandReuseAnalyzer`, because a snippet of another rule is a statement whose value nothing uses.
- Snapshots use `SnapshotComparer` (decision 12): a mismatch writes `*.received.cs`, `DYNASHARP_ACCEPT_SNAPSHOTS=1` accepts, `*.verified.*` stay LF. `KeysEmitterTests` cuts `KeyShapes.KeyFormats` into a snapshot per key builder and one for the rest.
- Allocation claims live in `CommandAllocationTests` and `benchmarks/`; `BenchmarkDotNet` builds from the process start directory, so run it from the worktree.
- A full `dotnet format`, which no gate runs, runs no unbuilt generator, so a variable of `PartitionKey<T>` passed to `Query` is a `CS1503` to it; code here passes such a key inline or through a generic helper.
- No backend produces `UnprocessedItems` or `TransactionConflict` on demand, so those paths are tested with stubs; several shards come from the fake's `SplitStreamShards`.

`DYNASHARP_BACKENDS=aws` selects `Backend.RealAws`, run by hand by `real-aws.yml` on a role of its own, IRSA end to end with no AWS SDK (the workflow keeps GitHub's OIDC token in
a file). Table names start with `Tables.Prefix`, which the role is limited to; the start of a run deletes the tables of dead runs older than six hours. `SettlingHandler` holds back
eventually consistent reads for a moment after a write, because the tests read right after they write. Nothing runs real DynamoDB automatically, so it is no oracle in the gates.

`Backend.Fake` (`fake`, the local default, part of `all` and of the pull request pipeline) needs no container: `Backends/FakeBackend.cs` makes one fake per run, the raw clients
call `FakeDynamoDb.Answer`, and `Emulator.CreateHandler` gives a client the fake's handler where an emulator gives a `SocketsHttpHandler`. `Sandbox` and every test that watches the
wire through a `DelegatingHandler` wrap that handler, so they run on every backend. `Emulator.Fake` hands a test the fake itself, for what only it does on demand. What a `Gaps.cs`
row of the fake may name is a rule of section 14.

## 11. How to add

**An operation of the request API.**
1. `Requests/XRequest.cs`, `XResponse.cs`, with member names from botocore's service model.
2. `Operations/XOperation.cs`, implementing `IOperation<,>`, shared parts in `OperationJson`. A response a command reads too goes through an envelope (section 2).
3. `Requests/DynamoDbRequests.cs`, or `Streams/DynamoDbStreamsClient.cs`.
4. New enums in `Operations/WireNames.cs`, with `Unknown` or `Unspecified` as the zero value.
5. `generate_operation_vectors.py` and `XOperationTests.cs`; the operation in `OPERATIONS` of `generate_required_members.py`; an integration test in `IntegrationTests/Operations`.
6. The fake: a handler, or the name in `FakeWire.NotImplemented`; `FakeWireTests` fails until one is there.
7. `api.md` section 1 if it adds a namespace or a rule, `requirements.md` if it changes scope.

**A command for an operation.**
1. `Fluent/Commands/XCommand.cs`, a `readonly ref struct` in the shape of `PutCommand<T>`: `Enter`, the `CommandHandle` conversion, `DebuggerText`, `ToRequestJson`, `GetAwaiter`.
2. `Fluent/Commands/XOperation.cs` for `ICommandOperation<T>`, `Fluent/Reads` for a read, `Fluent/Actions` for `IActionOperation<T>`.
3. The verb on `Table<T>` or `Index<T>`, or, when it depends on `T`, in `ItemTableExtensions`, `EntityTableExtensions` or `EntityWriteExtensions` (section 3).
4. Tests by hand-written body, `CommandAllocationTests`, `CompileGuardTests`; an integration test in `IntegrationTests/Commands`.
5. `api.md`. A read implements `IReadCommand` to count as one for `DYNA202`.

**An option of a command.**
1. A read option: a property in `ReadOptions` and its output in `ReadOptions.WriteMembers`; `Release` and `CopyRead` take the struct whole. Any other: a property in `CommandState`, its reset in `Release`, its output in `WriteMembers` or `WriteRequestOptions`, and for an option every command has (`MaxAttempts`, `ReturnCapacity`) a line in `CopyRead`.
2. The clause on each command that has it, with `Enter`; an interface in `Fluent/Commands/I*.cs` if helpers should reach it.
3. A read option: the clause on `FanOutQueryCommand<T>`, `MixedQueryCommand<TMixed>` and, for a scan, `MixedScanCommand<TMixed>`.
4. A per-call setting of the loop: a member on `IWireCall<T>` and all five calls.
5. Tests, and the clause table of `api.md` section 5.

**A converter.**
1. `Serialization/Converters/XConverter.cs`, a `public readonly struct` implementing `IDynamoDbConverter<T>`, with `IAddableConverter` for a number or set. Use `ConverterChecks.Expect`.
2. A built-in: register it in `KeyScalars` (what can be a key; one that writes no string goes into `WireTypeOf` too), `OtherScalars` or the collection tables of `ConverterResolver`. One that writes text in the order of its values goes into `OrderedText` of `PlainKeyBuilderEmitter`. An opt-in one, named in `[DynamoDbConverter]` and registered nowhere, counts as built-in in `WireTypeOf(PropertyFacts)` by namespace, so one that writes a number where its type has none is named there (`EpochSecondsConverter`, `BoolAsNumberConverter`), and one in order goes into `OrderedText` (`SdkDateTimeConverter`).
3. `XConverterTests.cs`, every wrong-type path and a counting pool for rented arrays. A built-in that refuses a value on write throws an exception `ConverterChecks.IsRefusedValue` knows and goes into `RefusingConverters` of `WriteEmitter`, so the generated write names the property; `WriteEmitterTests` fails until a new converter is placed as refusing or not. A set converter writes an empty set as given, because a write by handle is DynamoDB's to reject; `ConverterResolver.Collection` wraps a set inside a list or map in `NonEmptySetConverter`, which refuses one.
4. An integration case in `Support/ConverterCase.cs`, a snapshot if the emitted call differs, the AOT smoke for a new generic shape.

**An operation of the fake.**
1. `Fake/Operations/X.cs`, a static class with `Name` and `Handle(FakeDatabase, JsonElement, Utf8JsonWriter)`. It names every member it takes in `RequestJson.Members`, reads each, and answers anything with an effect it does not implement as `FakeError.Unsupported` before it changes state.
2. Its line in `FakeWire.Handlers` (or `StreamsHandlers`), out of `FakeWire.NotImplemented`.
3. Tests in `Testing/Fake/XTests.cs` with bodies written by hand from the API reference.
4. The operation in the tables of `docs/testing.md` and the package README.

**A project default.** A new member of `DynamoDbDefaultsAttribute` needs a reading in `ProjectDefaults` and a place where the attribute on the type or property still wins. A
test of what it does at run time needs an entity in `tests/DynaSharp.DefaultsFixture`, the one project that sets the defaults.

**A key format.**
1. `requirements.md`, the format table and encodings, first: it is a data contract.
2. `Keys/KeyWriter.cs`, `KeyReader.cs`, `KeyText.cs` if it validates text.
3. `Model/Encodings/XEncoding.cs`, a `KeyEncoding` with `WriteCall`, `TryReadCall`, `IsLossless` and `CanReject` (false only when every value fits every template, so no key of it alone has a `TryOf`). The width decides the delimiter checks and the ranges: a fixed width has to sort by value (`KeyBuilderEmitter.IsInOrder`).
4. `Parsing/KeyInputs/*` and `KeyInputCatalog.cs`, with `DYNA104` or `DYNA105` for what does not fit.
5. `generate_key_vectors.py` first, then `KeyVectorTests`, a `Key*PropertyTests`, a shape in `KeyShapes` with its snapshots, `BETWEEN` and `begins_with` in `IntegrationTests`.

**A diagnostic.**
1. A field in `Diagnostics/Descriptors.cs`, listed in `All`, and the rule in `AnalyzerReleases.Unshipped.md`. `DescriptorTests` compares severity, category and title.
2. Report it from `TypeRules`, `EntityKeyBuilder`, `KeyTemplateBinder` or an analyzer's `SupportedDiagnostics`.
3. A positive and a negative test in `Generators/Diagnostics` or `Generators/Analyzers`, and the `Descriptors` snapshot.
4. A help link to the section of its family in `docs/diagnostics.md`, which names the rule; `DescriptorTests` checks both. A rule a consumer's build must show goes into `PackageSmoke/Mistakes` and its project file.

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
| Gate | The nullable inputs a key of a sparse index needs before it is written (`KeyModel.Gates`). Also `FakeDatabase.Gate`, the lock of the fake |
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

Each entry gives its reason. The dated "Decisions" rows of `tasks.md` hold the discussion where there was one.

1. **Commands are ref structs over versioned pooled state.** Safety, not bytes: the ref struct saves only 0.27 to 0.4 KB a call. A plain struct or class handle over pooled state can be copied, stored and used after the state went back to the pool and on to another request, which corrupts that request silently, and the version check catches a stale handle only at its next fluent call. A ref struct makes those bugs compile errors, which `DYNA201` explains.
2. **The request API is a sibling, and no layer stacks on another.** Commands write pooled UTF-8 with no request object or dictionary, which a layer over the records would give up, and the records stay the complete 1:1 surface of the service, which a layer over commands would narrow. Both share the pipeline, and `SendRawAsync` covers what neither models.
3. **One serialization path.** `DynamoDbReader`, `DynamoDbWriter` and the converters are the only DynamoDB-JSON code, so every quirk (N as text, B as base64, `NULL: true`, no empty sets) is implemented once and `AttributeValue`, generated code and `JsonBridge` cannot drift apart.
4. **The handler is the test seam, with no interface on the client or the table.** A test through the handler covers mapping, expressions and wire format for every API, and cannot pass on a request the client never sends, as a mock of an interface could. Code that wants a mock wraps its data access in an interface of its own.
5. **Handles, no expression trees.** Allocation and AOT: an expression tree is built per call and read by reflection at run time, which `src/` bans for Native AOT (EfficientDynamoDb's lambdas cost it 7.99 KB on an update), where a handle is a generated static property.
6. **Key values are rejected, never escaped, and `_type` is a required dedicated attribute.** Escaping hides key injection (`TENANT#{Tenant}#USER#{Id}` with a tenant holding `#USER#`), and a default discriminator of the type name would make renaming a class a silent data migration.
7. **Batch retries are bounded by rounds without progress, and a throttle on the last attempt, or any failure after a request went through, surfaces as incomplete.** A large `BatchGet` legitimately takes many rounds, so a bound on rounds could exhaust a read that progresses; and a raw throttle or failure would hide from the caller what earlier requests already wrote. `api.md` section 10 is the contract.
8. **`TransactWrite` verbs are extension methods** over `ITransactWriteChain<T>` and `ITransactWriteStep<T>`. Instance methods would repeat about 60 members on four structs. The price is a worse compiler error (`CS0311` or `CS0315` naming a verb), which `DYNA202` repairs. `ToRequestJson` is a member of each chain type instead, because every other command has one of its own, so on it alone the extension's error misled.
9. **The generated entity has no seam to the key emitter.** `WriteEmitter` and `ReadEmitter` emit the final calls into `Keys`; a partial-method guard would drop keys silently when its implementation is missing.
10. **`#_type = if_not_exists(#_type, :t)`, not a plain `SET`.** An update through the wrong table at a key held by another type would otherwise rewrite that type without a word. A new item gets the type, an existing one keeps it.
11. **A `char` converts to a string value, with no `[Obsolete]` on the operator.** TUnit's generator emits every implicit operator of a parameter type, so an `[Obsolete(error)]` there broke consumers' TUnit tests.
12. **Snapshots use a local comparer, not Verify.** Verify 33 needs a sponsorship declaration only the owner can make, and the comparer is 40 lines.
13. **`HttpClient` stays, and the response stream is read synchronously.** `HttpMessageInvoker` would save 80 B a call, not worth a different disposal contract. The synchronous `ReadAsStream` saves 72 B; the price is that a custom handler whose content builds its stream on demand can block there.
14. **A token is client input, and signed only on request.** It reaches the body as raw bytes, so `Parse` takes exactly one JSON object of attribute values. A key the application holds signs it, in client options, so every instance of a service checks what another handed out, and plain tokens stay what they were.
15. **`DYNA212` and `DYNA214` are warnings for 0.x.** The grammar and path syntax are copies of the service's from 2026, and a false error in a consumer's build can only be fixed by a new package.
16. **`src/` stays parseable by Roslyn 5.9, and the generator stays on C# 12.** Stryker compiles `src/` with Roslyn 5.9, so newer syntax fails the mutation run; `LangVersion` in each csproj enforces it. The generator references no Roslyn above its floor, in case a project Stryker compiles loads it as an analyzer.
17. **No string `switch` of 7 or more cases, and no throwing arm on a closed set.** The compiler lowers a string `switch` of 7 or more cases to a hash of the string with branches no test reaches, and an arm that throws on a closed set is a branch no test reaches; either breaks the coverage gate. Polymorphism (`KeyEncoding`) or a dictionary does the job instead.
18. **Pooled buffers are returned uncleared.** All of them: request and response bodies, the SigV4 buffer with the session token, and the arrays of `KeyWriter`. The threat model is in-process. The same keys, items and credentials live as immutable strings on the GC heap, where nothing can clear them, and anything that can rent from the shared pool already runs inside the process and can read that heap. The runtime does not promise that zeroing managed memory removes the data either, since the GC copies objects as it compacts. Clearing would cost on every call and protect nothing.
19. **A skew resend counts against `MaxAttempts`.** 1 must mean one request.
20. **`Query` by `PartitionKey<T>` is an extension method.** An extension applies only when no member does, so no call that compiled before can change what it binds to, which `CompileGuardTests` pins. The cost is a worse error on a receiver with no `Query` at all.

## 14. The fake

`FakeDynamoDb` runs the client's path in reverse: it reads request JSON and writes response JSON, which the library never does, so it shares the codec and nothing of the
operations. Everything but the facade is internal, in `src/DynaSharp.Testing/Fake/`, namespace `DynaSharp.Testing.Fake`; `api.md` section 16 is its surface, `docs/testing.md` its
scope. It is not a hot path (`JsonDocument` for envelopes, LINQ, copies), but stays AOT-safe: no reflection and no `System.Text.Json` serialization of CLR types. It has no
telemetry of its own: the client's spans and metrics run over it as over any handler.

The facade's `FakeFailures.cs` keeps the failures of `FailNext` under `FakeDatabase.Gate`; both `Answer` overloads take one before `FakeWire.Handle`, so a failed request reaches no
table. The facade holds the one `StubHandler` over `Answer` of all its clients, which `Reset` clears through the internal `StubHandler.Clear`. `CreateTable(CreateTableCommand)`
reads the command through `KeepsExistingTable` and `TimeToLiveAttributeName` and goes through `TableOperations.Create` like every other table.

| Folder | Holds |
|---|---|
| `Wire` | `FakeWire`, the registry (`Handlers`, `StreamsHandlers`, `NotImplemented`) and the one entry: target to handler, body parsed, the handler under the gate, every answer with a new `x-amzn-RequestId`, a `FakeError` rendered as DynamoDB's body, any other exception passed through as a bug, so a test fails where it happened instead of reading an HTTP 500. `FakeAnswer`, `FakeError` and its factories, `RequestJson`, `ResponseJson`, `RequestModel` (the checks of the model of a request, which DynamoDB runs before it reads the request further and reports together), `ItemShape` and `ItemLayer` (the JSON of an item, key or `ExpressionAttributeValues` before the client's reader: nameless attributes, empty sets, `NULL` false, more than 32 levels, worded by where the value stands) |
| `Storage` | `FakeDatabase` (tables, streams, clock, gate, `IdempotencyCache` of client request tokens, sequence numbers, and `NextStreamChange`, a task completed and replaced at each change of a stream), `FakeTable`, `FakeStream` with `FakeShard`s and `FakeStreamRecord`s (one shard until `Split`, then a lineage; changes staged by `FakeTable.Put` and `Remove`, committed per request into the open shard of the partition key), `FakeIndex`, `ItemStore` (partition, then `SlotKey`), `KeySchema`, `ItemKey`, `KeyComparer`, `KeyRange`, `SortKeyRange`, `ItemSize`, `AttributeRules`, `Segments` |
| `Expressions` | The expression engine (below), with `DecimalNumber` and `AttributeComparer`, behind `Expressions`, `ParsedCondition`, `ParsedUpdate` and `ParsedProjection` |
| `Operations` | One static class per operation, with what they share (`SingleWrite`, `ExpressionMembers`, `Batches`, `ReadRequest`). `PageBuilder` cuts the lazily enumerated items of `FakeTable.Range` or `ScanAll` into a `Page` (`Limit` before the filter, 1 MB by `ItemSize`, `LastEvaluatedKey` with the index key). `WriteOutcome` is what a write did, which `WriteCapacity` and `WriteReturns` answer from; `ReadCapacity` is the read arithmetic. `WriteJournal` takes back the writes of a batch or transaction that a later write fails. `Streams/ShardIterator` carries its position and time, so the fake keeps no iterators |

Rules that are easy to break:

- **Fail loudly, never differ silently.** An operation not in the registry is `UnknownOperationException` whatever the body, which is what DynamoDB Local sends and the Parity Suite takes as a skip. A member the fake does not implement inside an operation it answers is `FakeError.Unsupported`, in the namespace `DynaSharp.Testing#UnsupportedByFakeException`, so `ErrorCode` reads `UnsupportedByFakeException` and no test mistakes it for DynamoDB's answer. A value DynamoDB rejects, such as an enum value it lacks, is DynamoDB's `ValidationException`. A `ValidationException` for a request DynamoDB accepts is a bug of the fake.
- **The model first.** Every operation that takes an item or a list calls its `RequestModel.Check...` before it reads the rest, so the failures DynamoDB reports together are one message (`FakeError.Violations`), in its order and with its paths. A `Query` parses its key condition, and a `TransactWriteItems` every action, expressions before item or key, before either looks for a table. Wording and order are what the Parity Suite recorded of real DynamoDB (`tier3/validation-ordering`, `error-messages`, `limits`), which differs from DynamoDB Local in places; a vector then keeps Local's message in the file and does not compare it (`worded_otherwise` in `build/generate_fake_vectors.py`).
- **No member is ignored.** `RequestJson.Members` names every member an object may have, nested ones too, and an option with an effect the fake does not implement is `Unsupported` before the request changes anything.
- **The registry is complete.** `FakeWireTests` enumerates every `IOperation` of the library and fails until each has a handler or is in `FakeWire.NotImplemented` (empty today), so a new operation of the client forces a decision about the fake.
- **One storage path.** The wire and the direct API both go through `FakeTable.Put`, `Get` and `Remove`, which normalize with `AttributeRules`, check keys with `KeySchema`, the size with `ItemSize` and the keys of every index, and reindex. Table records go the same way: the wire reads a `CreateTableRequest` or `UpdateTableRequest`, and `TableOperations.Create` and `Update` check it. `ItemSize` is the one size, for the 400 KB and key limits, the 1 MB page, capacity, table and index sizes and stream records.
- **Numbers** are stored in the canonical text of `DecimalNumber`, not `DynamoDbNumber`, which parses into CLR types and loses digits; equal numbers are then equal as text, and `ItemKey` and the stores compare by value through `AttributeComparer` anyway. Strings order by UTF-8 bytes, which `AttributeComparer.CompareStrings` gets from UTF-16 without encoding.
- **One writer of a table description,** `ResponseJson.WriteTableDescription`; the direct API reads it back with `TableJson.ReadTable`, so it returns what a client reads.
- **One lock, no `await`.** Every `Answer` and every direct call runs under `FakeDatabase.Gate` from start to end, synchronously. A batch is therefore as atomic as a transaction, stricter than DynamoDB and not observable by a test that passes there.
- **All or nothing.** A batch or a transaction validates every element before it writes, a transaction checks every condition on the items as they were, and a write that fails after others undoes them through `WriteJournal`; a transaction reports that as a `ValidationError` reason, a batch as the `ValidationException` itself.
- **Stream records are all or nothing per request.** `FakeTable.Put` and `Remove` stage every change on the table's enabled stream, also writes `WriteJournal` takes back, and every entry (`FakeWire.Handle`, the direct API, `TimeToLive.Expire`) ends with `CommitChanges` when it went through and `DiscardChanges` in any case. A commit drops a change whose new item equals the old one.
- **A request that changes no stream completes no `NextStreamChange`,** `GetRecords` and `DescribeStream` among them, or the readers of a `CreateStreamsClient` client would wake each other without end. `EnableStream` and `DisableStream` are an `UpdateTable` with only a `StreamSpecification`; `SplitStreamShards` goes the direct path beside it.
- **The direct API throws `FakeError.ToDynamoDbException`,** which renders the body and maps it through the client's `ErrorMapper`, so a fixture fails as a client would.
- **Independent of the client on purpose:** request readers, response writers, the expression engine, numbers, ordering, sizes, capacity. The client's `ExpressionWriter`, the text renderer of `RecordedRequest` and the `ReadResponse` of the operations are the client's view, which the fake must satisfy, not echo, or it would confirm the client's mistakes. Reused: `AttributeValue`, `Item`, `AttributeValueConverter`, `WireNames`, the writers of `TableJson` and `OperationJson`, `ErrorMapper`, `TableSchema` for `CreateTable<T>`, the handler seam. Sharing the codec is safe because `real_aws_responses.json` checks it, the fake's tests use hand-written bodies, and the Parity Suite drives the fake with another SDK, so a symmetric bug cannot hide.
- **Scope.** The fake is for functional tests; the fine print the Parity Suite recorded of DynamoDB (validation order, message wording, the limits on expressions, nesting, transactions and numbers) is followed, the rest (the size an `UpdateItem` charges per clause, the 10 GB collection, the 16 MB cut) stays with the emulator-backed tests. A `Gaps.cs` row of `Backend.Fake` may only name a behaviour `docs/testing.md` lists as outside the fake (today PartiQL, tags, the 16 MB cut of a batch read); anything else that fails on the fake is a bug of the fake.

### The expression engine

`Fake/Expressions` parses and evaluates the expressions of a request with code of its own, echoing neither the client's `ExpressionWriter` nor the generator's parser, so a
mistake in an expression the client writes fails against the fake as it would against DynamoDB. `ExpressionTokenizer` makes tokens, and `ExpressionParser` builds trees by
recursive descent: `ConditionNode` for conditions, filters and key conditions, `UpdateAction` for updates, `DocumentPath` for projections. It resolves placeholders through the
request's `PlaceholderScope`, which the operation asks once, after the last expression, for unused entries (DynamoDB checks per request, not per expression), and raises what
DynamoDB raises before it reads an item: syntax, reserved words, function operands, constant operand types, `BETWEEN` bounds, overlapping paths. `ConditionEvaluator` evaluates a
condition on an item, `UpdateApplier` makes the new item and, through `Projection`, the paths `UPDATED_OLD` and `UPDATED_NEW` return, and `KeyConditionCompiler` turns a key
condition into the `KeyRange` a read walks. The keywords, functions and reserved words are copies, which `ExpressionTokenizerTests` and `ReservedWordsTests` keep equal to the
client's and the generator's.

### How the fake is checked

Four measures, none derived from the fake.

- The integration suite, on the backend `fake` (section 10).
- `IntegrationTests/Differential`: random sequences of data plane operations from a seed (`SequenceGenerator`), sent as raw JSON to the fake and an emulator at once, on fresh tables. `Outcome` makes each answer canonical (sorted members and set elements, `ConsumedCapacity` apart, unordered results as sets); the first difference ends the sequence, which `SequenceRunner` shortens to what it needs. Error codes are compared always, messages only for `ConditionalCheckFailedException` and `TransactionCanceledException`, which the fake mimics. `KnownDivergences` names per emulator what it does that DynamoDB does not; Dynoxide's missing validation is checked against DynamoDB Local and DynamoDB Local's capacity against Dynoxide, so a run of both misses neither. `DifferentialSettings` reads the `DYNASHARP_DIFF_*` knobs (seed, 120 sequences by default, length, parallelism, a report file); the pull request pipeline runs the default, `differential.yml` a long run by hand.
- `Testing/Fake/FakeVectorTests` replays `Testing/Fake/Vectors/*.json`, requests `build/generate_fake_vectors.py` sent with botocore to DynamoDB Local, through `FakeDynamoDb.Answer`, comparing status, code and body.
- The Parity Suite, against `tools/DynaSharp.FakeServer` (an `HttpListener` on `127.0.0.1` in front of one fake) through `build/parity.sh` or `build/parity.ps1`, and `parity.yml` by hand. Its score is tracked in `tasks.md`, never in the docs.
