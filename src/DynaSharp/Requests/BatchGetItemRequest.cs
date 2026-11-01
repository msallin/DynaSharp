namespace DynaSharp.Requests;

/// <summary>
/// Reads items from one or more tables by primary key in a single call (DynamoDB <c>BatchGetItem</c>).
/// </summary>
/// <remarks>
/// This is the low-level request: the library neither splits a request that is too large nor repeats the keys DynamoDB leaves
/// unprocessed. A call can name at most 100 keys in all and returns at most 16 MB; an item can be at most 400 KB. DynamoDB rejects
/// a request that names more than 100 keys, or the same key twice, with a <see cref="ValidationException"/>.
/// </remarks>
public sealed record BatchGetItemRequest
{
    /// <summary>
    /// Gets the keys to read, by table name (or table ARN). Each table can occur once. DynamoDB rejects an empty map.
    /// </summary>
    public required IReadOnlyDictionary<string, KeysAndAttributes> RequestItems { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="BatchGetItemResponse.ConsumedCapacity"/>, one entry per table.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }
}
