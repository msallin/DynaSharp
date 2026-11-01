namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="PutItemRequest"/>.
/// </summary>
public sealed record PutItemResponse
{
    /// <summary>
    /// Gets the replaced item when <see cref="PutItemRequest.ReturnValues"/> was <see cref="ReturnValue.AllOld"/> and an
    /// item existed; otherwise <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Attributes { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="PutItemRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }

    /// <summary>
    /// Gets the size estimate of the item collection, if requested with
    /// <see cref="PutItemRequest.ReturnItemCollectionMetrics"/> and the table has a local secondary index; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ItemCollectionMetrics? ItemCollectionMetrics { get; init; }
}
