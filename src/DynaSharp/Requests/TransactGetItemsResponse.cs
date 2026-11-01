namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="TransactGetItemsRequest"/>.
/// </summary>
public sealed record TransactGetItemsResponse
{
    /// <summary>
    /// Gets the results, one per action of the request and in the same order: the result at an index belongs to the action at that
    /// index. Never <see langword="null"/>. The list is read-only; do not cast it to change it.
    /// </summary>
    public IReadOnlyList<ItemResponse> Responses { get; init; } = [];

    /// <summary>Gets the read capacity consumed, one entry per table, when the request asked for it. Empty otherwise; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; init; } = [];
}
