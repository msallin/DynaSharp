using System.Collections.Frozen;

namespace DynaSharp;

/// <summary>What a transaction of writes returns. Getting this far means that all its actions were applied, as one.</summary>
public readonly struct TransactWriteResult
{
    internal TransactWriteResult(IReadOnlyList<ConsumedCapacity> consumedCapacity, IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>>? itemCollectionMetrics)
    {
        ConsumedCapacity = consumedCapacity;
        ItemCollectionMetrics = itemCollectionMetrics ?? FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;
    }

    /// <summary>
    /// Gets the capacity the transaction consumed, if <c>ReturnCapacity</c> asked for it: an entry per table. Empty otherwise. A write in a transaction
    /// costs twice a standard write, for the prepare and the commit.
    /// </summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; }

    /// <summary>
    /// Gets the size estimates of the item collections by table, if <c>ReturnMetrics</c> asked for them. DynamoDB sends them for tables with a local
    /// secondary index, and they contain the partition key value, so they never reach telemetry. Empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> ItemCollectionMetrics { get; }
}
