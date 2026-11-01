namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="UpdateItemRequest"/>.
/// </summary>
public sealed record UpdateItemResponse
{
    /// <summary>
    /// Gets the attributes selected by <see cref="UpdateItemRequest.ReturnValues"/>, before or after the update: the whole item for
    /// <see cref="ReturnValue.AllOld"/> and <see cref="ReturnValue.AllNew"/>, only the changed attributes for
    /// <see cref="ReturnValue.UpdatedOld"/> and <see cref="ReturnValue.UpdatedNew"/>. <see langword="null"/> when nothing was
    /// requested, and for the <c>Old</c> variants when the item did not exist before.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Attributes { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="UpdateItemRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// An update consumes write capacity for the larger of the item sizes before and after.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }

    /// <summary>
    /// Gets the size estimate of the item collection, if requested with <see cref="UpdateItemRequest.ReturnItemCollectionMetrics"/>
    /// and the table has a local secondary index; otherwise <see langword="null"/>.
    /// </summary>
    public ItemCollectionMetrics? ItemCollectionMetrics { get; init; }
}
