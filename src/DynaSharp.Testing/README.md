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

A request takes the next response in the queue, then the answer of a responder given to the constructor, then `DefaultResponse`, and without any of them it fails. The second argument of `Create` changes the options:

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
| Tables | `Table`, `Table<T>`, `TableCreated`, `TableDeleted`, `TimeToLive`, `TimeToLiveUpdated` |
| Streams | `StreamDescription`, `ShardIterator`, `Records` |
| Failures | `ConditionalCheckFailed`, `TransactionCanceled`, `ResourceNotFound`, `ResourceInUse`, `DynamoDbError` |
| Anything else | `Json` |

`Table<T>(name)` lists the keys and the indexes of the entity as active, so a wait for its indexes ends on it. It describes one entity: a table of several takes `Table(name, status, indexes)`.

## What was sent

`stub.Requests` holds every request. A `RecordedRequest` has the `Operation`, the `Body`, `BodyText` and `BodyJson`, and reads an item or a key back with `ReadItem<T>()` and `ReadKey<T>()`.

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

## STS

The requests of a credentials provider do not go through `HttpHandler`, so `StubSts` is a server: HTTP on `127.0.0.1` and a free port, from the constructor to `Dispose`. It answers `AssumeRoleWithWebIdentity` with the next queued `StubStsResponse`, then `DefaultResponse`, and records `Requests`.

```csharp
using var sts = new StubSts();
sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "secret", "token")));

var provider = new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint);
var credentials = await provider.GetCredentialsAsync(ct);
Console.WriteLine(sts.Requests[0].RoleArn);
```

That constructor reads no variable, so the test runs in parallel with any other. A test can also set `AWS_ENDPOINT_URL_STS` to `sts.Endpoint`, with `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, and go through the default chain as a pod does; that test must not run in parallel. The stub never touches the environment or the disk, so the token file is the test's to write.
