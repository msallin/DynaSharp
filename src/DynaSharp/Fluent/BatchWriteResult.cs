using System.Collections.Frozen;

namespace DynaSharp;

/// <summary>What a batch write returns. Getting this far means every write of the batch was applied: what cannot be written ends in a <see cref="BatchIncompleteException"/>.</summary>
public readonly struct BatchWriteResult
{
    internal BatchWriteResult(int written, int requests, IReadOnlyList<ConsumedCapacity>? consumedCapacity, IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>>? itemCollectionMetrics)
    {
        Written = written;
        Requests = requests;
        ConsumedCapacity = consumedCapacity ?? [];
        ItemCollectionMetrics = itemCollectionMetrics ?? FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;
    }

    /// <summary>Gets how many writes were applied, which is all of them.</summary>
    public int Written { get; }

    /// <summary>Gets how many requests DynamoDB saw, each with a span of its own: one per 25 writes or 16 MB, and one more per round of unprocessed writes. Zero for a batch without writes.</summary>
    public int Requests { get; }

    /// <summary>Gets the capacity the batch consumed, if <c>ReturnCapacity</c> asked for it: one entry per table and request, so the capacity of a table is the sum of its entries. Empty otherwise.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; }

    /// <summary>
    /// Gets the size estimates of the item collections by table, if <c>ReturnMetrics</c> asked for them. DynamoDB sends them for tables with a local
    /// secondary index, and they contain the partition key value, so they never reach telemetry. Empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> ItemCollectionMetrics { get; }
}
