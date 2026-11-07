using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Testing;

/// <summary>
/// A canned HTTP response for a <see cref="StubHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The body is sent as <c>application/x-amz-json-1.0</c>, the content type of DynamoDB. A test says what DynamoDB answers and leaves the JSON to this class:
/// <see cref="Item{T}"/> for <c>GetItem</c>, <see cref="Items{T}(IEnumerable{T}, int?)"/> for a page of <c>Query</c> or <c>Scan</c>, <see cref="Attributes{T}"/> for
/// a write that returns values, <see cref="ConditionalCheckFailed{T}"/> and <see cref="TransactionCanceled(ReadOnlySpan{CancellationReason})"/> for failed conditions,
/// and <see cref="Unprocessed(string, ReadOnlySpan{WriteRequest})"/> with <see cref="BatchItems{T}"/> for batches. They take an <see cref="DynaSharp.Item"/> as well as a
/// generated entity, and they write through the code the entity is stored with, so a canned item cannot drift from the mapping. An <see cref="DynaSharp.Item"/> can hold any
/// attributes, which is how a test gives the partial answer of <c>UPDATED_NEW</c>. <see cref="Count(int, int?)"/> answers a <c>Select: COUNT</c>.
/// </para>
/// <para>
/// Table administration has <see cref="Table(string, TableStatus, ReadOnlySpan{Requests.GlobalSecondaryIndexDescription})"/> and <see cref="Table{T}(string, TableStatus)"/> for <c>DescribeTable</c>, <see cref="TableCreated"/>, <see cref="TableUpdated"/>, <see cref="TableDeleted"/>,
/// <see cref="TablesListed(ReadOnlySpan{string})"/> for <c>ListTables</c>, <see cref="TimeToLive"/>, <see cref="TimeToLiveUpdated"/>, <see cref="ResourceNotFound"/> and <see cref="ResourceInUse"/>. DynamoDB Streams has <see cref="StreamDescription(string, ReadOnlySpan{Streams.Shard})"/>,
/// <see cref="ShardIterator"/> and <see cref="Records"/>. What such an answer holds beyond its arguments is fixed: the region of <see cref="StubClient"/>, an account, a creation time of 2026-01-01, so a test can
/// compare it and no answer depends on the clock.
/// </para>
/// <para>
/// A response holds what DynamoDB sends by default. <c>ConsumedCapacity</c> and <c>ItemCollectionMetrics</c> come only when a request asks for them, so a test that reads them
/// writes the body with <see cref="Json"/>, as it does for <c>TransactGetItems</c> and anything unusual. <see cref="DynamoDbError"/> makes every other error.
/// </para>
/// <para>
/// <b>An answer is for its operations.</b> A response of a factory is the answer to the operations its body is written for, as each factory says, and a <see cref="StubHandler"/> that hands it to a request
/// of another operation throws. The client would read most such bodies without complaint: the answer of an <c>UpdateTimeToLive</c> taken by a <c>GetItem</c> reads as no item, and a test that expects none
/// passes for the wrong reason. The errors go by what they are for: <see cref="ConditionalCheckFailed()"/> answers a <c>PutItem</c>, an <c>UpdateItem</c> or a <c>DeleteItem</c>, and
/// <see cref="TransactionCanceled(ReadOnlySpan{TransactionFailure})"/> a <c>TransactWriteItems</c> or a <c>TransactGetItems</c>. <see cref="ResourceNotFound"/> and <see cref="ResourceInUse"/> have the
/// shape of an error that many operations return, and fit any, as do <see cref="Json"/>, <see cref="DynamoDbError"/> and <c>new StubResponse()</c>, whose body the test writes or which has none.
/// </para>
/// </remarks>
public sealed partial class StubResponse
{
    private const string ErrorTypePrefix = "com.amazonaws.dynamodb.v20120810#";
    private const string ConditionFailedMessage = "The conditional request failed";

    private byte[]? _bodyBytes;

    /// <summary>Gets the HTTP status code. Defaults to <see cref="HttpStatusCode.OK"/>.</summary>
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    /// <summary>Gets the response body, normally a JSON document. Defaults to an empty JSON object. Can be any text to simulate a broken response, for example the HTML of a proxy.</summary>
    public string Body { get; init; } = "{}";

    /// <summary>
    /// Gets additional response headers by name, for example <c>x-amzn-RequestId</c>. Headers that belong to the content, such as
    /// <c>Content-Type</c>, replace the defaults.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Gets the operations this response answers, as <see cref="RecordedRequest.Operation"/> names them, or <see langword="null"/> if it fits any.</summary>
    internal IReadOnlyList<string>? Operations { get; private init; }

    /// <summary>Creates a response with a JSON body.</summary>
    /// <param name="json">The body, for example <c>{"Item":{"pk":{"S":"a"}}}</c>.</param>
    /// <param name="statusCode">The HTTP status code. DynamoDB answers successful requests with 200.</param>
    /// <param name="headers">Additional response headers, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public static StubResponse Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null) =>
        new() { StatusCode = statusCode, Body = json, Headers = headers };

    /// <summary>Creates an error response in the shape DynamoDB uses: <c>{"__type":"com.amazonaws.dynamodb.v20120810#Code","message":"..."}</c>.</summary>
    /// <param name="errorCode">The error code without namespace, for example <c>ResourceNotFoundException</c> or <c>ThrottlingException</c>.</param>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code. DynamoDB answers client errors with 400 and server errors with 500.</param>
    /// <param name="requestId">The value of the <c>x-amzn-RequestId</c> header, or <see langword="null"/> for none.</param>
    /// <returns>The response.</returns>
    public static StubResponse DynamoDbError(string errorCode, string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest, string? requestId = null) => new()
    {
        StatusCode = statusCode,
        Body = Render(json =>
        {
            json.WriteString("__type", ErrorTypePrefix + errorCode);
            json.WriteString("message", message);
        }),
        Headers = requestId is null ? null : new Dictionary<string, string> { [WireProtocol.RequestIdHeaderName] = requestId },
    };

    /// <summary>Creates the error of a table, an index or a stream that does not exist, or of a table that is not active yet for a call that needs it to be.</summary>
    /// <returns>The response: HTTP 400 with <c>ResourceNotFoundException</c>.</returns>
    public static StubResponse ResourceNotFound() => DynamoDbError("ResourceNotFoundException", "Requested resource not found");

    /// <summary>Creates the error of a table that cannot change now: it exists already on a <c>CreateTable</c>, or it is <c>CREATING</c>, <c>UPDATING</c> or <c>DELETING</c>.</summary>
    /// <returns>The response: HTTP 400 with <c>ResourceInUseException</c>.</returns>
    public static StubResponse ResourceInUse() => DynamoDbError("ResourceInUseException", "Attempt to change a resource which is still in use");

    /// <summary>
    /// Creates the answer to a <c>GetItem</c>: <c>{"Item":{...}}</c>, or <c>{}</c> if there is no item, which is how DynamoDB says that it found nothing.
    /// </summary>
    /// <typeparam name="T">The type of the item: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="item">The item, or <see langword="null"/> for a miss. A struct cannot be null: the miss of a struct entity is <c>new StubResponse()</c>.</param>
    /// <returns>The response.</returns>
    public static StubResponse Item<T>(T? item)
        where T : IDynamoDbDocument<T> => Single("Item", item, For.GetItem);

    /// <summary>
    /// Creates a page of a <c>Query</c> or <c>Scan</c> that ends the read, with <c>Count</c> filled in. <c>ScannedCount</c> is the same unless a test says that a filter dropped items.
    /// </summary>
    /// <remarks>
    /// A generated entity is written with its keys, as a <c>Put</c> writes it. An <see cref="DynaSharp.Item"/> is written as it stands, and a read of an entity restores what its keys hold from the item: a
    /// <c>required</c> input of a key that the item lacks throws <see cref="DynamoDbMappingException"/>, which names the key. So an <see cref="DynaSharp.Item"/> for such an entity holds the keys as well, for which
    /// <see cref="KeyOf{T}"/> gives the attributes.
    /// </remarks>
    /// <typeparam name="T">The type of the items: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="items">The items, which may be none.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for as many as <paramref name="items"/> holds.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scannedCount"/> is below the number of items, which DynamoDB never sends.</exception>
    public static StubResponse Items<T>(IEnumerable<T> items, int? scannedCount = null)
        where T : IDynamoDbDocument<T> => Page(items, null, scannedCount);

    /// <summary>
    /// Creates a page that more pages follow: the read goes on from <paramref name="lastEvaluatedKey"/>, also when a filter left the page empty. A fan-out also takes this key as a hint for its merge,
    /// so a made-up sort key reorders the items there: use the overload that takes an <see cref="IKey"/>.
    /// </summary>
    /// <typeparam name="T">The type of the items: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="items">The items, which may be none.</param>
    /// <param name="lastEvaluatedKey">The key of the last item DynamoDB read, as <see cref="KeyOf{T}"/> makes it from an entity.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for as many as <paramref name="items"/> holds.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="lastEvaluatedKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scannedCount"/> is below the number of items, which DynamoDB never sends.</exception>
    public static StubResponse Items<T>(IEnumerable<T> items, Item lastEvaluatedKey, int? scannedCount = null)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(lastEvaluatedKey);
        return Page(items, lastEvaluatedKey, scannedCount);
    }

    /// <summary>Creates a page that more pages follow, with a generated key: <c>Items(orders, Order.Keys.Of("alice", 7))</c>.</summary>
    /// <typeparam name="T">The type of the items: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="items">The items, which may be none.</param>
    /// <param name="lastEvaluatedKey">The key of the last item DynamoDB read.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for as many as <paramref name="items"/> holds.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="lastEvaluatedKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scannedCount"/> is below the number of items, which DynamoDB never sends.</exception>
    public static StubResponse Items<T>(IEnumerable<T> items, IKey lastEvaluatedKey, int? scannedCount = null)
        where T : IDynamoDbDocument<T> => Page(items, KeyOf(lastEvaluatedKey), scannedCount);

    /// <summary>
    /// Creates a page of a <c>Query</c> or <c>Scan</c> with <c>Select: COUNT</c> that ends the read: <c>{"Count":3,"ScannedCount":3}</c>, without <c>Items</c>, as <c>CountAsync</c> gets it. <c>ScannedCount</c> is the same unless a test says that a
    /// filter dropped items.
    /// </summary>
    /// <param name="count">How many items are in the page after the filter.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for <paramref name="count"/>.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative, or <paramref name="scannedCount"/> is below it, which DynamoDB never sends.</exception>
    public static StubResponse Count(int count, int? scannedCount = null) => CountPage(count, null, scannedCount);

    /// <summary>Creates a page of a count that more pages follow: <c>CountAsync</c> goes on from <paramref name="lastEvaluatedKey"/> and adds the pages up.</summary>
    /// <param name="count">How many items are in the page after the filter.</param>
    /// <param name="lastEvaluatedKey">The key of the last item DynamoDB read, as <see cref="KeyOf{T}"/> makes it from an entity.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for <paramref name="count"/>.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lastEvaluatedKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative, or <paramref name="scannedCount"/> is below it, which DynamoDB never sends.</exception>
    public static StubResponse Count(int count, Item lastEvaluatedKey, int? scannedCount = null)
    {
        ArgumentNullException.ThrowIfNull(lastEvaluatedKey);
        return CountPage(count, lastEvaluatedKey, scannedCount);
    }

    /// <summary>Creates a page of a count that more pages follow, with a generated key.</summary>
    /// <param name="count">How many items are in the page after the filter.</param>
    /// <param name="lastEvaluatedKey">The key of the last item DynamoDB read.</param>
    /// <param name="scannedCount">How many items DynamoDB read before the filter, or <see langword="null"/> for <paramref name="count"/>.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lastEvaluatedKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative, or <paramref name="scannedCount"/> is below it, which DynamoDB never sends.</exception>
    public static StubResponse Count(int count, IKey lastEvaluatedKey, int? scannedCount = null) => CountPage(count, KeyOf(lastEvaluatedKey), scannedCount);

    /// <summary>
    /// Creates the answer to a <c>PutItem</c>, <c>UpdateItem</c> or <c>DeleteItem</c> that asked for values: <c>{"Attributes":{...}}</c>, or <c>{}</c> if there are none, as for
    /// <c>ReturnOld()</c> on a key that held no item. The answer to a write that asked for nothing is <c>new StubResponse()</c>.
    /// </summary>
    /// <typeparam name="T">The type of the attributes: a generated entity for a whole item, or <see cref="DynaSharp.Item"/>, also for the few attributes of <c>Return(attr)</c>.</typeparam>
    /// <param name="attributes">The attributes, or <see langword="null"/> for none.</param>
    /// <returns>The response.</returns>
    public static StubResponse Attributes<T>(T? attributes)
        where T : IDynamoDbDocument<T> => Single("Attributes", attributes, For.Write);

    /// <summary>Creates the error of a write whose condition did not hold, without the item: what DynamoDB sends unless the request set <c>ReturnOldOnFailure()</c>.</summary>
    /// <returns>The response: HTTP 400 with <c>ConditionalCheckFailedException</c>.</returns>
    public static StubResponse ConditionalCheckFailed() => ConditionalCheckFailed<Item>(null);

    /// <summary>Creates the error of a write whose condition did not hold, with the item the condition saw, as <c>ReturnOldOnFailure()</c> gets it.</summary>
    /// <typeparam name="T">The type of the item: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="item">The item, or <see langword="null"/> if none existed under the key.</param>
    /// <returns>The response: HTTP 400 with <c>ConditionalCheckFailedException</c>.</returns>
    public static StubResponse ConditionalCheckFailed<T>(T? item)
        where T : IDynamoDbDocument<T> => new()
        {
            StatusCode = HttpStatusCode.BadRequest,
            Operations = For.Write,
            Body = Render(json =>
            {
                json.WriteString("__type", ErrorTypePrefix + "ConditionalCheckFailedException");
                if (item is not null)
                {
                    WriteDocument(json, "Item", item);
                }

                json.WriteString("message", ConditionFailedMessage);
            }),
        };

    /// <summary>
    /// Creates the error of a cancelled transaction, with one reason per action in the order of the actions, so that <see cref="TransactionCanceledException.ReasonFor"/> finds the
    /// reason of a marker: <c>TransactionCanceled(TransactionFailure.None, TransactionFailure.ConditionalCheckFailed)</c> for a transaction whose second action failed.
    /// </summary>
    /// <param name="reasons">The reasons. <see cref="Reason"/> says what each one sends.</param>
    /// <returns>The response: HTTP 400 with <c>TransactionCanceledException</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A reason is <see cref="TransactionFailure.Unknown"/> or not a member of the enum.</exception>
    public static StubResponse TransactionCanceled(params ReadOnlySpan<TransactionFailure> reasons)
    {
        var mapped = new CancellationReason[reasons.Length];
        for (var i = 0; i < reasons.Length; i++)
        {
            mapped[i] = Reason(reasons[i]);
        }

        return TransactionCanceled((ReadOnlySpan<CancellationReason>)mapped);
    }

    /// <summary>
    /// Creates the error of a cancelled transaction from whole reasons: <see cref="Reason"/>, <see cref="ConditionalCheckFailedReason{T}"/> for one that holds the item
    /// <see cref="TransactionCanceledException.ItemFor"/> returns, or a <see cref="CancellationReason"/> of your own for a code this library does not know.
    /// </summary>
    /// <param name="reasons">The reasons, one per action in the order of the actions.</param>
    /// <returns>The response: HTTP 400 with <c>TransactionCanceledException</c>.</returns>
    public static StubResponse TransactionCanceled(params ReadOnlySpan<CancellationReason> reasons)
    {
        var all = reasons.ToArray();
        return new StubResponse
        {
            StatusCode = HttpStatusCode.BadRequest,
            Operations = For.Transaction,
            Body = Render(json =>
            {
                json.WriteString("__type", ErrorTypePrefix + "TransactionCanceledException");
                json.WriteStartArray("CancellationReasons");
                foreach (var reason in all)
                {
                    WriteReason(json, reason);
                }

                json.WriteEndArray();

                // This error says Message where most others say message, and it repeats the codes.
                json.WriteString("Message", $"Transaction cancelled, please refer cancellation reasons for specific reasons [{string.Join(", ", all.Select(static reason => reason.Code))}]");
            }),
        };
    }

    /// <summary>
    /// Creates the reason of one action as DynamoDB words it. <see cref="TransactionFailure.Throttled"/> stands for two codes and sends <c>ThrottlingError</c>; a
    /// <see cref="CancellationReason"/> of your own sends <c>ProvisionedThroughputExceeded</c>.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The reason.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failure"/> is <see cref="TransactionFailure.Unknown"/>, which has no code, or not a member of the enum.</exception>
    public static CancellationReason Reason(TransactionFailure failure) => failure switch
    {
        // When adding a member of TransactionFailure: add its code and message here, from the API reference.
        TransactionFailure.None => new CancellationReason { Code = "None" },
        TransactionFailure.ConditionalCheckFailed => new CancellationReason { Code = "ConditionalCheckFailed", Message = ConditionFailedMessage },
        TransactionFailure.ItemCollectionSizeLimitExceeded => new CancellationReason { Code = "ItemCollectionSizeLimitExceeded", Message = "Collection size exceeded." },
        TransactionFailure.TransactionConflict => new CancellationReason { Code = "TransactionConflict", Message = "Transaction is ongoing for the item" },
        TransactionFailure.Throttled => new CancellationReason
        {
            Code = "ThrottlingError",
            Message = "Throughput exceeds the current capacity of your table or index. DynamoDB is automatically scaling your table or index so please try again shortly.",
        },
        TransactionFailure.ValidationError => new CancellationReason { Code = "ValidationError", Message = "One or more parameter values were invalid." },
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "This value has no code on the wire. For a code of your own, write a CancellationReason."),
    };

    /// <summary>Creates the reason of an action whose condition did not hold, with the item the condition saw, as an action with <c>ReturnOldOnFailure()</c> gets it.</summary>
    /// <typeparam name="T">The type of the item: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="item">The item, or <see langword="null"/> if none existed under the key.</param>
    /// <returns>The reason.</returns>
    public static CancellationReason ConditionalCheckFailedReason<T>(T? item)
        where T : IDynamoDbDocument<T> =>
        Reason(TransactionFailure.ConditionalCheckFailed) with { Item = item is null ? null : DynaSharp.Item.From(item) };

    /// <summary>
    /// Creates the answer to a <c>BatchWriteItem</c> that left writes unprocessed, on one table: <c>Unprocessed("Orders", StubResponse.Put(order), StubResponse.Delete(key))</c>.
    /// Without writes it is the answer that ends a batch. Writes on several tables are a body for <see cref="Json"/>.
    /// </summary>
    /// <remarks>
    /// The batch sends the writes again by itself, so a test queues what the later rounds get as well, and the wait before a round runs on
    /// <see cref="DynamoDbClientOptions.TimeProvider"/>: see <see cref="StubHandler"/>.
    /// </remarks>
    /// <param name="tableName">The table of the writes.</param>
    /// <param name="writes">The writes DynamoDB did not get to.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    public static StubResponse Unprocessed(string tableName, params ReadOnlySpan<WriteRequest> writes)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        var all = writes.ToArray();
        return Answer(For.BatchWrite, Render(json =>
        {
            json.WriteStartObject("UnprocessedItems");
            if (all.Length > 0)
            {
                json.WriteStartArray(tableName);
                foreach (var write in all)
                {
                    BatchJson.WriteWriteRequest(new DynamoDbWriter(json), write);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }));
    }

    /// <summary>
    /// Creates the answer to a <c>BatchGetItem</c> that read nothing and left keys unprocessed, on one table: <c>Unprocessed("Orders", StubResponse.Keys(key))</c>.
    /// </summary>
    /// <remarks>
    /// DynamoDB sends along how the keys were to be read, and the next round repeats that as it stands. A test that wants to see <c>ConsistentRead</c> or a projection on the next
    /// round sets it: <c>StubResponse.Keys(key) with { ConsistentRead = true }</c>.
    /// </remarks>
    /// <param name="tableName">The table of the keys.</param>
    /// <param name="keys">The keys DynamoDB did not get to.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> or <paramref name="keys"/> is <see langword="null"/>.</exception>
    public static StubResponse Unprocessed(string tableName, KeysAndAttributes keys)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(keys);
        return BatchRead(tableName, Array.Empty<Item>(), keys);
    }

    /// <summary>Creates the answer to a <c>BatchGetItem</c> that read some items and left the other keys unprocessed, on one table.</summary>
    /// <typeparam name="T">The type of the items: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="tableName">The table of the items and the keys.</param>
    /// <param name="keys">The keys DynamoDB did not get to.</param>
    /// <param name="found">The items it read.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/>, <paramref name="keys"/> or <paramref name="found"/> is <see langword="null"/>.</exception>
    public static StubResponse Unprocessed<T>(string tableName, KeysAndAttributes keys, IEnumerable<T> found)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(found);
        return BatchRead(tableName, found, keys);
    }

    /// <summary>Creates the answer to a <c>BatchGetItem</c> that read everything, on one table. Items come in no particular order, and a key without an item is simply absent.</summary>
    /// <typeparam name="T">The type of the items: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="tableName">The table of the items.</param>
    /// <param name="items">The items, which may be none.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> or <paramref name="items"/> is <see langword="null"/>.</exception>
    public static StubResponse BatchItems<T>(string tableName, IEnumerable<T> items)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(items);
        return BatchRead(tableName, items, null);
    }

    /// <summary>Creates a put for <see cref="Unprocessed(string, ReadOnlySpan{WriteRequest})"/>.</summary>
    /// <typeparam name="T">The type of the item: a generated entity or <see cref="DynaSharp.Item"/>.</typeparam>
    /// <param name="item">The item.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public static WriteRequest Put<T>(T item)
        where T : IDynamoDbDocument<T> => new() { PutRequest = new PutRequest { Item = DynaSharp.Item.From(item) } };

    /// <summary>Creates a delete for <see cref="Unprocessed(string, ReadOnlySpan{WriteRequest})"/>.</summary>
    /// <param name="key">The attributes of the key, as <see cref="KeyOf{T}"/> makes them from an entity.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static WriteRequest Delete(Item key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new WriteRequest { DeleteRequest = new DeleteRequest { Key = key } };
    }

    /// <summary>Creates a delete by a generated key for <see cref="Unprocessed(string, ReadOnlySpan{WriteRequest})"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static WriteRequest Delete(IKey key) => Delete(KeyOf(key));

    /// <summary>Puts keys together for <see cref="Unprocessed(string, KeysAndAttributes)"/>.</summary>
    /// <param name="keys">The attributes of each key, as <see cref="KeyOf{T}"/> makes them from an entity.</param>
    /// <returns>The keys.</returns>
    public static KeysAndAttributes Keys(params ReadOnlySpan<Item> keys) => new() { Keys = keys.ToArray() };

    /// <summary>Puts generated keys together for <see cref="Unprocessed(string, KeysAndAttributes)"/>.</summary>
    /// <param name="keys">The generated keys.</param>
    /// <returns>The keys, ready for a response.</returns>
    /// <exception cref="ArgumentNullException">A key is <see langword="null"/>.</exception>
    public static KeysAndAttributes Keys(params ReadOnlySpan<IKey> keys)
    {
        var attributes = new Item[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            attributes[i] = KeyOf(keys[i]);
        }

        return new KeysAndAttributes { Keys = attributes };
    }

    /// <summary>Gets the attributes of a generated key, so that a test can compare it with <see cref="RecordedRequest.ReadKey()"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>A new item that holds the key attributes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static Item KeyOf(IKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ReadAttributes(RenderBytes(json => key.WriteKey(new DynamoDbWriter(json))));
    }

    /// <summary>Gets the key attributes of an entity, as a request for it would carry them.</summary>
    /// <typeparam name="T">The type of the entity.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns>A new item that holds the key attributes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    public static Item KeyOf<T>(T entity)
        where T : IDynamoDbEntity<T>
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        return ReadAttributes(RenderBytes(json => T.WriteKey(new DynamoDbWriter(json), entity)));
    }

    /// <summary>
    /// Names the factory of the answer to an operation, for the failure of a <see cref="StubHandler"/> that took the answer of another, where a test wrote <see cref="Json"/> for it before the factory existed
    /// or reached for the one of a neighbour, such as <see cref="Table(string, TableStatus, ReadOnlySpan{GlobalSecondaryIndexDescription})"/> for an <c>UpdateTable</c>.
    /// </summary>
    /// <param name="operation">The operation of the request, or <see langword="null"/> for a request without <c>X-Amz-Target</c>.</param>
    /// <returns><c>StubResponse.TableUpdated for an UpdateTable</c>, or <see langword="null"/> for an operation without such a factory.</returns>
    internal static string? FactoryFor(string? operation)
    {
        // When adding a factory for an operation whose answer was a Json body until then, name it here as well.
        if (operation == UpdateTableOperation.Name)
        {
            return $"{nameof(StubResponse)}.{nameof(TableUpdated)} for an UpdateTable";
        }

        return operation == ListTablesOperation.Name ? $"{nameof(StubResponse)}.{nameof(TablesListed)} for a ListTables" : null;
    }

    /// <summary>Tells whether this response, which has <see cref="Operations"/>, answers a request of the operation.</summary>
    /// <param name="operation">The operation of the request, or <see langword="null"/> for a request without <c>X-Amz-Target</c>.</param>
    /// <returns><see langword="true"/> if the response names this operation.</returns>
    internal bool Answers(string? operation)
    {
        foreach (var name in Operations!)
        {
            if (name == operation)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Names what this response answers, as a message says it: <c>GetItem</c>, <c>PutItem, UpdateItem or DeleteItem</c>, or <c>any operation</c>.</summary>
    /// <returns>The text.</returns>
    internal string DescribeOperations() => Operations switch
    {
        null => "any operation",
        [var only] => only,
        _ => string.Join(", ", Operations.Take(Operations.Count - 1)) + " or " + Operations[^1],
    };

    internal HttpResponseMessage CreateMessage()
    {
        // Encoded once: a response that answers many requests (DefaultResponse) is not re-encoded each time.
        _bodyBytes ??= Encoding.UTF8.GetBytes(Body);
        var content = new ByteArrayContent(_bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(WireProtocol.ContentType);
        var message = new HttpResponseMessage(StatusCode) { Content = content };
        if (Headers is not null)
        {
            foreach (var (name, value) in Headers)
            {
                if (!message.Headers.TryAddWithoutValidation(name, value))
                {
                    content.Headers.Remove(name);
                    content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        return message;
    }

    // A miss is {}, which fits any operation as a body, and still the answer of the operation the factory is for.
    private static StubResponse Single<T>(string member, T? document, string[] operations)
        where T : IDynamoDbDocument<T> =>
        Answer(operations, document is null ? "{}" : Render(json => WriteDocument(json, member, document)));

    private static StubResponse Answer(string[] operations, string body) => new() { Body = body, Operations = operations };

    // {"Items":[{...},{...}],"Count":2,"ScannedCount":2,"LastEvaluatedKey":{...}}, the members in the order of the recorded responses of real DynamoDB.
    private static StubResponse Page<T>(IEnumerable<T> items, Item? lastEvaluatedKey, int? scannedCount)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] all = [.. items];
        var scanned = scannedCount ?? all.Length;
        ArgumentOutOfRangeException.ThrowIfLessThan(scanned, all.Length, nameof(scannedCount));
        return Answer(For.Page, Render(json =>
        {
            WriteDocuments(json, "Items", all);
            json.WriteNumber("Count", all.Length);
            json.WriteNumber("ScannedCount", scanned);
            if (lastEvaluatedKey is not null)
            {
                WriteDocument(json, "LastEvaluatedKey", lastEvaluatedKey);
            }
        }));
    }

    // {"Count":2,"ScannedCount":2,"LastEvaluatedKey":{...}}: a count has no Items at all, not an empty list.
    private static StubResponse CountPage(int count, Item? lastEvaluatedKey, int? scannedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var scanned = scannedCount ?? count;
        ArgumentOutOfRangeException.ThrowIfLessThan(scanned, count, nameof(scannedCount));
        return Answer(For.Page, Render(json =>
        {
            json.WriteNumber("Count", count);
            json.WriteNumber("ScannedCount", scanned);
            if (lastEvaluatedKey is not null)
            {
                WriteDocument(json, "LastEvaluatedKey", lastEvaluatedKey);
            }
        }));
    }

    // {"Responses":{"Shop":[{...}]},"UnprocessedKeys":{"Shop":{"Keys":[{...}]}}}. Responses names the table of the request also when it found nothing, as "Shop":[].
    private static StubResponse BatchRead<T>(string tableName, IEnumerable<T> found, KeysAndAttributes? keys)
        where T : IDynamoDbDocument<T>
    {
        T[] all = [.. found];
        return Answer(For.BatchGet, Render(json =>
        {
            json.WriteStartObject("Responses");
            WriteDocuments(json, tableName, all);
            json.WriteEndObject();
            json.WriteStartObject("UnprocessedKeys");
            if (keys is not null)
            {
                json.WritePropertyName(tableName);
                BatchJson.WriteKeysAndAttributes(new DynamoDbWriter(json), keys);
            }

            json.WriteEndObject();
        }));
    }

    private static void WriteDocument<T>(Utf8JsonWriter json, string member, T document)
        where T : IDynamoDbDocument<T>
    {
        json.WriteStartObject(member);
        T.WriteAttributes(new DynamoDbWriter(json), document);
        json.WriteEndObject();
    }

    private static void WriteDocuments<T>(Utf8JsonWriter json, string member, T[] documents)
        where T : IDynamoDbDocument<T>
    {
        json.WriteStartArray(member);
        foreach (var document in documents)
        {
            json.WriteStartObject();
            T.WriteAttributes(new DynamoDbWriter(json), document);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    // {"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{...}}, and {"Code":"None"} for an action that caused no problem.
    private static void WriteReason(Utf8JsonWriter json, CancellationReason reason)
    {
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "Code"u8, reason.Code);
        OperationJson.WriteStringIfSet(json, "Message"u8, reason.Message);
        OperationJson.WriteItemIfSet(new DynamoDbWriter(json), "Item"u8, reason.Item);
        json.WriteEndObject();
    }

    private static string Render(Action<Utf8JsonWriter> writeMembers) => Encoding.UTF8.GetString(RenderBytes(writeMembers));

    private static byte[] RenderBytes(Action<Utf8JsonWriter> writeMembers)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = WireJson.CreateWriter(buffer))
        {
            json.WriteStartObject();
            writeMembers(json);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static Item ReadAttributes(byte[] utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        reader.ReadStartItem();
        return AttributeValueConverter.ReadAttributes(ref reader);
    }

    // The operations whose answer a factory writes, which StubHandler compares with the X-Amz-Target of the request.
    // When adding a factory: give it the operations its body is written for here, or leave it without any if it fits every operation, and say so in its docs.
    private static class For
    {
        public static readonly string[] GetItem = [GetItemOperation.Name];
        public static readonly string[] Page = [QueryOperation.Name, ScanOperation.Name];
        public static readonly string[] Write = [PutItemOperation.Name, UpdateItemOperation.Name, DeleteItemOperation.Name];
        public static readonly string[] Transaction = [TransactWriteItemsOperation.Name, TransactGetItemsOperation.Name];
        public static readonly string[] BatchWrite = [BatchWriteItemOperation.Name];
        public static readonly string[] BatchGet = [BatchGetItemOperation.Name];
        public static readonly string[] DescribeTable = [DescribeTableOperation.Name];
        public static readonly string[] CreateTable = [CreateTableOperation.Name];
        public static readonly string[] DeleteTable = [DeleteTableOperation.Name];
        public static readonly string[] UpdateTable = [UpdateTableOperation.Name];
        public static readonly string[] ListTables = [ListTablesOperation.Name];
        public static readonly string[] DescribeTimeToLive = [DescribeTimeToLiveOperation.Name];
        public static readonly string[] UpdateTimeToLive = [UpdateTimeToLiveOperation.Name];
        public static readonly string[] DescribeStream = [DescribeStreamOperation.Name];
        public static readonly string[] GetShardIterator = [GetShardIteratorOperation.Name];
        public static readonly string[] GetRecords = [GetRecordsOperation.Name];
    }
}
