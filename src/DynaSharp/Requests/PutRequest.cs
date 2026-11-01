namespace DynaSharp.Requests;

/// <summary>
/// Writes an item as part of a <see cref="WriteRequest"/>, with the effect of <c>PutItem</c> without a condition.
/// </summary>
public sealed record PutRequest
{
    /// <summary>
    /// Gets the item, including all key attributes. It replaces an existing item with the same key entirely. At most 400 KB; string
    /// and binary values must not be empty and sets must not be empty.
    /// </summary>
    public required IReadOnlyDictionary<string, AttributeValue> Item { get; init; }
}
