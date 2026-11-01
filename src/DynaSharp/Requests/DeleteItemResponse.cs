namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="DeleteItemRequest"/>.
/// </summary>
public sealed record DeleteItemResponse
{
    /// <summary>
    /// Gets the deleted item when <see cref="DeleteItemRequest.ReturnValues"/> was <see cref="ReturnValue.AllOld"/> and an item
    /// existed; otherwise <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Attributes { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="DeleteItemRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }

    /// <summary>
    /// Gets the size estimate of the item collection, if requested with <see cref="DeleteItemRequest.ReturnItemCollectionMetrics"/>
    /// and the table has a local secondary index; otherwise <see langword="null"/>.
    /// </summary>
    public ItemCollectionMetrics? ItemCollectionMetrics { get; init; }
}
