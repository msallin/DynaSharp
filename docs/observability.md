# Observability

The client reports through .NET's own APIs: traces through an `ActivitySource` and metrics through a `Meter`, both named `DynaSharp`, and logs through `ILogger`. Names follow the OpenTelemetry semantic conventions for AWS SDK spans, DynamoDB and database client metrics. Without a listener, a span or measurement costs a null check. No key, item or attribute value appears in any of it, see [What stays out](#what-stays-out).

## Turning it on

With the OpenTelemetry SDK, subscribe by name:

- traces: `.WithTracing(tracing => tracing.AddSource("DynaSharp"))`
- metrics: `.WithMetrics(metrics => metrics.AddMeter("DynaSharp"))`

Logs need a logger factory in the options, in a hosted application the container's:

```csharp
using DynaSharp;
using Microsoft.Extensions.Logging;

public static class ClientSetup
{
    public static DynamoDbClient Create(ILoggerFactory loggerFactory) =>
        new(new DynamoDbClientOptions { LoggerFactory = loggerFactory });
}
```

Without `LoggerFactory` nothing is logged. The credentials provider the client makes when `Credentials` is not set logs to the same factory; a provider you pass in does not log.

The locks and leader election of `DynaSharp.Coordination` report to the same source and meter, and log to the factory of the client of their table: see [Coordination](coordination.md#observability). Their spans and logs name the lock, which is a name chosen in code; their metrics do not. Their logs also carry the holder label of a `LockTable`, a name of the process such as its pod, which spans and metrics never do.

## Traces

Each request is one span of kind `Client`, named after service and operation: `DynamoDB.GetItem`, `DynamoDB Streams.GetRecords`. A retried request is still one span; a query of three pages is three, a batch one per request, and `CreateTable()` one per call it makes, such as `DescribeTable` and `CreateTable`. A failed request sets the status to `Error` without the message, which can quote the request. A condition that did not hold for `TrySendAsync` is an answer, not a failure: its span keeps the status unset and gets `dynasharp.condition_met`, and its duration has no `error.type`.

| Attribute | On | Value |
|---|---|---|
| `db.system.name` | Every span | `aws.dynamodb` |
| `db.operation.name`, `rpc.method` | Every span | The operation, `GetItem` |
| `rpc.system`, `rpc.system.name` | Every span | `aws-api` |
| `rpc.service` | Every span | `DynamoDB` or `DynamoDB Streams` |
| `cloud.region` | Every span | The region of the client |
| `server.address`, `server.port` | Every span | The endpoint's host, and its port unless default |
| `aws.request_id` | A span with a response | The request id DynamoDB sent |
| `db.collection.name` | A call of one table | The table, as the request names it |
| `aws.dynamodb.table_names` | A call of one table or more | The tables. A batch or transaction lists all and has no `db.collection.name` |
| `aws.dynamodb.consistent_read` | `GetItem`, `Query`, `Scan` | Whether the read is strongly consistent |
| `aws.dynamodb.projection` | A read with a projection | The projection expression, names only |
| `aws.dynamodb.index_name`, `aws.dynamodb.limit`, `aws.dynamodb.select` | `Query` and `Scan` that set them | The index, the limit per page, the selection |
| `aws.dynamodb.scan_forward` | `Query` | `false` for a descending read |
| `aws.dynamodb.segment`, `aws.dynamodb.total_segments` | A `Scan` of one segment | The segment and the segment count |
| `aws.dynamodb.count`, `aws.dynamodb.scanned_count` | The response of `Query` and `Scan` | Items returned, and items read before the filter |
| `aws.dynamodb.consumed_capacity` | A response with consumed capacity | One JSON text per table, in the shape DynamoDB sends |
| `aws.dynamodb.provisioned_read_capacity`, `aws.dynamodb.provisioned_write_capacity` | `CreateTable` and `UpdateTable` that provision | The capacity units |
| `aws.dynamodb.exclusive_start_table`, `aws.dynamodb.table_count` | `ListTables` | Where the page starts, and how many names it returned |
| `error.type` | A failed request | DynamoDB's error code, `timeout`, or the exception's full type name, such as `System.Net.Http.HttpRequestException` |
| `db.response.status_code` | A failed request with a code from DynamoDB | The error code it sent |
| `dynasharp.condition_met` | A `PutItem`, `UpdateItem` or `DeleteItem` of `TrySendAsync`, also after `Return(handle)`, whose condition did not hold | `false` |

### Reading a stream

`ReadStreamAsync` adds a span per shard it reads, of kind `Internal`, named `DynamoDB Streams.ReadStream`. It is a child of the span that was current when the loop started, and the calls of the shard are its children. A child shard is not the child span of its parent, so that a stream read for days is no trace that grows for days: its span links to the span of the parent shard instead. The span lasts as long as the shard is read and its children are looked for, up to a minute more, which for an open shard can be hours, until DynamoDB closes it. A shard read again after a transient failure stays one span, with the failed calls under it.

`ReadChangesAsync`, the typed reading, reads through `ReadStreamAsync` and has the same spans; typing a change adds none, and a change it skips counts in `dynasharp.client.operation.skipped_items`.

| Attribute | Value |
|---|---|
| `db.system.name`, `rpc.service`, `cloud.region`, `server.address`, `server.port` | As on the spans of the calls |
| `dynasharp.stream.shard_id` | The shard |
| `dynasharp.stream.record_count` | The records of the shard handed over |
| `dynasharp.stream.retry_count` | How many times the shard was read again after a transient failure, on a shard that was |
| `error.type`, `db.response.status_code` | The failure that ended the reading, set as on a failed request. A shard stopped by the loop, or by the failure of another shard, has none |

## Metrics

| Metric | Type | Unit | Records |
|---|---|---|---|
| `db.client.operation.duration` | Histogram | `s` | Each request's duration, retries and waits included, in the conventions' buckets, 1 ms to 10 s |
| `dynasharp.client.operation.retries` | Counter | `{retry}` | Each resend after a failed attempt |
| `dynasharp.client.operation.throttles` | Counter | `{throttle}` | Each throttling error, repeated or not |
| `dynasharp.client.conflict.retries` | Counter | `{retry}` | Each rerun of the `RetryOnConflictAsync` delegate after a conflict |
| `dynasharp.client.conflict.exhausted` | Counter | `{conflict}` | Each conflict `RetryOnConflictAsync` passed on after the delegate used up its runs |
| `dynasharp.client.operation.skipped_items` | Counter | `{item}` | Each item `OnMappingFailure` skipped, see [Queries](queries.md#skip-items-that-fail-to-map), and each change `onMappingFailure` of `ReadChangesAsync` skipped, with `db.operation.name` `GetRecords`, see [Streams](streams.md#skip-changes-that-fail-to-map) |
| `dynasharp.client.stream.shards` | UpDownCounter | `{shard}` | The shards `ReadStreamAsync` is reading, up when one starts and down when it ends |
| `dynasharp.client.stream.records` | Counter | `{record}` | Each record `ReadStreamAsync` handed out |
| `dynasharp.client.stream.retries` | Counter | `{retry}` | Each time `ReadStreamAsync` read a shard again after a transient failure, see [Streams](streams.md#when-a-shard-fails) |
| `dynasharp.client.consumed_capacity` | Counter | `{capacity_unit}` | Capacity units DynamoDB reported, per table, indexes included. Only requests that ask, with `.ReturnCapacity()` or `ReturnConsumedCapacity`, have any |

| Tag | On |
|---|---|
| `db.operation.name` | All |
| `db.collection.name` | All, for a call of one table. On `consumed_capacity`, the table DynamoDB names in the response |
| `server.address`, `server.port` | All; they tell two clients apart. The stream metrics carry nothing else but the `error.type` of a stream retry |
| `error.type` | The duration of a failed request, the retries, the throttles, the two conflict counters and the stream retries |
| `db.system.name` | The duration |
| `db.response.status_code` | The duration of a request that failed with a code from DynamoDB |

The conflict counters carry the operation that met the conflict, such as `PutItem`.

## Logs

The client logs only what no single caller sees: its configuration, retries that ran out, a corrected clock, failed credential refreshes, an endpoint over plain http, the shards of a stream as they end or fail for a while, and a table that `CreateTable().IfNotExists()` found to differ from what the code declares, which the program that meets it is often not the one to fix. A failed request reaches its caller as an exception, and is logged only when it says something about the client, such as an empty retry quota. Event ids are stable; categories are the types:

- `DynaSharp.DynamoDbClient`
- `DynaSharp.Streams.DynamoDbStreamsClient`
- `DynaSharp.Credentials.DefaultCredentialsProvider`, with the providers it uses: `WebIdentityCredentialsProvider`, `ContainerCredentialsProvider` and `InstanceMetadataCredentialsProvider`

| Id | Level | Event | Message |
|---|---|---|---|
| 1 | Debug | The client chose its region | `DynamoDB client uses region {Region} (source: {Source}).` |
| 2 | Debug | The client chose its endpoint | `DynamoDB client uses endpoint {Endpoint} (source: {Source}).` |
| 3 | Debug | The client has its credentials provider | `DynamoDB client uses credentials provider {CredentialsProvider}.`, the type |
| 4 | Warning | A retryable error on the last attempt, passed on | `DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}, the last one allowed. ...` |
| 5 | Warning | A retryable error with the retry quota empty, passed on | `DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}. The retry quota of the client is used up, ...` |
| 6 | Warning | The signing clock was corrected | `The local clock differs from the DynamoDB clock by {Offset}. ...` |
| 7 | Warning | A batch gave up with work left | `DynamoDB {Operation} on table {Table} gave up with {Unprocessed} actions unprocessed after {Requests} requests. ...` |
| 8 | Warning | `RetryOnConflictAsync` used up its runs | `DynamoDB {Operation} on table {Table} lost to another writer with {ErrorType} on run {Runs} of RetryOnConflictAsync, ...` |
| 9 | Warning | The endpoint is http on a non-loopback host, once per client | `DynamoDB client sends requests to {Host} over plain http. The requests, their signatures and the session token travel unencrypted.` |
| 10 | Debug | `ReadStreamAsync` read a shard to its end and stopped looking for its children | `DynamoDB Streams shard {ShardId} was read to its end. Child shards to read next: {Children}.` |
| 11 | Debug | `ReadStreamAsync` found a child only on a later look | `DynamoDB Streams listed the child shard {ChildShardId} of shard {ShardId} only {Elapsed} after the first look for its children.` |
| 12 | Warning | `ReadStreamAsync` reads a shard again after a transient failure, once at the start of each outage of the shard | `DynamoDB Streams shard {ShardId} failed with {ErrorType} on the last attempt of a call. ReadStreamAsync reads it on from where it stopped, with a backoff, for up to {Window}, ...` |
| 13 | Information | The shard works again after such an outage | `DynamoDB Streams shard {ShardId} is read again, {Elapsed} after it failed and after {Retries} retries.` |
| 14 | Warning | `CreateTable().IfNotExists()` found a table that differs from what the command declares, once per command, see [A table that exists](tables.md#a-table-that-exists) | `DynamoDB table {Table} does not match what CreateTable() declares ({Count} in CreateTableResult.Drift), and is left as it is: {Drift}`, the descriptions of the drift |
| 20 | Debug | The default chain chose a source | `Credentials come from the {Source}.` |
| 21 | Warning | A refresh failed while the credentials are still good | `Refreshing the credentials from the {Source} failed in the advisory refresh period: {Reason}. ...`, with the exception |
| 22 | Warning | A refresh failed and requests fail until one succeeds | `Refreshing the credentials from the {Source} failed in the mandatory refresh period: {Reason}. ...`, with the exception |
| 23 | Warning | DynamoDB rejected the credentials as expired, and they were dropped | `DynamoDB rejected the credentials from the {Source} as expired. ...` |
| 24 | Warning | STS rejected the web identity token as expired, and the file is read again | `STS rejected the token of the {Source} as expired. ...` |
| 25 | Warning | The STS endpoint is http on a non-loopback host, once per provider | `STS is reached at {Host} over plain http. The web identity token in the request and the session token in the response travel unencrypted.` |
| 26 | Debug | The source a profile gets its credentials from | `The profile '{Profile}' gets its credentials from {Source}.` |
| 27 | Warning | Renewing the cached SSO token failed; the cached token is used while it lasts | `Refreshing the cached SSO token of {Token} failed: {Reason}. The cached token is used while it lasts.` |
| 28 | Warning | The renewed SSO token could not be written back to the token cache | `The refreshed SSO token of {Token} could not be written to the token cache: {Reason}. ...` |
| 29 | Debug | The cached SSO token was renewed | `The cached SSO token of {Token} was refreshed.` |

`{Table}` is the table of the call, `{ErrorType}` the error code. A message names no key, token or file content, and of a URL at most its host.

## A handler of your own

`HttpHandler` in the options takes any `HttpMessageHandler`, such as a `DelegatingHandler` that records the wire.

> [!WARNING]
> A request's content is a view of a pooled buffer, valid until the handler's `SendAsync` returns; then it belongs to another call. Read it before returning, never later: a body read late fails DynamoDB's signature check.

A handler must send a request once, never twice in parallel, which rules out hedging. A retry handler is not needed: the client retries, see [Errors and retries](errors.md).

## What stays out

Telemetry carries operations, tables, indexes, region and server, shards of a stream, error codes, counts, capacity units, attribute names in a projection, and the names of key, index and time to live attributes in the drift of a table (event 14), which are schema. The spans and logs of `DynaSharp.Coordination` also carry the name of a lock, which operators need to tell locks apart: it is a name chosen in code, like a table name, so keep data out of it. Its logs carry the holder label you give a `LockTable`, which names the process, and must not carry data either. Never a key, item, attribute value or an expression with its values; `ItemCollectionMetrics` holds a partition key, so it is never reported. Exception messages follow the same rule, but not what some exceptions carry on purpose, such as `ConditionalCheckFailedException.Key`, see [Errors and retries](errors.md#what-throws). `ToString()` of an `Item` or `AttributeValue` is DynamoDB JSON with the values.
