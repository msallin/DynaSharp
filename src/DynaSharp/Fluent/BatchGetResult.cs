namespace DynaSharp;

/// <summary>What a batch get returns.</summary>
/// <typeparam name="T">The type of the items.</typeparam>
public readonly struct BatchGetResult<T>
{
    internal BatchGetResult(IReadOnlyList<T> items, int requests, IReadOnlyList<ConsumedCapacity>? consumedCapacity)
    {
        Items = items;
        Requests = requests;
        ConsumedCapacity = consumedCapacity ?? [];
    }

    /// <summary>Gets the items, in no particular order: not the order of the keys, and it differs between two runs. A key without an item is simply absent, so compare <c>Count</c> with the number of keys to notice one.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets how many requests DynamoDB saw, each with a span of its own: one per 100 keys, and one more per round of unprocessed keys. Zero for a batch without keys.</summary>
    public int Requests { get; }

    /// <summary>Gets the capacity the batch consumed, if <c>ReturnCapacity</c> asked for it: one entry per request, so the capacity of the batch is their sum. Empty otherwise.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; }
}
