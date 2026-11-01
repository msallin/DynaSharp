namespace DynaSharp.Requests;

/// <summary>
/// The result of one <see cref="TransactGetItem"/>.
/// </summary>
public sealed record ItemResponse
{
    /// <summary>
    /// Gets the attributes of the item, restricted to the projection if the action had one. <see langword="null"/> when no item has
    /// the key: a missing item is not an error. An existing item without any projected attribute is an empty dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Item { get; init; }
}
