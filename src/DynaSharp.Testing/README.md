# DynaSharp.Testing

Test support for [DynaSharp](https://github.com/msallin/DynaSharp). It fakes the wire, so a test needs neither DynamoDB nor a mock, and runs the real signing, mapping and expressions.

| You want to | Use |
|---|---|
| Answer with canned responses and check what was sent | [`StubHandler`](#a-client-for-a-test) |
| Run code against state that it writes and reads back | [`FakeDynamoDb`](#a-fake-dynamodb) |
| Test credentials that go through STS | [`StubSts`](#sts) |

## A client for a test

A test needs both namespaces. `DynaSharp` holds the client, the tables and the extensions of their commands, such as `Query(pk, sk)`; without it the compiler names no missing `using`, only CS1501 or CS0315.

```csharp
using DynaSharp;
using DynaSharp.Testing;
```

`StubHandler` answers requests and records them. `StubClient.Create` makes a client over it: region `us-east-1`, static fake credentials, one attempt per call, no attempt timeout, and nothing read from the environment or the shared AWS files. `StubClient.CreateStreams` does the same for DynamoDB Streams.

```csharp
using var stub = new StubHandler();
using var client = StubClient.Create(stub);
stub.Enqueue(StubResponse.Item(customer));

var found = await client.Table<Customer>("Shop").Get(Customer.Keys.Of("alice")).ItemAsync(ct);
```

A request takes the next queued response, else the answer of the responder given to the constructor, else `DefaultResponse`, else it fails. A `StubResponse` taken by an operation it is not written for throws, naming both. Responses left in the queue are no error; `stub.ThrowIfResponsesLeft()` at the end of a test checks that all were taken. The second argument of `Create` changes the options:

```csharp
using var retrying = StubClient.Create(stub, options => options with { MaxAttempts = 4, TimeProvider = clock });
```

`AttemptTimeout` and the timeout of `WaitUntilActive()` run on `TimeProvider`. With a fake clock, set an `AttemptTimeout` only in a test of a timeout, and give a wait a timeout that a loop advancing the clock does not reach, or advance it once per poll the handler has seen; otherwise the wait throws `TableNotActiveException` with answers still queued.

## Answers

`StubResponse` writes DynamoDB's answers from entities, so a test holds no JSON:

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

`TablesListed` with `lastEvaluatedTableName` is a page with more to follow. `Table<T>()` describes the table the entity declares, and `Table<T>(name)` the one it is given; both list the entity's keys and indexes as active, so a wait for its indexes ends on it. A table of several entities takes `Table(name, status, indexes)`.

`StreamDescription(table, shards)` describes an `ENABLED` stream, on which `ReadStreamAsync` never ends; a script whose last shard closes answers `StreamDescription(table, StreamStatus.Disabled, shards)`.

## What was sent

`stub.Requests` holds every request. A `RecordedRequest` has `Operation`, `Body`, `BodyText` and `BodyJson`, and reads an item or key back with `ReadItem<T>()` and `ReadKey<T>()`. `TableName`, `IndexName`, `ConsistentRead`, `ScanIndexForward` and `Limit` give those members as sent, or `null` where absent, as in a batch, which names a table per entry; a command sends `ConsistentRead` only as `true` and `ScanIndexForward` only as `false`.

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

Keywords and spacing stay as sent, and a placeholder missing from the maps stays as it is, so the test sees the mistake. A name holding a dot or a bracket is in backticks, and a value is in PartiQL notation:

| Stored | Text |
|---|---|
| String, number | `"Placed"`, `3.10` |
| Boolean, null | `true`, `null` |
| Binary | `b"aGk="` |
| List, map | `["a", 1]`, `{"k": 1}` |
| Set | `<<"a", "b">>`, `<<1>>`, `<<b"aA==">>` |

`TransactItems` gives a transaction's steps, each a `RecordedTransactStep` with `Kind`, `TableName`, `ReadItem<T>()`, `ReadKey<T>()` and the same texts. `RecordingHandler` records the `Exchanges` of a real client. `ToRequestJson()` of a command shows the request without any client.

## A fake DynamoDB

`FakeDynamoDb` is an in-memory DynamoDB behind the same seam. A test sets up tables and items, runs the code under test through a real client, and reads the state back. It is thread-safe, holds no static state and needs no disposing: make one per test with `new`.

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>();
using var client = fake.CreateClient();

await client.Table<Customer>().Put(customer).SendAsync(ct);

var stored = fake.Get(Customer.Keys.Of("alice"));
var sent = fake.Requests.Select(request => request.Operation); // PutItem
```

A table is `ACTIVE` with its indexes in the answer that creates it, `DeleteTable` frees the name at once, and any other table is `ResourceNotFoundException`. To create one:

- `fake.CreateTable(command)` applies a `CreateTableCommand` of the code under test, unsent, at once and without a request: its keys, the indexes of every `Including`, its stream and its time to live. With `IfNotExists()` it keeps a table that exists.
- `fake.CreateTable<T>()` creates the table the entity declares, with its keys and indexes, as its `CreateTable()` would, and `fake.CreateTable<T>(name)` the same under another name.
- `fake.CreateTable(name, "pk", "sk")` creates the table of `client.Table(name, "pk", "sk")`, without a model, whose keys are strings, such as the table of the locks of `DynaSharp.Coordination`.
- `fake.CreateTable(request)` takes a `CreateTableRequest`, and a client of the fake can send `CreateTable().SendAsync` or a `CreateTable` of its own.

Create a table of several entities from the command the application builds at start-up, so that the fake gets every index of every entity it names, without an `await` in a constructor:

```csharp
var fake = new FakeDynamoDb();
using var client = fake.CreateClient();
fake.CreateTable(client.Table<Customer>().CreateTable().Including<Order>().IfNotExists());
```

The forms without a table name, `CreateTable<T>()`, `DescribeTable<T>()`, `EnableStream<T>(viewType)`, `DisableStream<T>()`, `Put(entity)`, `Get(key)`, `Delete(key)`, `Delete(entity)`, `Items<T>()`, `TableItems<T>()`, `GetItem(key)` and `PutItem<T>(item)`, use the table the entity declares, with `[DynamoDbEntity(Table = "...")]` or the default of its assembly, and throw `InvalidOperationException` for an entity that declares none. They take the name as declared: the fake knows no `TableNaming`, so a test whose client sets one, or that wants a table of its own, passes the name, as in `fake.CreateTable<Customer>("dev-Shop")`.

`Put`, `Get`, `Delete` and `Items` set up and read items without a client, through the checks of a request, so a key that does not match the schema throws the `ValidationException` a client would get. `TableItems<T>()` gives every item of the table as raw items, `GetItem(key)` one, and `PutItem<T>(item)` puts a raw item, such as one of an older shape that the entity cannot read. `DescribeTable`, `DeleteTable`, `TableNames` and `Reset` do the same for tables.

On a table of several entities, `Items<T>` gives the items of `T` and skips the others, as a typed `Scan()` does:

```csharp
var fake = new FakeDynamoDb();
fake.CreateTable<Customer>();
fake.Put(customer);
fake.Put(order);

IReadOnlyList<Customer> stored = fake.Items<Customer>();
```

An item of `T` that `T` cannot read still throws `DynamoDbMappingException`, and so does `Get<T>` for an item of another entity on that key.

`fake.Requests` holds what every client of `fake.CreateClient()` and `fake.CreateStreamsClient()` sent, in the order sent. `fake.Enqueue(response)` answers the next request of any of them instead of the fake, so a queued throttle changes no state; with a stream reader polling in the background, use `FailNext`, which names the operation. `Reset()` empties both. `fake.CreateHandler()` gives a `StubHandler` over the fake for a client made another way.

Nothing expires by itself: `fake.ExpireItems()` runs DynamoDB's sweep when the test says, on the tables with time to live on, deleting each item whose expiry is at or before the fake's clock and not more than five years before it. `fake.CreateTable<T>` turns time to live on for an entity with a `[TimeToLive]` property.

Reads, writes, batches and transactions behave as on DynamoDB, consumed capacity included; [What the fake does](https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does) lists every operation, rule and limit. The fake never ignores a member of a request: an unimplemented member fails with the code `UnsupportedByFakeException`, an unanswered operation with `UnknownOperationException`. A batch never leaves unprocessed items or keys; to test a client that retries them, queue that answer on the handler.

A table with a stream records its changes, which a `DynamoDbStreamsClient` of `fake.CreateStreamsClient()` reads. `fake.EnableStream(table, viewType)` and `fake.DisableStream(table)` switch the stream as an `UpdateTable` does, and `fake.SplitStreamShards(table)` splits its shards:

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

The shards of a disabled stream are closed, so `fake.DisableStream("Shop")` ends a reading that a test runs to its end. Code that only handles changes needs no stream: a test builds a `Change<T>`, a record or a `MixedChange` with its constructor, `new MixedChange<Customer, Order>(new Change<Order> { ... })`, see [Streams](https://msallin.github.io/DynaSharp/streams.html#several-entities).

A reader of `CreateStreamsClient` that finds nothing waits `EmptyPageDelay`, as against DynamoDB, but the wait also ends at the next change of a stream of the fake, so a test reads a write in milliseconds without setting the delay or moving a clock. A child that `secondChildListedAfter` lists late and a retry after `FailNext` still wait on the clock, which the test moves. A test of the delay itself reads through `StubClient.CreateStreams` over a script of answers.

### Failing a call

`fake.FailNext(operation, failure, times = 1, when = null)` makes the next requests of an operation fail, through every handler of the fake. The fake never sees such a request, so a failed write writes nothing. `ThrowIfFailuresLeft()` fails a test whose code sent fewer requests than it planned failures for:

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

A client of the fake makes one attempt per call; a test of the retries asks for more, `fake.CreateClient(options => options with { MaxAttempts = 3 })`. `ReadStreamAsync` reads a shard on after a failure its client gave up on, waiting on the clock of the fake:

```csharp
var fake = new FakeDynamoDb(clock);
using var streams = fake.CreateStreamsClient();
fake.FailNext("GetRecords", StubResponse.DynamoDbError("ThrottlingException", "Rate exceeded."), times: 2);
// ReadStreamAsync over streams reads on after the two throttles, once the test moves the clock past its waits.
```

- **Which requests.** A request takes the first planned failure of its operation that `when` accepts, such as `request => request.BodyJson.GetProperty("TableName").GetString() == "Orders"`; `when` runs under the lock of the fake and must not call it. `times` is how many requests get it, each taken by one request, also when shard readers send at once.
- **The queue first.** A response queued with `Enqueue` answers before a planned failure. `Reset()` drops the failures no request took, and the direct API, `Put` or `Items`, takes none.

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

A credentials provider does not go through `HttpHandler`, so `StubSts` is a server on `127.0.0.1` and a free port, from the constructor to `Dispose`. It answers `AssumeRoleWithWebIdentity` and `AssumeRole` with the next queued `StubStsResponse`, else `DefaultResponse`, and records `Requests`.

```csharp
using var sts = new StubSts();
sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token")));

var provider = new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint);
var credentials = await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].RoleArn);
```

That constructor reads no variable, so the test can run in parallel. A test of the default chain sets `AWS_ENDPOINT_URL_STS` to `sts.Endpoint`, with `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, and does not run in parallel; without that endpoint the token goes to the real STS. The test writes the token file itself.

`AssumeRoleCredentialsProvider` goes to the stub through `StsEndpoint`, and reads no variable when `Region` is set too. The stub takes any signature, and `Requests` holds the form.

```csharp
using var sts = new StubSts();
sts.DefaultResponse = StubStsResponse.Credentials(new AwsCredentials("ASIAIOSFODNN7EXAMPLE", "secret", "token"));

var source = new StaticCredentialsProvider("AKIAIOSFODNN7EXAMPLE", "source-secret");
var options = new AssumeRoleOptions { ExternalId = "partner-7", Region = AwsRegion.FromName("eu-west-1"), StsEndpoint = sts.Endpoint };
var provider = new AssumeRoleCredentialsProvider(source, roleArn, options: options);
await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].ExternalId);
```

Credentials without an expiration go out with `2100-01-01T00:00:00Z`, so the provider never refreshes them. It refreshes from 15 minutes before the expiration and blocks callers from 10 minutes before: a test of a refresh sets the expiration it needs, relative to its clock.
