namespace DynaSharp;

/// <summary>
/// The size estimate of an item collection that a write operation modified, reported when the request asked for it with
/// <see cref="ReturnItemCollectionMetrics.Size"/>.
/// </summary>
/// <remarks>
/// An item collection is the set of all items that share a partition key value, in the table and in its local secondary indexes.
/// DynamoDB limits it to 10 GB and rejects writes that would exceed that with an
/// <see cref="ItemCollectionSizeLimitExceededException"/>. The metrics are only returned for tables that have a local secondary
/// index.
/// </remarks>
public sealed record ItemCollectionMetrics
{
    /// <summary>Gets the partition key of the item collection, which is the partition key of the item that was written.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? ItemCollectionKey { get; init; }

    /// <summary>
    /// Gets a lower and an upper bound, in gigabytes, for the size of the item collection: two elements. The estimate includes
    /// the items of the table and all attributes projected into the local secondary indexes. It changes over time and is not
    /// precise; use it to notice that a collection approaches the 10 GB limit.
    /// </summary>
    public IReadOnlyList<double>? SizeEstimateRangeGB { get; init; }
}
