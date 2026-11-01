namespace DynaSharp.Requests;

/// <summary>
/// Puts or deletes items in one or more tables in a single call (DynamoDB <c>BatchWriteItem</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the low-level request: the library neither splits a request that is too large nor repeats the writes DynamoDB leaves
/// unprocessed. A call can contain at most 25 writes in all and transmit at most 16 MB; an item can be at most 400 KB. DynamoDB
/// rejects the whole request, as a <see cref="ValidationException"/>, when it exceeds a limit, when it addresses a table that does
/// not exist, when a key does not match the key schema of its table, or when it contains two writes for the same item (also a put
/// and a delete of one item).
/// </para>
/// <para>
/// Each write is atomic, the batch as a whole is not. A batch cannot update items (a put replaces the item), and its writes cannot
/// have conditions. Use <see cref="TransactWriteItemsRequest"/> for either.
/// </para>
/// </remarks>
public sealed record BatchWriteItemRequest
{
    /// <summary>
    /// Gets the writes, by table name (or table ARN). Each table can occur once. DynamoDB rejects an empty map and an empty list for a
    /// table.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> RequestItems { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="BatchWriteItemResponse.ConsumedCapacity"/>, one entry per table.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }

    /// <summary>
    /// Gets whether to report the size of the item collections that were modified in
    /// <see cref="BatchWriteItemResponse.ItemCollectionMetrics"/>. Only tables with a local secondary index have item collections.
    /// </summary>
    public ReturnItemCollectionMetrics ReturnItemCollectionMetrics { get; init; }
}
