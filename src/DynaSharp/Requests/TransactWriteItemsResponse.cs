using System.Collections.Frozen;

namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="TransactWriteItemsRequest"/>. DynamoDB confirms a transaction without further data; the members are
/// the optional metrics.
/// </summary>
public sealed record TransactWriteItemsResponse
{
    /// <summary>
    /// Gets the write capacity consumed, one entry per table, when the request asked for it. Empty otherwise; never
    /// <see langword="null"/>. A repeated request that DynamoDB recognizes by its <see cref="TransactWriteItemsRequest.ClientRequestToken"/>
    /// reports the read capacity of looking up the first outcome instead.
    /// </summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; init; } = [];

    /// <summary>
    /// Gets the item collections that the transaction modified, by table, when the request asked for it and the tables have local
    /// secondary indexes. Empty otherwise; never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> ItemCollectionMetrics { get; init; } =
        FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;
}
