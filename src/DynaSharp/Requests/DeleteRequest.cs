namespace DynaSharp.Requests;

/// <summary>
/// Deletes an item as part of a <see cref="WriteRequest"/>, with the effect of <c>DeleteItem</c> without a condition.
/// </summary>
public sealed record DeleteRequest
{
    /// <summary>
    /// Gets the full primary key of the item: the partition key and, if the table has one, the sort key. Deleting an item that
    /// does not exist is not an error.
    /// </summary>
    public required IReadOnlyDictionary<string, AttributeValue> Key { get; init; }
}
