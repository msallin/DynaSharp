# DynaSharp.Testing

Test support for [DynaSharp](https://github.com/msallin/DynaSharp). It fakes the wire, so a test needs neither DynamoDB nor a mock, and runs the real signing, mapping and expressions.

| You want to | Use |
|---|---|
| Answer with canned responses and check what was sent | [`StubHandler`](#a-client-for-a-test) |
| Run code against state that it writes and reads back | [`FakeDynamoDb`](#a-fake-dynamodb) |
| Test credentials that go through STS | [`StubSts`](#sts) |

## A client for a test

`StubHandler` answers requests and records them. `StubClient.Create` makes a client over it: region `us-east-1`, static fake credentials, one attempt per call, no attempt timeout, and nothing read from the environment or the shared AWS files. `StubClient.CreateStreams` does the same for DynamoDB Streams.

```csharp
using var stub = new StubHandler();
using var client = StubClient.Create(stub);
stub.Enqueue(StubResponse.Item(customer));

var found = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync(ct);
```

A request takes the next queued response, else the answer of the responder given to the constructor, else `DefaultResponse`, else it fails. A `StubResponse` taken by an operation it is not written for throws, naming both operations and its position in the queue. Responses left in the queue are no error; call `stub.ThrowIfResponsesLeft()` at the end of a test to check that all were taken. The second argument of `Create` changes the options:

```csharp
using var retrying = StubClient.Create(stub, options => options with { MaxAttempts = 4, TimeProvider = clock });
```

`AttemptTimeout` runs on `TimeProvider`, so with a fake clock it would time out the request the test is answering; set one only in a test of a timeout.

The timeout of `WaitUntilActiveAsync` and `WaitUntilActive()` runs on that clock too. A loop that advances the clock with `Task.Yield` until the wait ends burns the whole timeout, and the wait throws `TableNotActiveException` with answers still queued. Give the wait a timeout the loop does not reach, or advance the clock once per poll the handler has seen.

## Answers

`StubResponse` writes DynamoDB's answers from entities, so a test holds no JSON.

| For | Use |
|---|---|
| Items | `Item`, `Items`, `Count`, `Attributes`, `BatchItems`, `Unprocessed` |
| Tables | `Table`, `Table<T>`, `TableCreated`, `TableUpdated`, `TableDeleted`, `TablesListed`, `TimeToLive`, `TimeToLiveUpdated` |
| Streams | `StreamDescription`, `Shard`, `ShardIterator`, `Records`, which takes `Inserted<T>`, `Modified<T>`, `Removed<T>` and `Change` |
| Failures | `ConditionalCheckFailed`, `TransactionCanceled`, `ResourceNotFound`, `ResourceInUse`, `DynamoDbError` |
| Anything else | `Json` |

| Answer | Answers |
|---|---|
| `Item` | `GetItem` |
| `Items`, `Count` | `Query`, `Scan` |
| `Attributes`, `ConditionalCheckFailed` | `PutItem`, `UpdateItem`, `DeleteItem` |
| `TransactionCanceled` | both transactions |
| `BatchItems`, `Unprocessed` | their batch |
| Tables and streams | the one call each describes, such as `TableUpdated` for `UpdateTable` |
| `ResourceNotFound`, `ResourceInUse`, `DynamoDbError`, `Json`, `new StubResponse()` | any operation |

`TablesListed` with `lastEvaluatedTableName` is a page with more to follow. `Table<T>(name)` lists the entity's keys and indexes as active, so a wait for its indexes ends on it; a table of several entities takes `Table(name, status, indexes)`.

`StreamDescription(table, shards)` describes an `ENABLED` stream, and an enabled stream can still open shards, so `ReadStreamAsync` keeps looking for the children of a closed shard and never ends. A closed stream must say `DISABLED`: a script whose last shard closes answers `StreamDescription(table, StreamStatus.Disabled, shards)`, which ends the reading.

## What was sent

`stub.Requests` holds every request. A `RecordedRequest` has `Operation`, `Body`, `BodyText` and `BodyJson`, and reads an item or key back with `ReadItem<T>()` and `ReadKey<T>()`. `TableName`, `IndexName`, `ConsistentRead`, `ScanIndexForward` and `Limit` give those members as sent, or `null` where absent: a batch or transaction names a table per entry, and a command sends `ConsistentRead` only as `true` and `ScanIndexForward` only as `false`.

`ConditionText`, `UpdateText`, `KeyConditionText`, `FilterText` and `ProjectionText` give an expression with its placeholders resolved, or `null`.

```csharp
await client.Table<Product>("Shop").Update(Product.Keys.Of("KB-01"))
    .Set(Product.Attrs.Price, 79.00m)
    .If(Product.Attrs.Price.Eq(95.00m))
    .SendAsync(ct);

var sent = stub.Requests[^1];
Console.WriteLine(sent.ConditionText); // Price = 95.00
Console.WriteLine(sent.UpdateText);    // SET Price = 79.00, _type = if_not_exists(_type, "PRODUCT")
```

Keywords, functions and spacing stay as sent, and a placeholder missing from the maps stays as it is, so the test sees the mistake. A name appears as is, in backticks if it holds a dot or a bracket. A value reads in PartiQL notation with double quotes:

| Stored | Text |
|---|---|
| String, number | `"Placed"`, `3.10` |
| Boolean, null | `true`, `null` |
| Binary | `b"aGk="` |
| List, map | `["a", 1]`, `{"k": 1}` |
| Set | `<<"a", "b">>`, `<<1>>`, `<<b"aA==">>` |

`TransactItems` gives a transaction's steps, each a `RecordedTransactStep` with `Kind`, `TableName`, `ReadItem<T>()`, `ReadKey<T>()` and the same texts. `RecordingHandler` records the `Exchanges` of a real client. `ToRequestJson()` of a command shows the request without any client.

## A fake DynamoDB

`FakeDynamoDb` is an in-memory DynamoDB behind the same seam. A test sets up tables and items, runs the code under test through a real client, and reads the state back.

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
using var client = fake.CreateClient();

await client.Table<Customer>("Shop").Put(customer).SendAsync(ct);

var stored = fake.Get<Customer>("Shop", StubResponse.KeyOf(Customer.Keys.Of("alice")));
```

Create a table with a `CreateTable` over the wire, `CreateTable().SendAsync`, `fake.CreateTable(request)`, `fake.CreateTable<T>(name)`, which declares the entity's keys and indexes as its `CreateTable()` would, or `fake.CreateTable(name, "pk", "sk")` for the table of `client.Table(name, "pk", "sk")`, without a model, whose keys are strings, such as the table of the locks of `DynaSharp.Coordination`. Any other table is `ResourceNotFoundException`. A table and its indexes are `ACTIVE` in the answer that creates them, and `DeleteTable` frees the name at once.

A table of several entities is best created the way the application creates it, through a client of the fake. Where the application calls `CreateTable().IncludingAll()`, the method the generator writes for an assembly whose entities share the keys of one table, the test runs that same code over `fake.CreateClient()`, and the fake gets every index of every entity of that assembly, see [Every entity of an assembly](https://msallin.github.io/DynaSharp/tables.html#every-entity-of-an-assembly). `fake.CreateTable<T>(name)` declares one entity, and the test project's own `IncludingAll()` names the test project's entities, not the application's.

`Put`, `Get`, `Delete`, `Items` and their entity forms set up and read items without a client, through the checks of a request: a key that does not match the schema throws the `ValidationException` a client would get. `DescribeTable`, `TableNames` and `Reset` do the same for tables. The fake is thread-safe, holds no static state and needs no disposing: make one per test with `new`.

On a table of several entities, `Items<T>` gives the items of `T` and skips the others, as a typed `Scan()` does, so a single-table design reads back one entity at a time:

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
fake.Put("Shop", customer);
fake.Put("Shop", order);

IReadOnlyList<Customer> stored = fake.Items<Customer>("Shop");
```

An item of `T` that `T` cannot read still throws `DynamoDbMappingException`, and so does `Get<T>` for an item of another entity on that key. [What the fake does](https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does) has the rules.

`fake.CreateClient()` is `StubClient.Create(fake.CreateHandler())`, a `StubHandler` whose responder is the fake. `Requests` records what the client sent, and a response queued with `Enqueue` answers the next request instead of the fake, which never sees it: a queued throttle changes no state, as on DynamoDB.

The constructor's clock dates the tables. Time to live is stored and reported, but nothing expires by itself: `fake.ExpireItems()` runs DynamoDB's background sweep when the test says, deleting every item whose time to live attribute is a number of epoch seconds at or before that clock. An expiry more than five years before the clock is left alone, as on DynamoDB. The sweep reads tables with time to live on: `fake.CreateTable<T>` turns it on for an entity with a `[TimeToLive]` property, as `CreateTable()` of the client does, and a table of another kind gets it from an `UpdateTimeToLive`, or from `CreateTable().TimeToLive(...)` through a client of the fake.

Reads and writes behave as on DynamoDB: queries, segmented scans, projections, pages cut by `Limit` before the filter and at 1 MB with `LastEvaluatedKey` holding the index key too, conditions with `ReturnValues` and `ReturnValuesOnConditionCheckFailure`, batches, and all-or-nothing transactions with one cancellation reason per action. An optimistic lock, a create that must not overwrite, or a failed transaction condition leaves the item as it was. A `ClientRequestToken` makes a transaction idempotent for 10 minutes on the fake's clock. Consumed capacity follows the Developer Guide, indexes included. A batch never leaves unprocessed items or keys; to test a client that retries them, queue that answer on the handler.

A table with a stream records its changes in a shard, which `fake.SplitStreamShards(table)` splits, and `fake.CreateStreamsClient()` reads them through a `DynamoDbStreamsClient`, as `StubClient.CreateStreams` makes it, waiting on the clock of the fake. `fake.EnableStream(table, viewType)` and `fake.DisableStream(table)` switch the stream as an `UpdateTable` with a `StreamSpecification` does: an enable makes a new stream with a new `LatestStreamArn`, a disable closes its shards. So the code that consumes a stream is tested with the code that writes:

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
fake.EnableStream("Shop", StreamViewType.NewAndOldImages);
using var client = fake.CreateClient();
using var streams = fake.CreateStreamsClient();

var shop = client.Table<Customer>("Shop");
await shop.Put(customer).SendAsync(ct);

await foreach (var change in shop.ReadChangesAsync(streams, ShardIteratorType.TrimHorizon, cancellationToken: ct))
{
    Console.WriteLine($"{change.Kind} {change.After!.Name}"); // Insert, and the name
    break;
}
```

`ReadChangesAsync` gives the changes as entities, as on DynamoDB, and `ReadStreamAsync` the records, whose `As<T>()` types one. Code that only handles changes needs no stream at all: a test builds a `Change<T>`, a record, and a `MixedChange` with the constructor of its type, `new MixedChange<Customer, Order>(new Change<Order> { ... })`, see [Streams](https://msallin.github.io/DynaSharp/streams.html#several-entities). The shards of a disabled stream are closed, so a read of one, or of the whole stream, ends: `fake.DisableStream("Shop")` ends a reading that a test runs to its end, and `DescribeStream` says `DISABLED`.

A reader that finds nothing waits before it asks again: `EmptyPageDelay`, a second by default, and at least a second before it looks for the children of a shard again. Against DynamoDB that keeps it from polling an idle shard without pause. On a client of `CreateStreamsClient` those waits also end at the next change of a stream of the fake: a write, a split, an enable or a disable. So a test reads a write in milliseconds, a reading ends as soon as the stream is disabled, and neither the test nor the code under test sets `EmptyPageDelay` or moves a clock for it. The reader sends the requests it sends to DynamoDB, an empty page, a wait and the next page; only the wait is shorter. A child that `secondChildListedAfter` lists late and a retry after a failure of `FailNext` still wait on the clock, which the test moves. Because a write ends the wait, how much clock time passes before a reader of this client sees a write is not `EmptyPageDelay`: a test that measures the delay itself, such as one that checks the `emptyPageDelay` its code passes, reads through a client of `StubClient.CreateStreams` over a script of answers, whose wait is the whole delay on the clock of its options.

[What the fake does](https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does) lists every operation, rule and limit. It never ignores a member of a request: an unimplemented member fails with the code `UnsupportedByFakeException`, an unanswered operation with `UnknownOperationException`.

### Failing a call

`fake.FailNext(operation, failure, times = 1, when = null)` makes the next requests of an operation fail, through every handler of the fake, those made before too, so a client of `CreateClient` and one of `CreateStreamsClient` alike. The fake never sees such a request, as DynamoDB never sees one it throttles, so a failed write writes nothing, and the requests after it are answered by the fake. `ThrowIfFailuresLeft()` fails a test whose code sent fewer requests than it planned failures for, as `ThrowIfResponsesLeft()` does for a handler:

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
using var client = fake.CreateClient();
fake.FailNext("PutItem", StubResponse.DynamoDbError("InternalServerError", "Failure of the service.", System.Net.HttpStatusCode.InternalServerError));

try
{
    await client.Table<Customer>("Shop").Put(customer).SendAsync(ct);
}
catch (InternalServerErrorException)
{
    // The fake holds no customer: the request never reached it.
}

fake.ThrowIfFailuresLeft();
```

A client of the fake makes one attempt per call, so a failure reaches the code under test at once. A test of the retries asks for more attempts: `fake.CreateClient(options => options with { MaxAttempts = 3 })` repeats a throttle or a server error as it does on DynamoDB. `ReadStreamAsync` reads a shard on after a failure that its client gave up on, waiting on the clock of the fake, which the test moves:

```csharp
var fake = new FakeDynamoDb(clock);
using var streams = fake.CreateStreamsClient();
fake.FailNext("GetRecords", StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded."), times: 2);
// ReadStreamAsync over streams reads on after the two throttles, once the test moves the clock past its waits.
```

- **Which requests.** A request takes the first planned failure of its operation, `GetRecords` or its whole target, that `when` accepts, such as `request => request.BodyJson.GetProperty("TableName").GetString() == "Orders"` for one table; `when` runs under the lock of the fake and must not call it. `times` is how many requests get it.
- **Threads.** The shard readers of `ReadStreamAsync` send at once; each failure is taken by one request, under the lock of the fake.
- **The handler first.** A response queued with `Enqueue` on a handler still answers before the fake is asked, so before a planned failure.
- **Reset.** `Reset()` drops the failures no request took. The direct API, `Put` or `Items`, is no request and takes none.

For a write whose response is lost, the write must happen and the client must see an error, which `FailNext` does not do. Let the fake answer and return an error instead:

```csharp
var fake = new FakeDynamoDb();
using var lost = new StubHandler(request =>
{
    var answer = fake.Answer(request);
    return request.Operation == "PutItem"
        ? StubResponse.DynamoDbError("InternalServerError", "The response was lost.", System.Net.HttpStatusCode.InternalServerError)
        : answer;
});
```

## STS

A credentials provider does not go through `HttpHandler`, so `StubSts` is a server: HTTP on `127.0.0.1` and a free port, from the constructor to `Dispose`. It answers `AssumeRoleWithWebIdentity` and `AssumeRole` with the next queued `StubStsResponse`, else `DefaultResponse`, and records `Requests`.

```csharp
using var sts = new StubSts();
sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token")));

var provider = new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint);
var credentials = await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].RoleArn);
```

That constructor reads no variable, so the test can run in parallel. To go through the default chain as a pod does, set `AWS_ENDPOINT_URL_STS` to `sts.Endpoint`, with `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, and do not run that test in parallel. Without `AWS_ENDPOINT_URL_STS` the token goes to the real STS in the test's region, or the fetch fails without a region. The stub never touches the environment or the disk: the test writes the token file.

`AssumeRoleCredentialsProvider` goes to the stub through `StsEndpoint`, and reads no variable when `Region` is set too. The stub takes any signature; the form is in `Requests`, with `ExternalId` and `DurationSeconds`.

```csharp
using var sts = new StubSts();
sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token"));

var source = new StaticCredentialsProvider("AKIAIOSFODNN7EXAMPLE", "source-secret");
var options = new AssumeRoleOptions { ExternalId = "partner-7", Region = AwsRegion.FromName("eu-west-1"), StsEndpoint = sts.Endpoint };
var provider = new AssumeRoleCredentialsProvider(source, roleArn, options: options);
await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].ExternalId);
```

Credentials without an expiration go out with `2100-01-01T00:00:00Z`, as STS always sends one, so the provider never refreshes them. It refreshes in the background from 15 minutes before the expiration and blocks callers from 10 minutes before. A test of a refresh sets the expiration it needs, relative to its clock.
