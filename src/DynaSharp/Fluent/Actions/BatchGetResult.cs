using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>What a batch get returns: the items of its table in <see cref="Items"/>, and the item of each get with a slot through <see cref="Read{TOther}(BatchSlot{TOther})"/>.</summary>
/// <typeparam name="T">The type of the items.</typeparam>
public readonly struct BatchGetResult<T>
{
    private readonly BatchSlots? _slots;

    internal BatchGetResult(IReadOnlyList<T> items, int requests, IReadOnlyList<ConsumedCapacity>? consumedCapacity, BatchSlots? slots = null)
    {
        _slots = slots;
        Items = items;
        Requests = requests;
        ConsumedCapacity = consumedCapacity ?? [];
    }

    /// <summary>Gets the items of the keys of the table of the command, in no particular order: not the order of the keys, and it differs between two runs. A key without an item is simply absent, so compare <c>Count</c> with the number of keys to notice one.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets how many requests DynamoDB saw, each with a span of its own: one per 100 keys, and one more per round of unprocessed keys. Zero for a batch without keys.</summary>
    public int Requests { get; }

    /// <summary>Gets the capacity the batch consumed, if <c>ReturnCapacity</c> asked for it: one entry per request, so the capacity of the batch is their sum. Empty otherwise.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; }

    /// <summary>Reads the item of a get that has a slot, which can be of another table and type than the command.</summary>
    /// <typeparam name="TOther">How the item is read, which the slot knows.</typeparam>
    /// <param name="slot">The slot of the get.</param>
    /// <returns>The item, or <see cref="GetResult{T}.Found"/> <see langword="false"/> if there is none under the key. <see cref="GetResult{T}.ConsumedCapacity"/> stays empty: the capacity is on this result.</returns>
    /// <exception cref="ArgumentException">The slot is a default value, or it comes from another batch with more slots.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="TOther"/>.</exception>
    public GetResult<TOther> Read<TOther>(BatchSlot<TOther> slot)
        where TOther : IDynamoDbDocument<TOther> => BatchSlots.Read(_slots, slot);
}
