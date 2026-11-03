using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Fluent;

/// <summary>What the requests of a batch add up to. The lists exist only if DynamoDB reported something, which it does only when asked to.</summary>
internal struct BatchTotals
{
    /// <summary>Gets the number of requests that were answered.</summary>
    public int Requests { get; private set; }

    /// <summary>Gets the number of actions DynamoDB took.</summary>
    public int Processed { get; private set; }

    /// <summary>Gets the capacity of all requests, an entry per table and request.</summary>
    public List<ConsumedCapacity>? ConsumedCapacity { get; private set; }

    /// <summary>Gets the metrics of all requests by table.</summary>
    public Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>? ItemCollectionMetrics { get; private set; }

    /// <summary>Adds a response.</summary>
    /// <param name="response">The response.</param>
    /// <param name="sent">How many actions the request held.</param>
    public void Add(BatchResponse response, int sent)
    {
        Requests++;
        Processed += sent - response.Unprocessed;
        if (response.ConsumedCapacity.Count > 0)
        {
            ConsumedCapacity ??= [];
            ConsumedCapacity.AddRange(response.ConsumedCapacity);
        }

        if (response.ItemCollectionMetrics is { } metrics)
        {
            Merge(metrics);
        }
    }

    /// <summary>Counts a request whose response could not be read: it was answered, but none of its actions counts as taken.</summary>
    public void AddUnread() => Requests++;

    [SuppressMessage("Style", "IDE0028:Simplify collection initialization", Justification = "The compiler in Stryker cannot parse collection expression arguments.")]
    private void Merge(IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> metrics)
    {
        ItemCollectionMetrics ??= new(StringComparer.Ordinal);
        foreach (var (table, entries) in metrics)
        {
            ItemCollectionMetrics[table] = ItemCollectionMetrics.TryGetValue(table, out var earlier) ? [.. earlier, .. entries] : entries;
        }
    }
}
