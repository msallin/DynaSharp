namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="GetItemRequest"/>.
/// </summary>
public sealed record GetItemResponse
{
    /// <summary>Gets the item, or <see langword="null"/> if no item has the given key.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? Item { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="GetItemRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }
}
