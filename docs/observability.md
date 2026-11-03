# Observability

The client reports what it does through the APIs of .NET itself: traces through an `ActivitySource`, metrics through a `Meter`, both named `DynaSharp`, and logs through `ILogger`. The names follow the OpenTelemetry semantic conventions for AWS SDK spans, DynamoDB and database client metrics. Without a listener, a span or a measurement costs a null check.

No key, item or attribute value appears in any of it.

## Turning it on

With the OpenTelemetry SDK, subscribe to both by name:

- traces: `.WithTracing(tracing => tracing.AddSource("DynaSharp"))`
- metrics: `.WithMetrics(metrics => metrics.AddMeter("DynaSharp"))`

Logs need the logger factory in the options. In an application with a host, it is the one of the container:

```csharp
using DynaSharp;
using Microsoft.Extensions.Logging;

public static class ClientSetup
{
    public static DynamoDbClient Create(ILoggerFactory loggerFactory) =>
        new(new DynamoDbClientOptions { LoggerFactory = loggerFactory });
}
```

Without `LoggerFactory` nothing is logged. The provider of credentials that the client makes when `Credentials` is not set logs to the same factory. A provider you create and pass in does not log.

## Traces

Each request to DynamoDB is one span of kind `Client`, named after the service and the operation: `DynamoDB.GetItem`, `DynamoDB.Query`, `DynamoDB Streams.GetRecords`. A retried request is still one span. A query that reads three pages is three spans, a batch is one span per request, and `CreateTable()` has a span for each call it makes, such as `DescribeTable` and `CreateTable`. A failed request sets the status to `Error` and leaves out the message, which can quote the request.

| Attribute | On | Value |
|---|---|---|
| `db.system.name` | Every span | `aws.dynamodb` |
| `db.operation.name`, `rpc.method` | Every span | The operation, `GetItem` |
| `rpc.system`, `rpc.system.name` | Every span | `aws-api` |
| `rpc.service` | Every span | `DynamoDB` or `DynamoDB Streams` |
| `cloud.region` | Every span | The region of the client |
| `server.address`, `server.port` | Every span | The host of the endpoint, and its port unless it is the default one |
| `aws.request_id` | A span with a response | The request id DynamoDB sent |
| `db.collection.name` | A call of one table | The table, as the request names it |
| `aws.dynamodb.table_names` | A call of one table or more | The tables. A batch or a transaction lists all of them and has no `db.collection.name` |
| `aws.dynamodb.consistent_read` | `GetItem`, `Query`, `Scan` | Whether the read is strongly consistent |
| `aws.dynamodb.projection` | A read with a projection | The projection expression, names only |
| `aws.dynamodb.index_name`, `aws.dynamodb.limit`, `aws.dynamodb.select` | `Query` and `Scan` that set them | The index, the limit per page, the selection |
| `aws.dynamodb.scan_forward` | `Query` | `false` for a descending read |
| `aws.dynamodb.segment`, `aws.dynamodb.total_segments` | A `Scan` of one segment | The segment, and how many there are |
| `aws.dynamodb.count`, `aws.dynamodb.scanned_count` | The response of `Query` and `Scan` | Items returned, and items read before the filter |
| `aws.dynamodb.consumed_capacity` | A response with consumed capacity | One JSON text per table, in the shape DynamoDB sends |
| `aws.dynamodb.provisioned_read_capacity`, `aws.dynamodb.provisioned_write_capacity` | `CreateTable` and `UpdateTable` that provision | The capacity units |
| `aws.dynamodb.exclusive_start_table`, `aws.dynamodb.table_count` | `ListTables` | Where the page starts, and how many names it returned |
| `error.type` | A failed request | The error code of DynamoDB, `timeout`, or the full name of the exception type, such as `System.Net.Http.HttpRequestException` |
| `db.response.status_code` | A failed request with a code from DynamoDB | The error code it sent |

## Metrics

| Metric | Type | Unit | Records |
|---|---|---|---|
| `db.client.operation.duration` | Histogram | `s` | The duration of each request, retries and their waits included. The buckets are those of the conventions, 1 ms to 10 s |
| `dynasharp.client.operation.retries` | Counter | `{retry}` | Each time a request is sent again after a failed attempt |
| `dynasharp.client.operation.throttles` | Counter | `{throttle}` | Each throttling error, whether or not the attempt was repeated |
| `dynasharp.client.conflict.retries` | Counter | `{retry}` | Each time `RetryOnConflictAsync` runs its delegate again after a conflict |
| `dynasharp.client.conflict.exhausted` | Counter | `{conflict}` | Each conflict that `RetryOnConflictAsync` passed on, because the delegate used up its runs |
| `dynasharp.client.consumed_capacity` | Counter | `{capacity_unit}` | The capacity units DynamoDB reported, per table and including its indexes. Only a request that asks for them, with `.ReturnCapacity()` or `ReturnConsumedCapacity`, has any |

| Tag | On |
|---|---|
| `db.operation.name` | All |
| `db.collection.name` | All, for a call of one table. On `consumed_capacity` it is the table DynamoDB names in the response |
| `server.address`, `server.port` | All. They tell the measurements of two clients apart |
| `error.type` | The duration of a failed request, the retries, the throttles and the two conflict counters |
| `db.system.name` | The duration |
| `db.response.status_code` | The duration of a request that failed with a code from DynamoDB |

The two conflict counters carry the operation that met the conflict, such as `PutItem`.

## Logs

The client logs only what no caller of one call can see: how it was configured, retries that ran out, a corrected clock, credentials that failed to refresh, an endpoint over plain http. A failed request reaches its caller as an exception, and is logged only when it says something about the client, such as a retry quota that is empty. The categories are the types: `DynaSharp.DynamoDbClient`, `DynaSharp.Streams.DynamoDbStreamsClient`, and `DynaSharp.Credentials.DefaultCredentialsProvider` with the providers it uses, `WebIdentityCredentialsProvider`, `ContainerCredentialsProvider` and `InstanceMetadataCredentialsProvider`. The event ids are stable.

| Id | Level | Event | Message |
|---|---|---|---|
| 1 | Debug | The client chose its region | `DynamoDB client uses region {Region} (source: {Source}).` |
| 2 | Debug | The client chose its endpoint | `DynamoDB client uses endpoint {Endpoint} (source: {Source}).` |
| 3 | Debug | The client has its provider of credentials | `DynamoDB client uses credentials provider {CredentialsProvider}.`, the type |
| 4 | Warning | A retryable error on the last attempt, passed on | `DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}, the last one allowed. ...` |
| 5 | Warning | A retryable error with the retry quota empty, passed on | `DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}. The retry quota of the client is used up, ...` |
| 6 | Warning | The signing clock was corrected | `The local clock differs from the DynamoDB clock by {Offset}. ...` |
| 7 | Warning | A batch gave up with work left | `DynamoDB {Operation} on table {Table} gave up with {Unprocessed} actions unprocessed after {Requests} requests. ...` |
| 8 | Warning | `RetryOnConflictAsync` used up its runs | `DynamoDB {Operation} on table {Table} lost to another writer with {ErrorType} on run {Runs} of RetryOnConflictAsync, ...` |
| 9 | Warning | The endpoint is http on a host that is not loopback, once per client | `DynamoDB client sends requests to {Host} over plain http. The requests, their signatures and the session token travel unencrypted.` |
| 20 | Debug | The default chain chose a source | `Credentials come from the {Source}.` |
| 21 | Warning | A refresh failed while the credentials are still good | `Refreshing the credentials from the {Source} failed in the advisory refresh period: {Reason}. ...`, with the exception |
| 22 | Warning | A refresh failed and requests fail until one succeeds | `Refreshing the credentials from the {Source} failed in the mandatory refresh period: {Reason}. ...`, with the exception |
| 23 | Warning | DynamoDB rejected the credentials as expired, and they were dropped | `DynamoDB rejected the credentials from the {Source} as expired. ...` |
| 24 | Warning | STS rejected the web identity token as expired, and the file is read again | `STS rejected the token of the {Source} as expired. ...` |
| 25 | Warning | The STS endpoint is http on a host that is not loopback, once per provider | `STS is reached at {Host} over plain http. The web identity token in the request and the session token in the response travel unencrypted.` |

`{Table}` is the table of the call, and `{ErrorType}` the error code. A message names no key, no token and no file content, and of a URL at most its host.

## A handler of your own

`HttpHandler` in the options takes any `HttpMessageHandler`, such as a `DelegatingHandler` that records the wire for inspection. The content of a request is a view of a pooled buffer, valid until the handler's `SendAsync` returns. After that the buffer belongs to another call, so a handler reads the content before it returns and never later, and a body read late fails the signature check of DynamoDB. A handler sends a request once and never twice in parallel, which rules out a hedging handler. A retry handler is not needed: the client retries by itself, see [Errors and retries](errors.md).

## What stays out

Spans, metrics and logs carry operations, tables, indexes, the region and the server, error codes, counts and capacity units, and names of attributes in a projection. Never a key, an item, an attribute value or an expression with its values. `ItemCollectionMetrics` holds a partition key, so it is never reported. The same holds for the messages of the exceptions, but not for what some of them carry on purpose, such as `ConditionalCheckFailedException.Key`, see [Errors and retries](errors.md#what-throws), and `ToString()` of an `Item` or an `AttributeValue` is DynamoDB JSON with the values.
