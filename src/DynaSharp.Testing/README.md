# DynaSharp.Testing

Test support for [DynaSharp](https://github.com/msallin/DynaSharp). It fakes the wire, so a test needs neither DynamoDB nor a mock of the client, and it runs the real signing, mapping and expressions.

## A client for a test

`StubHandler` answers requests and records them. `StubClient.Create` makes a client over it, in the region `us-east-1`, with static fake credentials, one attempt per call and no attempt timeout. It reads nothing from the environment or the shared AWS files, so a test behaves the same on every machine. `StubClient.CreateStreams` does the same for DynamoDB Streams.

```csharp
using var stub = new StubHandler();
using var client = StubClient.Create(stub);
stub.Enqueue(StubResponse.Item(customer));

var found = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync(ct);
```

A request takes the next response in the queue, then the answer of a responder given to the constructor, then `DefaultResponse`, and without any of them it fails. A response of a `StubResponse` factory is the answer to the operations it is written for, and a request of another operation that takes it throws, naming both operations and the position of the response in the queue. Without that check the answer of an `UpdateTimeToLive` would read as no item to a `Get`, and a test of a miss would pass for the wrong reason. A response left in the queue is no error, since a test may queue more than one path needs: `stub.ThrowIfResponsesLeft()` at the end of a test checks that every one was taken. The second argument of `Create` changes the options:

```csharp
using var retrying = StubClient.Create(stub, options => options with { MaxAttempts = 4, TimeProvider = clock });
```

There is no attempt timeout because `AttemptTimeout` runs on `TimeProvider`, so a fake clock would time out the request the test is answering. A test of a timeout sets one.

The timeout of a wait for a table, `WaitUntilActiveAsync` and `WaitUntilActive()`, runs on that clock too. A loop that advances the clock with `Task.Yield` until the wait ends burns the whole timeout, and the wait throws `TableNotActiveException` with answers still in the queue. Give such a wait a timeout the loop does not reach, or advance the clock once per poll the handler has seen.

## Answers

`StubResponse` writes what DynamoDB sends, from entities, so a test holds no JSON.

| For | Use |
|---|---|
| Items | `Item`, `Items`, `Count`, `Attributes`, `BatchItems`, `Unprocessed` |
| Tables | `Table`, `Table<T>`, `TableCreated`, `TableUpdated`, `TableDeleted`, `TablesListed`, `TimeToLive`, `TimeToLiveUpdated` |
| Streams | `StreamDescription`, `Shard`, `ShardIterator`, `Records`, which takes `Inserted<T>`, `Modified<T>`, `Removed<T>` and `Change` |
| Failures | `ConditionalCheckFailed`, `TransactionCanceled`, `ResourceNotFound`, `ResourceInUse`, `DynamoDbError` |
| Anything else | `Json` |

Each is the answer to the operations it is written for: `Item` to `GetItem`, `Items` and `Count` to `Query` and `Scan`, `Attributes` and `ConditionalCheckFailed` to `PutItem`, `UpdateItem` and `DeleteItem`, `TransactionCanceled` to the two transactions, `BatchItems` and `Unprocessed` to their batch, and each answer of the tables and streams to the one call it describes, such as `TableUpdated` to `UpdateTable` and `TablesListed` to `ListTables`, whose overload with `lastEvaluatedTableName` is a page that more pages follow. `ResourceNotFound` and `ResourceInUse` have the shape of an error that many operations return and fit any, as do `DynamoDbError`, `Json` and `new StubResponse()`.

`Table<T>(name)` lists the keys and the indexes of the entity as active, so a wait for its indexes ends on it. It describes one entity: a table of several takes `Table(name, status, indexes)`.

## What was sent

`stub.Requests` holds every request. A `RecordedRequest` has the `Operation`, the `Body`, `BodyText` and `BodyJson`, and reads an item or a key back with `ReadItem<T>()` and `ReadKey<T>()`. `TableName`, `IndexName`, `ConsistentRead`, `ScanIndexForward` and `Limit` give those members of the body as sent, or `null` where the request has none: a batch or a transaction names a table per entry, a command sends `ConsistentRead` only as `true` and `ScanIndexForward` only as `false`.

Five properties give an expression with its placeholders resolved, or `null` if the request has none: `ConditionText`, `UpdateText`, `KeyConditionText`, `FilterText` and `ProjectionText`.

```csharp
await client.Table<Product>("Shop").Update(Product.Keys.Of("KB-01"))
    .Set(Product.Attrs.Price, 79.00m)
    .If(Product.Attrs.Price.Eq(95.00m))
    .SendAsync(ct);

var sent = stub.Requests[^1];
Console.WriteLine(sent.ConditionText); // Price = 95.00
Console.WriteLine(sent.UpdateText);    // SET Price = 79.00, _type = if_not_exists(_type, "PRODUCT")
```

Keywords, functions and spacing stay as sent, and a placeholder the maps lack stays as it is, which is the mistake a test should see. A name stands as it is, in backticks if it holds a dot or a bracket. A value reads in the notation of PartiQL with double quotes:

| Stored | Text |
|---|---|
| String, number | `"Placed"`, `3.10` |
| Boolean, null | `true`, `null` |
| Binary | `b"aGk="` |
| List, map | `["a", 1]`, `{"k": 1}` |
| Set | `<<"a", "b">>`, `<<1>>`, `<<b"aA==">>` |

`TransactItems` reads a transaction step by step, each a `RecordedTransactStep` with its `Kind`, `TableName`, `ReadItem<T>()`, `ReadKey<T>()` and the same texts. `RecordingHandler` records the `Exchanges` of a real client instead.

`ToRequestJson()` of a command is the other way to look, without a client at all.

## A fake DynamoDB

`FakeDynamoDb` is an in-memory DynamoDB behind the same seam. A test sets up tables and items, runs the code under test through a real client, and reads the state back, with no queued answers.

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>("Shop");
using var client = fake.CreateClient();

await client.Table<Customer>("Shop").Put(customer).SendAsync(ct);

var stored = fake.Get<Customer>("Shop", StubResponse.KeyOf(Customer.Keys.Of("alice")));
```

A table exists only once something creates it: a `CreateTable` over the wire, `CreateTable().SendAsync` of a table, `fake.CreateTable(request)`, or `fake.CreateTable<T>(name)`, which declares the keys and indexes of the entity as its `CreateTable()` would. A request on any other table answers `ResourceNotFoundException`. A table and its indexes are `ACTIVE` in the answer that creates them, so a wait for them ends at once, and `DeleteTable` frees the name at once.

`Put`, `Get`, `Delete`, `Items` and their forms for entities set up and read items without a client. They go through the checks of a request, so a key that does not match the schema throws the `ValidationException` a client would get. `DescribeTable`, `TableNames` and `Reset` do the same for tables. The fake is thread-safe and holds no static state: one per test, made with `new`, with nothing to dispose.

`fake.CreateClient()` is `StubClient.Create(fake.CreateHandler())`, and the handler is a `StubHandler` whose responder is the fake. So `Requests` records what the client sent, and a response queued with `Enqueue` answers the next request in place of the fake, which never sees it: a queued throttle changes no state, as a throttle of DynamoDB does not. A write whose response is lost is a responder that lets the fake answer and returns an error in its place:

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

The clock given to the constructor dates the tables. Time to live is stored and reported, and nothing expires by itself: `fake.ExpireItems()` is the background sweep of DynamoDB, run when the test says, and deletes every item whose attribute of time to live is a number of epoch seconds at or before that clock. As on DynamoDB, an expiry more than five years before the clock is left alone.

What the fake answers, and how it answers what it does not do, is listed on [the page of this package](https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does). It never ignores a member of a request: what it does not implement fails with the code `UnsupportedByFakeException`, and an operation it does not answer with `UnknownOperationException`.

## STS

The requests of a credentials provider do not go through `HttpHandler`, so `StubSts` is a server: HTTP on `127.0.0.1` and a free port, from the constructor to `Dispose`. It answers `AssumeRoleWithWebIdentity` with the next queued `StubStsResponse`, then `DefaultResponse`, and records `Requests`.

```csharp
using var sts = new StubSts();
sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token")));

var provider = new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint);
var credentials = await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].RoleArn);
```

That constructor reads no variable, so the test runs in parallel with any other. A test can also set `AWS_ENDPOINT_URL_STS` to `sts.Endpoint`, with `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, and go through the default chain as a pod does; that test must not run in parallel. `AWS_ENDPOINT_URL_STS` is what keeps it from the real STS: forget it, and the token goes to STS in the region of the test, or the fetch fails without a region. The stub never touches the environment or the disk, so the token file is the test's to write.

STS always sends an expiration, so credentials without one go out with `2100-01-01T00:00:00Z`. That is past any clock a test runs on, so the provider never refreshes them: it refreshes in the background from 15 minutes before the expiration, and makes callers wait from 10 minutes before it. A test of a refresh gives the expiration it needs, relative to its clock.
