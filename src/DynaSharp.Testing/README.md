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

Create a table with a `CreateTable` over the wire, `CreateTable().SendAsync`, `fake.CreateTable(request)` or `fake.CreateTable<T>(name)`, which declares the entity's keys and indexes as its `CreateTable()` would. Any other table is `ResourceNotFoundException`. A table and its indexes are `ACTIVE` in the answer that creates them, and `DeleteTable` frees the name at once.

`Put`, `Get`, `Delete`, `Items` and their entity forms set up and read items without a client, through the checks of a request: a key that does not match the schema throws the `ValidationException` a client would get. `DescribeTable`, `TableNames` and `Reset` do the same for tables. The fake is thread-safe, holds no static state and needs no disposing: make one per test with `new`.

`fake.CreateClient()` is `StubClient.Create(fake.CreateHandler())`, a `StubHandler` whose responder is the fake. `Requests` records what the client sent, and a response queued with `Enqueue` answers the next request instead of the fake, which never sees it: a queued throttle changes no state, as on DynamoDB.

A client of the fake makes one attempt per call. A test of the retries queues the failure on the handler and asks for more attempts:

```csharp
var fake = new FakeDynamoDb();
using var handler = fake.CreateHandler();
handler.Enqueue(StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded."));
using var retrying = StubClient.Create(handler, options => options with { MaxAttempts = 3 });
```

For a write whose response is lost, let the fake answer and return an error instead:

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

The constructor's clock dates the tables. Time to live is stored and reported, but nothing expires by itself: `fake.ExpireItems()` runs DynamoDB's background sweep when the test says, deleting every item whose time to live attribute is a number of epoch seconds at or before that clock. An expiry more than five years before the clock is left alone, as on DynamoDB.

Reads and writes behave as on DynamoDB: queries, segmented scans, projections, pages cut by `Limit` before the filter and at 1 MB with `LastEvaluatedKey` holding the index key too, conditions with `ReturnValues` and `ReturnValuesOnConditionCheckFailure`, batches, and all-or-nothing transactions with one cancellation reason per action. An optimistic lock, a create that must not overwrite, or a failed transaction condition leaves the item as it was. A `ClientRequestToken` makes a transaction idempotent for 10 minutes on the fake's clock. Consumed capacity follows the Developer Guide, indexes included. A batch never leaves unprocessed items or keys; to test a client that retries them, queue that answer on the handler.

A table with a stream records its changes in a shard, which `fake.SplitStreamShards(table)` splits, and `fake.CreateStreamsClient()` reads them through a `DynamoDbStreamsClient`, as `StubClient.CreateStreams` makes it. So the code that consumes a stream is tested with the code that writes:

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
using var client = fake.CreateClient();
using var streams = fake.CreateStreamsClient();
var updated = await client.UpdateTableAsync(new UpdateTableRequest
{
    TableName = "Shop",
    StreamSpecification = new StreamSpecification { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages },
}, ct);

await client.Table<Customer>("Shop").Put(customer).SendAsync(ct);

var arn = updated.TableDescription!.LatestStreamArn!;
await foreach (var change in streams.ReadStreamAsync(new ReadStreamRequest { StreamArn = arn, ShardIteratorType = ShardIteratorType.TrimHorizon }, ct))
{
    Console.WriteLine(change.Record.EventName); // Insert
    break;
}
```

The shard of a disabled stream is closed, so a read of it, or of the whole stream, ends.

[What the fake does](https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does) lists every operation, rule and limit. It never ignores a member of a request: an unimplemented member fails with the code `UnsupportedByFakeException`, an unanswered operation with `UnknownOperationException`.

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
