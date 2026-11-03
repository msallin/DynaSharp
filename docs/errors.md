# Errors and retries

The client retries what can pass on a second try and throws the rest. This page says what throws and what it carries, which failures are retried and how, where a retry can apply a write twice, how a timeout differs from a cancellation, and which IAM permissions each call needs. Every exception is in the namespace `DynaSharp`, so a `catch` needs no other `using`.

## What throws

Every error that DynamoDB returns is a `DynamoDbException` with `ErrorCode`, the code without its namespace, `StatusCode`, `RequestId`, `OperationName` and `TableName`, the table as the request named it, or null for a batch or a transaction. A code the client knows has a class of its own, and any other is a plain `DynamoDbException`.

| Exception | When | Retried | Carries |
|---|---|---|---|
| `ConditionalCheckFailedException` | A condition of a write did not hold, and nothing was written | No | `Item`, what the condition saw, with `ReturnOldOnFailure()`. `Key`, the key of the item as stored. `Message` names the condition the write sent |
| `TransactionCanceledException` | A transaction was cancelled, and none of it took effect | No | `ReasonFor(marker)`, `ItemFor(marker)`, `Failures` and `CancellationReasons`. `Message` names each failed action by position, verb and table |
| `TransactionConflictException` | A single-item call met an item that a transaction holds | No | |
| `ValidationException` | DynamoDB rejected the request: an expression that does not parse, a key of the wrong type, a range bound above the other | No | `Expressions`, `ExpressionNames` and `ExpressionValueTypes` for a command, which say what `#n0` and `:v0` stand for. Values are left out |
| `ResourceNotFoundException` | The table or index does not exist, or is not active yet | No | |
| `ResourceInUseException` | The table is being created, updated or deleted, or exists already | No | |
| `ProvisionedThroughputExceededException`, `ThrottlingException`, `RequestLimitExceededException`, `LimitExceededException` | Throttling: a table, an account or the control plane gets more than it takes | Yes, with the throttling backoff | |
| `TransactionInProgressException` | A transaction with the same token is still running | Yes, as throttling, with the same token | |
| `ReplicatedWriteConflictException` | A write met a write of another region of a global table | Yes | |
| `InternalServerErrorException`, any HTTP 500, 502, 503 or 504 | DynamoDB or a proxy in front of it failed. A body that is no DynamoDB response has the code `UnrecognizedResponse` | Yes | |
| `AccessDeniedException` | The credentials lack a permission, see [What a call needs](#what-a-call-needs) | No | |
| `UnrecognizedClientException` | The access key or the session token is not valid | No | |
| `DynamoDbException` with `ErrorCode` `ExpiredTokenException` | The credentials expired | Once and at once, when the provider can fetch new ones: the default chain, and the container, instance metadata and web identity providers. Otherwise no | |
| `IdempotentParameterMismatchException` | A transaction token came back within 10 minutes with other actions | No | |
| `ItemCollectionSizeLimitExceededException` | A partition of a table with a local index would grow past 10 GB | No | |
| `ExpiredIteratorException`, `TrimmedDataAccessException` | A shard iterator of DynamoDB Streams is older than 15 minutes, or its records are gone | No | |

The client throws these itself:

| Exception | When | Carries |
|---|---|---|
| `TimeoutException` | An attempt ran past `AttemptTimeout`. It is retried, and surfaces after the last attempt | |
| `HttpRequestException` | The network failed. It is retried, except a response above 64 MB, whose `HttpRequestError` is `ConfigurationLimitExceeded` | |
| `OperationCanceledException` | The caller's `CancellationToken` was cancelled. Never retried | |
| `CredentialsUnavailableException` | No source of credentials applies, or one that applies is broken. Never retried | |
| `BatchIncompleteException` | A batch write gave up, after its own resends, see [Batches](#batches) | `UnprocessedItems` and `Written`, and the failure as `InnerException`. Its `ErrorCode` is `BatchIncomplete` |
| `BatchGetIncompleteException<T>` | A batch get gave up | `UnprocessedKeys` and the `Items` read so far |
| `TableNotActiveException` | A wait for a table ran out, or `CreateTable()` found a table without an index it declares. It is a `TimeoutException` | `TableName`, `LastStatus` and `PendingIndex` |
| `DynamoDbMappingException` | An item does not fit its type: a `required` attribute is missing, a value cannot be read, or the item is of another type. DynamoDB did its part, so it is never retried, and an update that throws it has been applied | `EntityType`, `AttributeName` and `Key` |
| `KeyFormatException` | A value cannot be part of a key, see [Generated code](generated-code.md#keysof-and-keystryof). It is an `ArgumentException` | `ParamName`, the placeholder |
| `ArgumentException` | A key of `Table<Item>` is empty, an index is not declared by the entity, a fan-out repeats a partition key | |
| `InvalidOperationException` | The chain itself is wrong: a command used twice, a `[Version]` rule, `Value` of a `SelectResult` without an item | |
| `FormatException` | `ContinuationToken.Parse` got text that is no token of this version | |

No message names a key or a value. `Key`, `Item`, `UnprocessedItems`, `UnprocessedKeys` and `Items` hold your data, so what code does with them is its own decision, and the client logs none of it.

## Retries

Every request goes through one retry loop, on every level: `Table<T>`, `Table<Item>`, the request API and `SendRawAsync`. A query sends a request per page, and each one is retried on its own.

- **Attempts.** `MaxAttempts` of the options, 4 by default, counts the first attempt. `.MaxAttempts(n)` on a command sets it for that call.
- **Waits.** Before a retry the client waits a random time between zero and `min(20 s, base * 2^(attempt - 1))`, with a base of 50 ms for throttling and 100 ms for everything else. With the defaults that is at most 50, 100 and 200 ms for throttling, and 100, 200 and 400 ms otherwise, so a dependency that is still starting has 700 ms. Cancelling the call ends a wait at once.
- **Quota.** A client has 500 tokens for all its requests. A retry takes 5, or 10 after a timeout, and a success gives back 1, or what its last retry took. With the quota empty, errors surface at once until successes refill it, so a struggling service gets no extra load. The client logs a warning, event 5 under [Observability](observability.md#logs).
- **Resends.** When DynamoDB rejects a request for a local clock that is more than 4 minutes off, the client learns the offset and sends again at once. When it rejects expired credentials of a provider that can replace them, the provider drops them and the client sends again at once, once per call. Both count as an attempt and take no wait and no quota.
- **The end.** After the last attempt the error of that attempt surfaces unchanged, and the client logs a warning, event 4.

A condition that failed is no reason to send the same request again, so the loop never retries a conflict. `RetryOnConflictAsync` runs your read-modify-write again instead, see [Retry on a conflict](commands.md#retry-on-a-conflict).

### Batches

A batch sends again what DynamoDB left unprocessed, with the backoff above, and each of its requests has the retries of the loop. `MaxStalledRounds(n)`, 8 by default, bounds the requests in a row that get nothing done. Then, or on a throttle on the last attempt, or on any other failure after a request went through, it throws `BatchIncompleteException` or `BatchGetIncompleteException<T>`, whose `UnprocessedItems` or `UnprocessedKeys` go straight into `client.BatchWriteItemAsync` or `client.BatchGetItemAsync` of the request API. A failure of the first request, where nothing was applied, surfaces as it is.

## Where a retry applies a write twice

A request can reach DynamoDB and lose its response: a timeout, a dropped connection. Then the write was applied, and the retry sends it again.

- **Safe to repeat.** A `Put` without a condition, a `Delete`, a `Set` of a value, `SetIfNotExists`, `Remove`, a set `Add` or `Delete`, and the puts and deletes of a batch: the second time writes what the first did.
- **Applied twice.** `Add` on a number, `list_append` and arithmetic in `SET`, such as `.Set($"{Attr("n")} = {Attr("n")} + {1}")`. A counter counts one too many.
- **Fails against itself.** A conditional write, `IfNotExists()`, `IfExists()`, `IfVersion(n)`, a `Put` of a `[Version]` entity or your own `If(...)`, meets what its first attempt wrote, and throws `ConditionalCheckFailedException` for a write that was applied. Read the item to tell.
- **Applied once.** A transaction: every attempt of one send carries the same `ClientRequestToken`, and DynamoDB answers a repeat within 10 minutes with the outcome of the first. `.Token(id)` makes a retry of your own safe too, across a restart.
- **Applied again by the helper.** Inside `RetryOnConflictAsync`, a conditional write that fails against itself counts as a conflict, and the helper runs the delegate again, which writes again.

`.MaxAttempts(1)` makes a write happen at most once. It is not exactly once: a lost response then surfaces as a `TimeoutException` or an `HttpRequestException` after the write was applied. A condition is what makes it exact, because the second attempt cannot pass it:

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

`.MaxAttempts(n)` is on every command and transaction step, and comes before `Return(attr)` and `Select(handle)`, which end the chain. A parallel scan and `CreateTable()` have none. The request API takes no per-call setting, so a second client does it: `new DynamoDbClient(client.Options with { MaxAttempts = 1 })` shares the credentials and a handler that was given.

## Timeouts and cancellation

They are two different limits, and they surface as two different exceptions.

- **`AttemptTimeout`**, 30 seconds by default, bounds one attempt, from looking up the credentials to reading the whole response. It surfaces as `TimeoutException`, never as `OperationCanceledException`, and it is retried: each attempt gets a fresh timeout, so a call can take `MaxAttempts * AttemptTimeout` plus the waits. `Timeout.InfiniteTimeSpan` turns it off.
- **A `CancellationToken`** bounds the whole call, retries and waits included, and surfaces as `OperationCanceledException`. It is never retried. To bound a call in time, cancel the token after that time.

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

Neither tells whether a write was applied, see [above](#where-a-retry-applies-a-write-twice). A wait for a table has a timeout of its own, `WaitUntilActive(timeout)`, which surfaces as `TableNotActiveException` with what the table was doing. A token on the same time races it and gives a bare `OperationCanceledException`, which names no table, so give the wait less time than the token.

## What a call needs

A role needs the IAM action of each operation a call sends, on the ARN of the table, `arn:aws:dynamodb:<region>:<account>:table/Shop`, or of what it reads.

| Call | Actions | Resource |
|---|---|---|
| `Get`, a `Get` with `Select` | `dynamodb:GetItem` | The table |
| `Put` | `dynamodb:PutItem` | The table |
| `Update` | `dynamodb:UpdateItem` | The table |
| `Delete` | `dynamodb:DeleteItem` | The table |
| `Query`, its `ItemAsync`, `CountAsync` and pages, a fan-out, a mixed read | `dynamodb:Query` | The table, or `table/Shop/index/ByStatus` for an index |
| `Scan`, `Segment`, `Parallel` | `dynamodb:Scan` | The table or the index |
| `BatchGet()` | `dynamodb:BatchGetItem` | Each table |
| `BatchWrite()` | `dynamodb:BatchWriteItem` | Each table |
| `TransactWrite()` | The action of each step: `dynamodb:PutItem` for `Put` and `Unique`, `dynamodb:UpdateItem`, `dynamodb:DeleteItem`, and `dynamodb:ConditionCheckItem` for `Check`. There is no action for the transaction itself | Each table |
| `TransactGet()` | `dynamodb:GetItem` | Each table |
| `CreateTable()` | `dynamodb:CreateTable` and `dynamodb:DescribeTable`. With `IfNotExists()` a table that exists takes `dynamodb:DescribeTable` alone | The table |
| `CreateTable().TimeToLive(...)` | Also `dynamodb:DescribeTimeToLive`, and `dynamodb:UpdateTimeToLive` while time to live is not on yet | The table |
| `WaitUntilActive()`, `WaitUntilActiveAsync`, `DescribeAsync`, `client.DescribeTableAsync` | `dynamodb:DescribeTable` | The table |
| `client.UpdateTableAsync`, for an index or a stream on a table that exists | `dynamodb:UpdateTable` | The table |
| `client.DeleteTableAsync` | `dynamodb:DeleteTable` | The table |
| `ToRequest()` of `CreateTable()` with `Tags`, sent by `client.CreateTableAsync` | Also `dynamodb:TagResource` | The table |
| `ListStreamsAsync` of `DynamoDbStreamsClient` | `dynamodb:ListStreams` | `*` |
| `DescribeStreamAsync`, `GetShardIteratorAsync`, `GetRecordsAsync` | `dynamodb:DescribeStream`, `dynamodb:GetShardIterator`, `dynamodb:GetRecords` | `table/Shop/stream/*` |
| Any other method of the request API, `SendRawAsync` | `dynamodb:` and the name of the operation | What it names |

An index has an ARN of its own, so a policy on the table alone lets no query read an index. A role of the shop that reads and writes items and its indexes, and creates the table at start:

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

A program that does not create its table leaves out the last four actions but `dynamodb:DescribeTable`, which `IfNotExists()` and a wait still take.

**IRSA on EKS.** The web identity provider exchanges the token of the pod for credentials with `AssumeRoleWithWebIdentity`, a call that is not signed. So no policy of the pod grants it: the trust policy of the role does, by allowing `sts:AssumeRoleWithWebIdentity` to the OIDC provider of the cluster, with a condition on the service account in `sub` and on `sts.amazonaws.com` in `aud`. EKS Pod Identity goes through the container endpoint instead, and its role trusts `pods.eks.amazonaws.com` with `sts:AssumeRole` and `sts:TagSession`. [Getting started](getting-started.md#credentials) says how the client finds the token.
