using System.Diagnostics.CodeAnalysis;

namespace DynaSharp;

/// <summary>What a get returns.</summary>
/// <typeparam name="T">The type of the item.</typeparam>
public readonly struct GetResult<T>
{
    internal GetResult(T? item, bool found, ConsumedCapacity? consumedCapacity)
    {
        Item = item;
        Found = found;
        ConsumedCapacity = consumedCapacity;
    }

    /// <summary>Gets the item, or the default value of <typeparamref name="T"/> if there is none.</summary>
    public T? Item { get; }

    /// <summary>Gets a value indicating whether an item exists under the key. It is what to test, because an entity may be a struct.</summary>
    [MemberNotNullWhen(true, nameof(Item))]
    public bool Found { get; }

    /// <summary>Gets the capacity the read consumed, if <c>ReturnCapacity</c> asked for it.</summary>
    public ConsumedCapacity? ConsumedCapacity { get; }
}
