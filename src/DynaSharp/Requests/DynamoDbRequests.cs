using DynaSharp.Http;
using DynaSharp.Operations;

namespace DynaSharp.Requests;

/// <summary>
/// The request API of DynamoDB: one method per DynamoDB operation, as an extension method on <see cref="DynamoDbClient"/>. Each takes
/// the request record of the operation and returns its response record, so requests and responses mirror the service and items are
/// dictionaries of <see cref="AttributeValue"/>. The methods are in scope with <c>using DynaSharp.Requests;</c>.
/// </summary>
/// <remarks>
/// <para>
/// What follows applies to every method of this class. How the client is created and configured is described on
/// <see cref="DynamoDbClient"/> and <see cref="DynamoDbClientOptions"/>.
/// </para>
/// <para>
/// Errors returned by DynamoDB surface as <see cref="DynamoDbException"/> or one of its derived types, for example
/// <see cref="ResourceNotFoundException"/> or <see cref="ConditionalCheckFailedException"/>. The exception names the call:
/// <see cref="DynamoDbException.OperationName"/> and, when the call addresses one table, <see cref="DynamoDbException.TableName"/>
/// (the message stays the one DynamoDB sent, which often says neither). A request that exceeds
/// <see cref="DynamoDbClientOptions.AttemptTimeout"/> throws <see cref="TimeoutException"/>; a request that the caller cancels
/// throws <see cref="OperationCanceledException"/>. Network failures surface as <see cref="HttpRequestException"/>, and so does a
/// response body above 64 MB (DynamoDB's largest response is the 16 MB of a <c>BatchGetItem</c>) with
/// <see cref="HttpRequestException.HttpRequestError"/> <see cref="HttpRequestError.ConfigurationLimitExceeded"/>; that one is not retried.
/// </para>
/// <para>
/// Failed requests are retried automatically, as the AWS SDKs do in their "standard" retry mode: throttling errors, server errors,
/// network failures and attempts that time out are sent again after a growing random wait. Errors that repeating the request cannot
/// fix surface at once. <see cref="DynamoDbClientOptions.MaxAttempts"/>, which defaults to 4 attempts in all and can be set to 1 to
/// turn retries off, has the full list and the rules. A client shares a budget of retries between all its requests, so it stops
/// retrying while DynamoDB keeps failing. A retried error surfaces only after the last attempt, and it is the error of that attempt.
/// If the clock of the machine is more than four minutes away from DynamoDB's and the request is rejected for that, the client learns
/// the difference, signs with the corrected time from then on and sends the request again without waiting.
/// </para>
/// <para>
/// Because a request can be sent more than once, it can also take effect more than once. When an attempt reaches DynamoDB but its
/// response is lost (a timeout, a dropped connection), the retry repeats a write that was already applied. That is harmless for a
/// put or a delete of the same item. An update that depends on the value it finds, such as <c>ADD</c> on a counter or
/// <c>list_append</c>, is applied twice, and a put with a condition such as <c>attribute_not_exists</c> can fail against its own
/// earlier attempt. Guard such writes with a condition (for example on a version attribute), or set
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1 for them. <see cref="TransactWriteItemsAsync"/> is the exception: all its
/// attempts carry one idempotency token, so DynamoDB applies the transaction once.
/// </para>
/// <para>
/// Each operation emits one <c>CLIENT</c> activity from the <see cref="System.Diagnostics.ActivitySource"/> named <c>DynaSharp</c>
/// and records its duration in the <c>db.client.operation.duration</c> histogram of the <see cref="System.Diagnostics.Metrics.Meter"/>
/// named <c>DynaSharp</c>, following the OpenTelemetry semantic conventions. A retried operation is still one activity and one
/// measurement. The counters <c>dynasharp.client.operation.retries</c> and <c>dynasharp.client.operation.throttles</c> count the
/// retries and the throttling errors, tagged with the operation, the table (left out when a batch or transaction spans several
/// tables; the activity then lists them in <c>aws.dynamodb.table_names</c>), the server (<c>server.address</c>, and <c>server.port</c>
/// unless it is the default one) and the error type; the counter <c>dynasharp.client.consumed_capacity</c> carries the same tags,
/// without the error type, like the duration. When a retryable error is passed on
/// because attempts or the retry budget ran out, and when the clock is corrected, the client logs a warning. None of this contains
/// keys, items or attribute values. Subscribe with <c>AddSource("DynaSharp")</c> and <c>AddMeter("DynaSharp")</c>.
/// </para>
/// </remarks>
public static class DynamoDbRequests
{
    /// <summary>
    /// Reads a single item by its full primary key (DynamoDB <c>GetItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table, the key and the read options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="GetItemResponse.Item"/> is <see langword="null"/> when no item has the key: a missing item is
    /// not an error.
    /// </returns>
    /// <remarks>
    /// Reads are eventually consistent unless <see cref="GetItemRequest.ConsistentRead"/> is set, which costs twice the read
    /// capacity and may be slower. An item can be at most 400 KB. The key must contain exactly the key attributes of the table.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example when the key does not match the key schema of the table.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:GetItem</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<GetItemResponse> GetItemAsync(this DynamoDbClient client, GetItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<GetItemOperation, GetItemRequest, GetItemResponse>(request, cancellationToken);

    /// <summary>
    /// Creates an item or replaces an existing item with the same primary key entirely (DynamoDB <c>PutItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table, the item and the write options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="PutItemResponse.Attributes"/> holds the replaced item when the request asked for
    /// <see cref="ReturnValue.AllOld"/> and an item existed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The write is unconditional unless <see cref="PutItemRequest.ConditionExpression"/> is set, so it silently overwrites an
    /// existing item. Use <c>attribute_not_exists(pk)</c> (with the name of the partition key) to create an item only if it does not
    /// exist. An item can be at most 400 KB and must contain all key attributes.
    /// </para>
    /// <para>
    /// A request that fails with a timeout or a network error may or may not have been applied, and the client retries such requests
    /// (see <see cref="DynamoDbClientOptions.MaxAttempts"/>), so a put can reach DynamoDB more than once: an attempt whose response
    /// was lost may already have written the item. Writing the same item again is harmless, but a put with a condition expression,
    /// for example <c>attribute_not_exists(pk)</c> to create an item, can then fail with <see cref="ConditionalCheckFailedException"/>
    /// against its own earlier attempt although the item was created by this very call. When that matters, put a unique value such
    /// as a request id into the item and compare it on a failed condition, or set <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">
    /// The condition expression did not hold, so this attempt wrote nothing. If an earlier attempt of the same call was retried, it may have written the item.
    /// </exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example when the item exceeds 400 KB or lacks a key attribute.</exception>
    /// <exception cref="ItemCollectionSizeLimitExceededException">The item collection of a table with a local secondary index exceeds 10 GB.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:PutItem</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<PutItemResponse> PutItemAsync(this DynamoDbClient client, PutItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<PutItemOperation, PutItemRequest, PutItemResponse>(request, cancellationToken);

    /// <summary>
    /// Changes the attributes of an existing item, or creates the item if no item has the key (DynamoDB <c>UpdateItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table, the key, the update and the write options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="UpdateItemResponse.Attributes"/> holds the attributes that <see cref="UpdateItemRequest.ReturnValues"/>
    /// selected, if the request asked for any.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The update is atomic and is applied only to the attributes the update expression names. It is an upsert: when no item has the
    /// key, the item is created, unless a <see cref="UpdateItemRequest.ConditionExpression"/> such as <c>attribute_exists(pk)</c>
    /// forbids it. The item can be at most 400 KB after the update. The write capacity consumed is that of the larger of the item
    /// before and after the update.
    /// </para>
    /// <para>
    /// An update is not idempotent in general. A request that fails with a timeout or a network error may or may not have been
    /// applied, and a request that the client sends again after such a failure can be applied twice: an <c>ADD</c> or a
    /// <c>SET n = n + :x</c> then counts twice. Make updates safe to repeat by setting absolute values, or guard them with a
    /// condition on a version attribute.
    /// </para>
    /// <para>
    /// An item that takes part in an ongoing transaction is rejected with a <see cref="TransactionConflictException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">
    /// The condition expression did not hold, so this attempt changed nothing. If an earlier attempt of the same call was retried, it may have changed the item.
    /// </exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the update expression is malformed, an expression placeholder is unused or undefined, the key does not match the key schema, or the item would exceed 400 KB.</exception>
    /// <exception cref="ItemCollectionSizeLimitExceededException">The item collection of a table with a local secondary index exceeds 10 GB.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:UpdateItem</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<UpdateItemResponse> UpdateItemAsync(this DynamoDbClient client, UpdateItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<UpdateItemOperation, UpdateItemRequest, UpdateItemResponse>(request, cancellationToken);

    /// <summary>
    /// Deletes a single item by its full primary key (DynamoDB <c>DeleteItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table, the key and the write options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="DeleteItemResponse.Attributes"/> holds the deleted item when the request asked for
    /// <see cref="ReturnValue.AllOld"/> and an item existed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Deleting an item that does not exist is not an error. Without a <see cref="DeleteItemRequest.ConditionExpression"/> a delete is
    /// idempotent, so repeating it after a timeout is safe. With a condition, a repeated delete can fail against its own earlier
    /// effect: <c>attribute_exists(pk)</c> no longer holds once the item is gone, so the repeat throws
    /// <see cref="ConditionalCheckFailedException"/> although the item was deleted.
    /// </para>
    /// <para>
    /// The write capacity consumed is based on the size of the deleted item. An item that takes part in an ongoing transaction is
    /// rejected with a <see cref="TransactionConflictException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">
    /// The condition expression did not hold, so this attempt deleted nothing. If an earlier attempt of the same call was retried, it may have deleted the item.
    /// </exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example when the key does not match the key schema of the table or <see cref="DeleteItemRequest.ReturnValues"/> is neither none nor all old.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DeleteItem</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<DeleteItemResponse> DeleteItemAsync(this DynamoDbClient client, DeleteItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<DeleteItemOperation, DeleteItemRequest, DeleteItemResponse>(request, cancellationToken);

    /// <summary>
    /// Reads the items of one partition, optionally narrowed by a sort key condition, from a table or an index (DynamoDB <c>Query</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table or index, the key condition and the read options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One page of the result. Without matching items <see cref="QueryResponse.Items"/> is empty: that is not an error. Continue with
    /// <see cref="QueryResponse.LastEvaluatedKey"/> until it is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A call reads at most 1 MB of data (and at most <see cref="QueryRequest.Limit"/> items) and then applies the filter, so a page
    /// can be empty and still have a <see cref="QueryResponse.LastEvaluatedKey"/>. A response with a key does not mean that more
    /// items exist; only a response without one is the last page. This method returns a single page; call it again with the key as
    /// <see cref="QueryRequest.ExclusiveStartKey"/> to read on.
    /// </para>
    /// <para>
    /// The read capacity consumed depends on the size of the items read, not of those returned: a filter or a projection does not
    /// reduce it. A strongly consistent read costs twice an eventually consistent one. Global secondary indexes are eventually
    /// consistent only and can lag behind the table; they reject <see cref="QueryRequest.ConsistentRead"/>.
    /// </para>
    /// <para>
    /// A query is safe to repeat. Pages are not a snapshot: items written between two calls may or may not appear.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table or index does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key condition is not an equality on the partition key, <see cref="QueryRequest.Limit"/> is below 1, a consistent read is requested on a global secondary index, or <see cref="QueryRequest.Select"/> conflicts with the projection expression.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table or index is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:Query</c> on the table or index.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<QueryResponse> QueryAsync(this DynamoDbClient client, QueryRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<QueryOperation, QueryRequest, QueryResponse>(request, cancellationToken);

    /// <summary>
    /// Reads every item of a table or index, optionally filtered (DynamoDB <c>Scan</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table or index and the read options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One page of the result. Continue with <see cref="ScanResponse.LastEvaluatedKey"/> until it is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A call reads at most 1 MB of data (and at most <see cref="ScanRequest.Limit"/> items) and then applies the filter, so a page
    /// can be empty and still have a <see cref="ScanResponse.LastEvaluatedKey"/>. A response with a key does not mean that more
    /// items exist; only a response without one is the last page. This method returns a single page; call it again with the key as
    /// <see cref="ScanRequest.ExclusiveStartKey"/> to read on.
    /// </para>
    /// <para>
    /// A scan consumes read capacity for everything it reads, whatever the filter returns, and on a large table it takes a long
    /// time. To speed it up, set <see cref="ScanRequest.TotalSegments"/> and read the segments with parallel sequences of calls; each
    /// segment continues with its own <see cref="ScanResponse.LastEvaluatedKey"/>. Reads are eventually consistent unless
    /// <see cref="ScanRequest.ConsistentRead"/> is set, and a scan is not a snapshot of the table.
    /// </para>
    /// <para>
    /// A scan is safe to repeat.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table or index does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because <see cref="ScanRequest.Limit"/> is below 1, only one of <see cref="ScanRequest.Segment"/> and <see cref="ScanRequest.TotalSegments"/> is set, the segment is not below the total, or a consistent read is requested on a global secondary index.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">The table or index is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:Scan</c> on the table or index.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<ScanResponse> ScanAsync(this DynamoDbClient client, ScanRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<ScanOperation, ScanRequest, ScanResponse>(request, cancellationToken);

    /// <summary>
    /// Reads items from one or more tables by primary key in a single call (DynamoDB <c>BatchGetItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The keys to read per table, and the read options per table.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The items that were found, grouped by table, and the keys that were not read. A successful response can be partial: check
    /// <see cref="BatchGetItemResponse.UnprocessedKeys"/> and send what it holds again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A call can name at most 100 keys in all and returns at most 16 MB; an item can be at most 400 KB. DynamoDB stops early when
    /// the response would exceed 16 MB or 1 MB for a partition, when a table is throttled and when it fails internally for some keys,
    /// and returns the keys it did not read as <see cref="BatchGetItemResponse.UnprocessedKeys"/>. This method does not split large
    /// requests and does not read the unprocessed keys: the caller sends them again, with a growing wait between the calls (the
    /// retries of the client cover failed requests, not a partial result). The map is in the form of
    /// <see cref="BatchGetItemRequest.RequestItems"/>.
    /// </para>
    /// <para>
    /// Items come in no particular order, a key without an item contributes nothing to the response, and the same key twice is
    /// rejected. Reads are eventually consistent unless <see cref="KeysAndAttributes.ConsistentRead"/> is set for the table. The
    /// call fails with <see cref="ProvisionedThroughputExceededException"/> only when none of the keys could be processed. It is
    /// safe to repeat.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it names more than 100 keys or the same key twice, a key does not match the key schema of its table, or a placeholder is unused.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">None of the keys could be processed because the tables are throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:BatchGetItem</c> on a table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<BatchGetItemResponse> BatchGetItemAsync(this DynamoDbClient client, BatchGetItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<BatchGetItemOperation, BatchGetItemRequest, BatchGetItemResponse>(request, cancellationToken);

    /// <summary>
    /// Puts or deletes items in one or more tables in a single call (DynamoDB <c>BatchWriteItem</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The writes per table, and the metrics to return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The writes that were not processed. A successful response does not mean that every write was applied: check
    /// <see cref="BatchWriteItemResponse.UnprocessedItems"/> and send what it holds again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A call can contain at most 25 writes in all and transmit at most 16 MB; an item can be at most 400 KB (the JSON form of an
    /// item in the request can be larger than the stored item). DynamoDB rejects the whole request when it exceeds a limit, when a
    /// table does not exist, when a key does not match the key schema, or when two writes address the same item. This method does
    /// not split large requests and does not write the unprocessed items: the caller sends them again, with a growing wait between
    /// the calls (the retries of the client cover failed requests, not a partial result). The map is in the form of
    /// <see cref="BatchWriteItemRequest.RequestItems"/>.
    /// </para>
    /// <para>
    /// Every put and delete is atomic, the batch as a whole is not, and the writes are applied in no particular order. A put
    /// replaces an existing item entirely, and a write cannot carry a condition or return the old item; use
    /// <see cref="TransactWriteItemsAsync"/>, <see cref="PutItemAsync"/>, <see cref="UpdateItemAsync"/> or
    /// <see cref="DeleteItemAsync"/> for that. Repeating the call, as the client does after a timeout or a network error, writes the
    /// same items again, which is harmless.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it contains more than 25 writes, an item exceeds 400 KB, a key does not match the key schema of its table, or two writes address the same item.</exception>
    /// <exception cref="ItemCollectionSizeLimitExceededException">The item collection of a table with a local secondary index exceeds 10 GB.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">None of the writes could be processed because the tables are throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:PutItem</c> or <c>dynamodb:DeleteItem</c> on a table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>
    /// or <see cref="ReplicatedWriteConflictException"/>) surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<BatchWriteItemResponse> BatchWriteItemAsync(this DynamoDbClient client, BatchWriteItemRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<BatchWriteItemOperation, BatchWriteItemRequest, BatchWriteItemResponse>(request, cancellationToken);

    /// <summary>
    /// Reads up to 100 items from one or more tables as one consistent snapshot (DynamoDB <c>TransactGetItems</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The items to read, in order.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One result per requested item, in the order of the request. A result without <see cref="ItemResponse.Item"/> means that no
    /// item has the key: that is not an error.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The reads are serializable with respect to the writes of other transactions and strongly consistent. A call can contain at
    /// most 100 actions and read at most 4 MB in all; an item can be at most 400 KB. The tables must be in one AWS account and
    /// Region, and items are read from tables only. A transactional read costs twice the read capacity of the same read outside a
    /// transaction. The call is all or nothing, with no partial result and no unprocessed items; this method does not split large
    /// requests.
    /// </para>
    /// <para>
    /// DynamoDB cancels the call with a <see cref="TransactionCanceledException"/> when an item is being changed by another
    /// transaction or write, or when a table is throttled. The exception lists a reason per action. The client does not repeat a
    /// cancelled transaction; whether that is worthwhile depends on the reason. A transactional read is safe to repeat.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="TransactionCanceledException">DynamoDB cancelled the transaction, for example because it conflicts with a concurrent write; see <see cref="TransactionCanceledException.CancellationReasons"/>.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it contains more than 100 actions, the items exceed 4 MB, or a key does not match the key schema of its table.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">A table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:GetItem</c> on a table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<TransactGetItemsResponse> TransactGetItemsAsync(this DynamoDbClient client, TransactGetItemsRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<TransactGetItemsOperation, TransactGetItemsRequest, TransactGetItemsResponse>(request, cancellationToken);

    /// <summary>
    /// Applies up to 100 puts, updates, deletes and condition checks to one or more tables, all or nothing (DynamoDB
    /// <c>TransactWriteItems</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The actions, in order, and the idempotency token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The optional metrics. A response means that every action took effect.</returns>
    /// <remarks>
    /// <para>
    /// A call can contain at most 100 actions and write at most 4 MB in all; an item can be at most 400 KB. The tables must be in one
    /// AWS account and Region, and no two actions can target the same item. A transactional write costs twice the write capacity of
    /// the same write outside a transaction. This method does not split large requests.
    /// </para>
    /// <para>
    /// If any action fails, the call throws <see cref="TransactionCanceledException"/> and nothing was written; its
    /// <see cref="TransactionCanceledException.CancellationReasons"/> list one reason per action in the order of the request. The
    /// client does not repeat a cancelled transaction: the reasons <c>ConditionalCheckFailed</c> and <c>ValidationError</c> will not
    /// pass, whereas <c>TransactionConflict</c> and the throttling reasons may.
    /// </para>
    /// <para>
    /// <b>Idempotency.</b> The call carries <see cref="TransactWriteItemsRequest.ClientRequestToken"/>, a GUID the library generates
    /// when the request sets none. All attempts of one call, including the retries after a timeout or a network error, send the same
    /// token, so a transaction whose response was lost is not applied twice: DynamoDB recognizes the token for 10 minutes after the
    /// first request that used it was completed and answers with the outcome of the first one. A request that arrives while the first
    /// is still running is rejected with <see cref="TransactionInProgressException"/>, which the client retries; the same token
    /// with other parameters fails with <see cref="IdempotentParameterMismatchException"/>. To repeat a call yourself after an
    /// exception, within 10 minutes and with unchanged parameters, supply the token in the request, because the generated one is not
    /// returned.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="TransactionCanceledException">DynamoDB cancelled the transaction, for example because a condition did not hold or an item takes part in another transaction; see <see cref="TransactionCanceledException.CancellationReasons"/>.</exception>
    /// <exception cref="IdempotentParameterMismatchException">The token was used within the last 10 minutes for a request with other parameters.</exception>
    /// <exception cref="TransactionInProgressException">A transaction with the same token is still running, and so it was on every retry.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist or is not <c>ACTIVE</c>.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it contains more than 100 actions, two actions target the same item, an action has none or several members, or the token is longer than 36 characters.</exception>
    /// <exception cref="ProvisionedThroughputExceededException">A table is throttled, and so was every retry.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for the write actions of the transaction on a table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>
    /// or <see cref="ReplicatedWriteConflictException"/>) surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<TransactWriteItemsResponse> TransactWriteItemsAsync(this DynamoDbClient client, TransactWriteItemsRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<TransactWriteItemsOperation, TransactWriteItemsRequest, TransactWriteItemsResponse>(request, cancellationToken);

    /// <summary>
    /// Reads the definition and state of a table (DynamoDB <c>DescribeTable</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The description of the table.</returns>
    /// <remarks>
    /// The result is eventually consistent: right after <c>CreateTable</c> the table may still be reported as missing. The item
    /// count and the size are approximations that DynamoDB refreshes about every six hours. Poll until
    /// <see cref="TableDescription.TableStatus"/> is <see cref="TableStatus.Active"/> before using a new table.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table name is malformed.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DescribeTable</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<DescribeTableResponse> DescribeTableAsync(this DynamoDbClient client, DescribeTableRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<DescribeTableOperation, DescribeTableRequest, DescribeTableResponse>(request, cancellationToken);

    /// <summary>
    /// Creates a table (DynamoDB <c>CreateTable</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table name, its key, its indexes and its settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="CreateTableResponse.TableDescription"/> shows the new table, normally in the state
    /// <see cref="TableStatus.Creating"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Table creation is asynchronous. The table accepts requests only when it is <see cref="TableStatus.Active"/>, which takes
    /// seconds to minutes (longer with global secondary indexes): poll <see cref="DescribeTableAsync"/> with a delay of a second or
    /// more until then. Reading or writing earlier fails with <see cref="ResourceNotFoundException"/> or
    /// <see cref="ResourceInUseException"/>. Creating a table is not idempotent: a request that fails with a timeout or a network
    /// error may have created the table, and the client retries such requests, so the retry can fail with
    /// <see cref="ResourceInUseException"/> although this very call created the table. Check with <see cref="DescribeTableAsync"/>
    /// before treating it as an error, or set <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1.
    /// </para>
    /// <para>
    /// An account can create only so many tables at the same time (50 in the state <see cref="TableStatus.Creating"/>, and a limit
    /// on all tables per region), and DynamoDB limits how fast tables are created and deleted; exceeding it throws
    /// <see cref="LimitExceededException"/>. The primary key, the local secondary indexes and the table name cannot be changed later.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The request has an undefined enum value, or <see cref="KeyType.Unknown"/>, <see cref="ScalarAttributeType.Unknown"/> or <see cref="ProjectionType.Unknown"/> where one is required (raised as the faulted task).</exception>
    /// <exception cref="ResourceInUseException">A table with that name already exists or is being created.</exception>
    /// <exception cref="LimitExceededException">The account has too many tables, or creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table name is malformed, an attribute is defined but no key uses it, capacity is set together with on-demand billing, or an index name is used twice.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:CreateTable</c>.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<CreateTableResponse> CreateTableAsync(this DynamoDbClient client, CreateTableRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<CreateTableOperation, CreateTableRequest, CreateTableResponse>(request, cancellationToken);

    /// <summary>
    /// Deletes a table and all its items (DynamoDB <c>DeleteTable</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="DeleteTableResponse.TableDescription"/> shows the table, normally in the state
    /// <see cref="TableStatus.Deleting"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Deletion is asynchronous and cannot be undone, except by restoring a backup. The table stays visible in the state
    /// <see cref="TableStatus.Deleting"/> for a while: poll <see cref="DescribeTableAsync"/> until it fails with
    /// <see cref="ResourceNotFoundException"/> before creating a table of the same name again. A table with deletion protection
    /// (<see cref="TableDescription.DeletionProtectionEnabled"/>) cannot be deleted; turn the protection off first. Its streams
    /// stay readable for 24 hours.
    /// </para>
    /// <para>
    /// Deleting is safe to repeat in the sense that the end state is the same, but a repeat after a lost response can fail with
    /// <see cref="ResourceNotFoundException"/> or <see cref="ResourceInUseException"/> because the table is already going away.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ResourceInUseException">The table is being created or updated, or is already being deleted.</exception>
    /// <exception cref="LimitExceededException">The account creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table has deletion protection or the name is malformed.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DeleteTable</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<DeleteTableResponse> DeleteTableAsync(this DynamoDbClient client, DeleteTableRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<DeleteTableOperation, DeleteTableRequest, DeleteTableResponse>(request, cancellationToken);

    /// <summary>
    /// Turns Time to Live on a table on or off (DynamoDB <c>UpdateTimeToLive</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table and the new setting.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response, which repeats the setting DynamoDB accepted.</returns>
    /// <remarks>
    /// <para>
    /// With Time to Live on, DynamoDB deletes items whose expiry attribute (a number holding epoch seconds) is in the past. The
    /// deletion is a background process: it lags the expiry, typically by up to a few days, so expired items are still returned
    /// by reads, queries and scans until DynamoDB gets to them. Filter on the attribute where that matters. Deleted items appear in
    /// the stream of the table as removals made by the DynamoDB service.
    /// </para>
    /// <para>
    /// The change itself takes up to an hour to take effect, and DynamoDB rejects further changes to the same table during that
    /// hour with a <see cref="ValidationException"/>. Setting the value that is already in effect is rejected as well, which makes a
    /// retry after a lost response fail although the first attempt worked.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ResourceInUseException">The table is being created, updated or deleted.</exception>
    /// <exception cref="LimitExceededException">Too many table operations are running in the account.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the setting is already in effect or was changed less than an hour ago.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:UpdateTimeToLive</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<UpdateTimeToLiveResponse> UpdateTimeToLiveAsync(this DynamoDbClient client, UpdateTimeToLiveRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<UpdateTimeToLiveOperation, UpdateTimeToLiveRequest, UpdateTimeToLiveResponse>(request, cancellationToken);

    /// <summary>
    /// Lists the names of the tables of the account in the region, sorted (DynamoDB <c>ListTables</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The optional paging settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One page of at most 100 names. Continue with <see cref="ListTablesResponse.LastEvaluatedTableName"/> until it is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The call lists names only; use <see cref="DescribeTableAsync"/> for a table. The list is eventually consistent: a table that was
    /// just created may be missing and one that was just deleted may still appear. This method returns a single page; the call is safe
    /// to repeat.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because <see cref="ListTablesRequest.Limit"/> is not between 1 and 100.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:ListTables</c>.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<ListTablesResponse> ListTablesAsync(this DynamoDbClient client, ListTablesRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<ListTablesOperation, ListTablesRequest, ListTablesResponse>(request, cancellationToken);

    /// <summary>
    /// Reads the Time to Live setting of a table (DynamoDB <c>DescribeTimeToLive</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The state of Time to Live and the name of the expiry attribute.</returns>
    /// <remarks>
    /// A table that never had Time to Live is reported as <see cref="TimeToLiveStatus.Disabled"/>. A change made with
    /// <see cref="UpdateTimeToLiveAsync"/> shows up as <see cref="TimeToLiveStatus.Enabling"/> or <see cref="TimeToLiveStatus.Disabling"/>
    /// for up to an hour. The call is safe to repeat.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table name is malformed.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DescribeTimeToLive</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<DescribeTimeToLiveResponse> DescribeTimeToLiveAsync(this DynamoDbClient client, DescribeTimeToLiveRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<DescribeTimeToLiveOperation, DescribeTimeToLiveRequest, DescribeTimeToLiveResponse>(request, cancellationToken);

    /// <summary>
    /// Changes the billing mode, capacity, stream, global secondary indexes, storage class or deletion protection of a table (DynamoDB
    /// <c>UpdateTable</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The table and the changes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The response. <see cref="UpdateTableResponse.TableDescription"/> shows the table, normally in the state
    /// <see cref="TableStatus.Updating"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Updating a table is asynchronous. While the table is <see cref="TableStatus.Updating"/> (and while a new index is
    /// <see cref="IndexStatus.Creating"/>) DynamoDB rejects further structural changes with <see cref="ResourceInUseException"/>:
    /// poll <see cref="DescribeTableAsync"/> with a delay of a second or more until the table is <see cref="TableStatus.Active"/>
    /// again. Reads and writes keep working meanwhile. A new global secondary index is allocated and then filled from the table, which takes
    /// minutes on an empty table and hours on a large one, and the table cannot be deleted until it is done; it can be queried when its <see cref="GlobalSecondaryIndexDescription.IndexStatus"/> is
    /// <see cref="IndexStatus.Active"/>.
    /// </para>
    /// <para>
    /// A repeated request is not harmless: after a timeout or a network error the client retries, and a retry of a change that
    /// already took effect fails with <see cref="ResourceInUseException"/> or <see cref="ValidationException"/> (for example creating
    /// an index that exists, or setting the billing mode the table has). Check with <see cref="DescribeTableAsync"/> before treating
    /// it as an error, or set <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1. Which changes DynamoDB allows together, and how
    /// often, is described on <see cref="UpdateTableRequest"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The request has an undefined enum value, or <see cref="ScalarAttributeType.Unknown"/>, <see cref="KeyType.Unknown"/> or <see cref="ProjectionType.Unknown"/> where one is required (raised as the faulted task).</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ResourceInUseException">The table or an index is being created, updated or deleted.</exception>
    /// <exception cref="LimitExceededException">The account has too many tables or indexes, or changes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it changes nothing, sets the billing mode the table already has, lowers capacity too often, combines changes that are not allowed together or has an index with an attribute that is not defined.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:UpdateTable</c> on the table.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<UpdateTableResponse> UpdateTableAsync(this DynamoDbClient client, UpdateTableRequest request, CancellationToken cancellationToken = default) =>
        CoreOf(client).ExecuteAsync<UpdateTableOperation, UpdateTableRequest, UpdateTableResponse>(request, cancellationToken);

    private static ClientCore CoreOf(DynamoDbClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Core;
    }
}
