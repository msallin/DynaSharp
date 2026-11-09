# Errors and retries

The client retries what can pass on a second try and throws the rest. Every exception is in the namespace `DynaSharp`.

```csharp
try
{
    await orders.Put(order).IfNotExists().SendAsync(ct);
}
catch (ConditionalCheckFailedException)
{
    Console.WriteLine("The order exists already.");
}
catch (DynamoDbException exception) when (exception.IsTransient)
{
    Console.WriteLine("DynamoDB is busy. Try again later.");
}
```

A timeout and a network failure are a `TimeoutException` and an `HttpRequestException`, not a `DynamoDbException`.

## What throws

Every error DynamoDB returns is a `DynamoDbException` with `ErrorCode` (without its namespace), `StatusCode`, `RequestId`, `OperationName` and `TableName` (as the request named it; null for a batch or transaction). A known code has its own class; any other is a plain `DynamoDbException`.

| Exception | When | Retried | Carries |
|---|---|---|---|
| `ConditionalCheckFailedException` | A write's condition did not hold; nothing was written. `TrySendAsync` returns it as `ConditionMet` false instead, see [Conditions](commands.md#conditions) | No | `Item`, what the condition saw, with `ReturnOldOnFailure()`. `Key`, as stored. `Message` names the condition sent |
| `TransactionCanceledException` | A transaction was cancelled; none of it took effect | No | `ReasonFor(marker)`, `ItemFor(marker)`, `Failures`, `CancellationReasons`. `Message` names each failed action by position, verb and table |
| `TransactionConflictException` | A single-item call met an item a transaction holds | No | |
| `ValidationException` | DynamoDB rejected the request: an expression that does not parse, a key of the wrong type, a range bound above the other | No | For a command, `Expressions`, `ExpressionNames` and `ExpressionValueTypes` say what `#n0` and `:v0` stand for, without values |
| `ResourceNotFoundException` | The table or index does not exist or is not active yet | No | |
| `ResourceInUseException` | The table is being created, updated or deleted, or already exists | No | |
| `ProvisionedThroughputExceededException`, `ThrottlingException`, `RequestLimitExceededException`, `LimitExceededException` | A table, the account or the control plane is throttled | Yes, with the throttling backoff | |
| `TransactionInProgressException` | A transaction with the same token is still running | Yes, as throttling, with the same token | |
| `ReplicatedWriteConflictException` | A write met a write from another region of a global table | Yes | |
| `InternalServerErrorException`, any HTTP 500, 502, 503 or 504 | DynamoDB or a proxy in front of it failed. A body that is no DynamoDB response has the code `UnrecognizedResponse` | Yes | |
| `AccessDeniedException` | The credentials lack a permission, see [What a call needs](#what-a-call-needs) | No | |
| `UnrecognizedClientException` | The access key or session token is not valid | No | |
| `DynamoDbException` with `ErrorCode` `InvalidSignatureException` | The signature was rejected: a secret key not of the access key, a request changed in between, or a local clock off. `Message` is DynamoDB's | No, but a clock more than 4 minutes off is corrected and the request resent, see [Retries](#retries) | |
| `DynamoDbException` with `ErrorCode` `MissingAuthenticationTokenException` | The request arrived unsigned: something in between removed the `Authorization` header. `Message` is DynamoDB's | No | |
| `DynamoDbException` with `ErrorCode` `ExpiredTokenException` | The credentials expired | Once, at once, if the provider can fetch new ones: the default chain, the container, instance metadata, web identity, assume role and SSO providers, a profile using one of them or `credential_process`, and the environment with `AWS_CREDENTIAL_EXPIRATION`. Otherwise no | |
| `IdempotentParameterMismatchException` | A transaction token came back within 10 minutes with other actions | No | |
| `ItemCollectionSizeLimitExceededException` | A partition of a table with a local index would grow past 10 GB | No | |
| `ExpiredIteratorException`, `TrimmedDataAccessException` | A DynamoDB Streams shard iterator is older than 15 minutes, or its records are gone | No | |

`IsTransient` on every `DynamoDbException` says whether the same call can succeed later unchanged, like `DbException.IsTransient`. True means the client's attempts ran out: try later, not at once in a loop.

- **True:** every row retried above, `TransactionConflictException`, and a `TransactionCanceledException` whose failed actions are all `TransactionConflict` or `Throttled`. `BatchIncompleteException` and `BatchGetIncompleteException<T>` are true when they gave up on their rounds or on a transient failure.
- **False:** everything else: a failed condition (it cannot pass until the item changes), a rejected request, a missing table, access and token errors, and an unknown code.

The client throws these itself:

| Exception | When | Carries |
|---|---|---|
| `TimeoutException` | An attempt ran past `AttemptTimeout`, or a timeout of the HTTP handler cancelled it, such as `SocketsHttpHandler.ConnectTimeout`. Retried; surfaces after the last attempt. Also a `CreateTable()` that ran past its `Timeout(...)`, see [A table that exists](tables.md#a-table-that-exists), which is not retried | The message of the `CreateTable()` names the table and the step |
| `HttpRequestException` | The network failed. Retried, except a response above 64 MB, whose `HttpRequestError` is `ConfigurationLimitExceeded` | |
| `OperationCanceledException` | The caller's `CancellationToken` was cancelled. Never retried | |
| `CredentialsUnavailableException` | No credentials source applies, or one that applies is broken. Never retried. See [When credentials fail](credentials.md#when-credentials-fail) | |
| `BatchIncompleteException` | A batch write gave up after its own resends, see [Batches](#batches) | `UnprocessedItems`, `Written`, the failure as `InnerException`. `ErrorCode` is `BatchIncomplete` |
| `BatchGetIncompleteException<T>` | A batch get gave up | `UnprocessedKeys` and the `Items` read so far |
| `TableNotActiveException` | A wait for a table ran out, or `CreateTable()` found a table missing a declared index. A `TimeoutException` | `TableName`, `LastStatus` and `PendingIndex` |
| `DynamoDbMappingException` | An item does not fit its type: a `required` attribute is missing, a value cannot be read, or the item is of another type. Never retried; an update that throws it has been applied. A query or scan can skip such items, see [Queries](queries.md#skip-items-that-fail-to-map) | `EntityType`, `AttributeName` and `Key` |
| `KeyFormatException` | A value cannot be part of a key, see [Generated code](generated-code.md#keysof-and-keystryof). An `ArgumentException` | `ParamName`, the placeholder |
| `ArgumentException` | A key of `Table<Item>` is empty, an index is not declared by the entity, a fan-out repeats a partition key | |
| `InvalidOperationException` | The chain is wrong: a command used twice, a `[Version]` rule, `Value` of a `SelectResult` without an item. Or `CreateTable().IfNotExists()` found a table whose key differs from the entities', see [A table that exists](tables.md#a-table-that-exists) | |
| `FormatException` | `ContinuationToken.Parse` got text that is no token of this version, or `PageAsync` got a token that does not fit the query or `TokenSigner`, see [Queries](queries.md#paging-for-web-apis). The message quotes nothing of the token | |

No message names a key or a value. `Key`, `Item`, `UnprocessedItems`, `UnprocessedKeys` and `Items` hold your data; the client logs none of it.

## Retries

One retry loop serves every level: `Table<T>`, `Table<Item>`, the request API and `SendRawAsync`. Each page of a query is retried on its own.

- **Attempts.** `MaxAttempts`, 4 by default, counts the first attempt. `.MaxAttempts(n)` on a command sets it per call.
- **Waits.** A random time between zero and `min(20 s, base * 2^(attempt - 1))`, base 50 ms for throttling and 100 ms otherwise: by default at most 50, 100, 200 ms and 100, 200, 400 ms, so a dependency still starting gets at most 700 ms. Cancelling the call ends a wait at once.
- **Quota.** A client has 500 tokens for all its requests. A retry takes 5, or 10 after a timeout; a success returns 1, or what its last retry took. With the quota empty, errors surface at once until successes refill it, and the client logs a warning, event 5 under [Observability](observability.md#logs).
- **Resends.** If DynamoDB rejects a local clock more than 4 minutes off, the client learns the offset and resends at once, and the same goes for the STS call of an assumed role. If it rejects expired credentials of a provider that can replace them, the provider drops them and the client resends at once, once per call. Both count as an attempt but take no wait and no quota.
- **The end.** The last attempt's error surfaces unchanged, and the client logs a warning, event 4.

The loop never retries a failed condition. `RetryOnConflictAsync` reruns your read-modify-write instead, see [Retry on a conflict](commands.md#retry-on-a-conflict).

### Batches

A batch resends what DynamoDB left unprocessed, with the backoff above; each of its requests has the loop's retries. `MaxStalledRounds(n)`, 8 by default, bounds consecutive requests that get nothing done. Then, on a throttle, a timeout, a lost connection or a server error on the last attempt of a request, or on any other failure after a request went through, it throws `BatchIncompleteException` or `BatchGetIncompleteException<T>`. Their `UnprocessedItems` or `UnprocessedKeys` go straight into `client.BatchWriteItemAsync` or `client.BatchGetItemAsync`. DynamoDB may have applied part of a request that got no answer, so its writes count as unprocessed, and sending them again is safe: a put or a delete ends where it would have. A failure of the first request that repeating cannot fix, such as a `ValidationException`, applied nothing and surfaces as it is.

## Where a retry applies a write twice

When a response is lost (a timeout, a dropped connection), the write was applied and the retry sends it again.

- **Safe to repeat.** A `Put` without a condition, a `Delete`, a `Set` of a value, `SetIfNotExists`, `Remove`, a set `Add` or `Delete`, and the puts and deletes of a batch.
- **Applied twice.** `Add` on a number, `list_append` and arithmetic in `SET`, such as `.Set($"{Attr("n")} = {Attr("n")} + {1}")`. A counter counts one too many.
- **Fails against itself.** A conditional write (`IfNotExists()`, `IfExists()`, `IfVersion(n)`, a `Put` of a `[Version]` entity, your own `If(...)`) meets its own first attempt and throws `ConditionalCheckFailedException` for a write that was applied. Read the item to tell, or let `ReturnOldOnFailure()` hand it over and compare it with what the write sent. A second delivery of the same change fails the same way, and nothing on the wire tells the two apart; `TrySendAsync` does not either, it only returns the answer without an exception, see [Events that arrive out of order](patterns-writes.md#events-that-arrive-out-of-order).
- **Applied once.** A transaction: every attempt of one send carries the same `ClientRequestToken`, and DynamoDB answers a repeat within 10 minutes with the first outcome. `.Token(id)` makes your own retry safe too, across a restart.
- **Applied again by the helper.** Inside `RetryOnConflictAsync`, a conditional write that fails against itself counts as a conflict, so the delegate runs and writes again. A write sent with `TrySendAsync` throws no conflict, so the helper does not run it again for a failed condition.

`.MaxAttempts(1)` makes a write happen at most once, not exactly once: a lost response surfaces as a `TimeoutException` or an `HttpRequestException` after the write was applied. A condition makes it exact, since the second attempt cannot pass it:

```csharp
await customers.Update(Customer.Keys.Of("alice"))
    .Add(Customer.Attrs.OrderCount, 1)
    .MaxAttempts(1)
    .SendAsync(ct);

var seen = await customers.Get(Customer.Keys.Of("alice")).ItemAsync(ct);
await customers.Update(Customer.Keys.Of("alice"))
    .Add(Customer.Attrs.OrderCount, 1)
    .If(Customer.Attrs.OrderCount.Eq(seen!.OrderCount))
    .SendAsync(ct);
```

`.MaxAttempts(n)` is on every command and transaction step, before `Return(attr)` and `Select(handle)`, which end the chain. A parallel scan and `CreateTable()` have none. For the request API, use a second client: `new DynamoDbClient(client.Options with { MaxAttempts = 1 })` shares the credentials and a handler that was given.

## Timeouts and cancellation

- **`AttemptTimeout`**, 30 seconds by default, bounds one attempt, from looking up the credentials to reading the whole response. It surfaces as `TimeoutException`, never `OperationCanceledException`, and is retried with a fresh timeout per attempt, so a call can take `MaxAttempts * AttemptTimeout` plus the waits. `Timeout.InfiniteTimeSpan` turns it off.
- **A timeout of the HTTP handler**, such as `ConnectTimeout` of a `SocketsHttpHandler` given as `HttpHandler`, surfaces as `TimeoutException` too, with the handler's `TaskCanceledException` inside, and is retried like `AttemptTimeout`. Every cancellation of the request that the caller's token did not cause counts as such a timeout. A credentials provider that cancels its own lookup (an endpoint it gave up on) is no such timeout: it surfaces as `CredentialsUnavailableException` with the cancellation inside, and is not retried.
- **A `CancellationToken`** bounds the whole call, retries and waits included, and surfaces as `OperationCanceledException`, never retried. To bound a call in time, cancel the token after that time.

```csharp
using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
deadline.CancelAfter(TimeSpan.FromSeconds(2));
try
{
    var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(deadline.Token);
}
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
    Console.WriteLine("No answer within 2 seconds.");
}
```

Neither tells whether a write was applied, see [above](#where-a-retry-applies-a-write-twice). A wait for a table has its own timeout, see [A table that exists](tables.md#a-table-that-exists).

## Limits

These are DynamoDB's limits, as AWS documents them under [Quotas in Amazon DynamoDB](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ServiceQuotas.html). Where the client does not act on one, DynamoDB rejects the request, mostly with a `ValidationException`, which is not retried.

| Limit | DynamoDB | What DynaSharp does |
|---|---|---|
| Item | 400 KB, names and values counted | Sends it; DynamoDB rejects a larger one with `ValidationException` |
| Partition key, sort key | 2048 bytes, 1024 bytes, also of an index | Sends it; DynamoDB rejects a longer one with `ValidationException` |
| Nesting of a document | 32 levels | A `JsonElement` deeper than that throws `ArgumentException` on the write, see [JSON of a contract](entities.md#json-of-a-contract) |
| `TransactWriteItems`, `TransactGetItems` | 100 actions, 4 MB of items | The step that would be action 101 throws `InvalidOperationException`, also inside `PutAll`, and leaves the command used up. A transaction above 4 MB is sent, and DynamoDB rejects it with `ValidationException` |
| `BatchWriteItem` | 25 puts and deletes, 16 MB per request | Any number: `BatchWrite()` cuts them into requests of 25 that fit in 16 MB, sends them one after the other and resends what DynamoDB left unprocessed |
| `BatchGetItem` | 100 keys per request, 16 MB per response | Any number: `BatchGet()` cuts them into requests of 100 and asks again for the keys a response left out |
| `Query`, `Scan` | 1 MB read per page, before the filter | `ItemsAsync` and `PagesAsync` follow the pages; `PageAsync` gives one, see [Page by page](queries.md#page-by-page) |
| Global secondary indexes | 20 per table, a default quota AWS can raise | Declares what the entities declare; DynamoDB rejects the `CreateTable` or `UpdateTable` |
| Local secondary indexes | 5 per table, created with the table only | The same. An existing table cannot get one, see [Local indexes](entities.md#local-indexes) |
| Item collection of a table with a local index | 10 GB per partition key value | DynamoDB rejects the write with `ItemCollectionSizeLimitExceededException`, see [What throws](#what-throws) |
| Expression | 4 KB per expression string | Sends it; DynamoDB rejects a longer one with `ValidationException` |
| Attribute name | 64 KB, and 255 characters for a key attribute of a secondary index and a projected attribute of a local one | Sends it; DynamoDB rejects a longer one with `ValidationException` |
| Table and index name | 3 to 255 of `a-z`, `A-Z`, `0-9`, `_`, `-` and `.` | A declared name outside that is the build error [`DYNA039`](diagnostics.md#entities) |

The request API of `DynaSharp.Requests` sends what it is given: a `TransactWriteItemsRequest` of 101 actions or a `BatchWriteItemRequest` of 26 writes goes out, and DynamoDB rejects it with `ValidationException`.

## What a call needs

A role needs the IAM action of each operation a call sends, on the ARN of each table it names (`arn:aws:dynamodb:<region>:<account>:table/Shop`) unless the row says otherwise.

| Call | Actions |
|---|---|
| `Get`, a `Get` with `Select` | `dynamodb:GetItem` |
| `Put` | `dynamodb:PutItem` |
| `Update` | `dynamodb:UpdateItem` |
| `Delete` | `dynamodb:DeleteItem` |
| `Query`, its `ItemAsync`, `CountAsync` and pages, a fan-out, a mixed read | `dynamodb:Query`, on the table, or `table/Shop/index/ByStatus` for an index |
| `Scan`, `Segment`, `Parallel` | `dynamodb:Scan`, on the table or the index |
| `BatchGet()` | `dynamodb:BatchGetItem` |
| `BatchWrite()` | `dynamodb:BatchWriteItem` |
| `TransactWrite()` | Each step's action: `dynamodb:PutItem` for `Put` and `Unique`, `dynamodb:UpdateItem`, `dynamodb:DeleteItem`, `dynamodb:ConditionCheckItem` for `Check`. None for the transaction itself |
| `TransactGet()` | `dynamodb:GetItem` |
| `CreateTable()` | `dynamodb:CreateTable` and `dynamodb:DescribeTable`. With `IfNotExists()`, an existing table takes `dynamodb:DescribeTable` alone |
| `CreateTable().TimeToLive(...)`, or `CreateTable()` of an entity with `[TimeToLive]` | Also `dynamodb:DescribeTimeToLive`, and `dynamodb:UpdateTimeToLive` while time to live is not on yet |
| `WaitUntilActive()`, `WaitUntilActiveAsync`, `DescribeTableAsync`, `client.DescribeTableAsync` | `dynamodb:DescribeTable` |
| `client.UpdateTableAsync`, for an index or a stream on an existing table | `dynamodb:UpdateTable` |
| `client.DeleteTableAsync` | `dynamodb:DeleteTable` |
| `ToRequest()` of `CreateTable()` with `Tags`, sent by `client.CreateTableAsync` | Also `dynamodb:TagResource` |
| `ListStreamsAsync` of `DynamoDbStreamsClient` | `dynamodb:ListStreams`, on `*` |
| `DescribeStreamAsync`, `GetShardIteratorAsync`, `GetRecordsAsync`, `ReadShardAsync`, `ReadStreamAsync` | `dynamodb:DescribeStream`, `dynamodb:GetShardIterator`, `dynamodb:GetRecords`, on `table/Shop/stream/*` |
| `ReadStreamAsync` and `ReadChangesAsync` of a table | Also `dynamodb:DescribeTable`, on the table |
| A `LockTable` of `DynaSharp.Coordination` | `dynamodb:GetItem` and `dynamodb:UpdateItem`, and `dynamodb:ConditionCheckItem` for `IsHeld` in a transaction |
| Any other request API method, `SendRawAsync` | `dynamodb:` plus the operation name, on what it names |

An index has its own ARN, so a policy on the table alone lets no query read an index. A shop role that reads and writes items and indexes and creates the table at start:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:DeleteItem",
        "dynamodb:ConditionCheckItem", "dynamodb:Query", "dynamodb:Scan",
        "dynamodb:BatchGetItem", "dynamodb:BatchWriteItem",
        "dynamodb:DescribeTable", "dynamodb:CreateTable",
        "dynamodb:DescribeTimeToLive", "dynamodb:UpdateTimeToLive"
      ],
      "Resource": [
        "arn:aws:dynamodb:eu-central-1:123456789012:table/Shop",
        "arn:aws:dynamodb:eu-central-1:123456789012:table/Shop/index/*"
      ]
    }
  ]
}
```

Without `CreateTable()`, leave out `dynamodb:CreateTable`, `dynamodb:DescribeTimeToLive` and `dynamodb:UpdateTimeToLive`, and keep `dynamodb:DescribeTable` only for `WaitUntilActiveAsync` or `DescribeTableAsync`.

**IRSA on EKS.** The web identity provider exchanges the pod's token with `AssumeRoleWithWebIdentity`, an unsigned call, so no pod policy grants it. The role's trust policy does: it allows `sts:AssumeRoleWithWebIdentity` to the cluster's OIDC provider, with conditions on the service account in `sub` and on `sts.amazonaws.com` in `aud`. EKS Pod Identity uses the container endpoint instead; its role trusts `pods.eks.amazonaws.com` with `sts:AssumeRole` and `sts:TagSession`. [Use IRSA on EKS](credentials.md#use-irsa-on-eks) says how the client finds the token.
