namespace DynaSharp.Requests;

/// <summary>
/// One page of the result of <see cref="QueryRequest"/>.
/// </summary>
/// <remarks>
/// A page holds at most 1 MB of read data. Check <see cref="LastEvaluatedKey"/>, not <see cref="Count"/>, to find out whether
/// there are more pages.
/// </remarks>
public sealed record QueryResponse
{
    /// <summary>
    /// Gets the items of this page that match the key condition and the filter, in sort key order. Empty when nothing matched and
    /// when the request used <see cref="Select.Count"/>. The list and its items are read-only; do not cast them to change them.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> Items { get; init; } = [];

    /// <summary>Gets the number of items in this page, after the filter. With <see cref="Select.Count"/> the only way to see how many matched.</summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets the number of items this page evaluated, before the filter. A high value next to a low <see cref="Count"/> means the
    /// query reads much more than it returns. Equal to <see cref="Count"/> without a filter.
    /// </summary>
    public int ScannedCount { get; init; }

    /// <summary>
    /// Gets the key of the last item evaluated, to pass as <see cref="QueryRequest.ExclusiveStartKey"/> to read the next page.
    /// <see langword="null"/> on the last page, never an empty dictionary. A key does not guarantee that more items exist: the next
    /// page can be empty. Only <see langword="null"/> means the end.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? LastEvaluatedKey { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="QueryRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }
}
