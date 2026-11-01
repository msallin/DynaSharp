using System.Collections.Frozen;

namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="BatchWriteItemRequest"/>: which writes were not processed, and the optional metrics.
/// </summary>
/// <remarks>
/// A successful call does not mean that every write was applied. When a table is throttled or DynamoDB fails internally for some
/// writes, it applies the others and returns the rest in <see cref="UnprocessedItems"/>. The caller must send those writes again
/// (<see cref="BatchWriteItemRequest.RequestItems"/> accepts the map as it is), preferably after a growing wait. A call fails as a
/// whole only when none of the writes could be processed.
/// </remarks>
public sealed record BatchWriteItemResponse
{
    /// <summary>
    /// Gets the writes that were not processed, in the form of the request, so that they can be sent as
    /// <see cref="BatchWriteItemRequest.RequestItems"/>. Empty when everything was written; never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> UnprocessedItems { get; init; } =
        FrozenDictionary<string, IReadOnlyList<WriteRequest>>.Empty;

    /// <summary>
    /// Gets the item collections that the processed writes modified, by table, when the request asked for it and the tables have
    /// local secondary indexes. Empty otherwise; never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> ItemCollectionMetrics { get; init; } =
        FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;

    /// <summary>Gets the write capacity consumed, one entry per table, when the request asked for it. Empty otherwise; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; init; } = [];
}
