namespace DynaSharp.Requests;

/// <summary>
/// One page of the result of <see cref="ScanRequest"/>.
/// </summary>
/// <remarks>
/// A page holds at most 1 MB of read data. Check <see cref="LastEvaluatedKey"/>, not <see cref="Count"/>, to find out whether
/// there are more pages. <see cref="Count"/> and <see cref="ScannedCount"/> describe this page only, not the table.
/// </remarks>
public sealed record ScanResponse
{
    /// <summary>
    /// Gets the items of this page that match the filter, in no particular order. Empty when nothing matched and when the request
    /// used <see cref="SelectKind.Count"/>. The list and its items are read-only; do not cast them to change them.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> Items { get; init; } = [];

    /// <summary>Gets the number of items in this page, after the filter. With <see cref="SelectKind.Count"/> the only way to see how many matched.</summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets the number of items this page evaluated, before the filter. A high value next to a low <see cref="Count"/> means the
    /// scan reads much more than it returns. Equal to <see cref="Count"/> without a filter.
    /// </summary>
    public int ScannedCount { get; init; }

    /// <summary>
    /// Gets the key of the last item evaluated, to pass as <see cref="ScanRequest.ExclusiveStartKey"/> to read the next page of the
    /// same segment. <see langword="null"/> on the last page, never an empty dictionary. A key does not guarantee that more items
    /// exist: the next page can be empty. Only <see langword="null"/> means the end.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? LastEvaluatedKey { get; init; }

    /// <summary>
    /// Gets the capacity the call consumed. Non-null only when the request set <see cref="ScanRequest.ReturnConsumedCapacity"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ConsumedCapacity? ConsumedCapacity { get; init; }
}
