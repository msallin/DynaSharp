using System.Diagnostics.CodeAnalysis;

namespace DynaSharp;

/// <summary>What a put, an update or a delete returns.</summary>
/// <typeparam name="T">The type of the item.</typeparam>
public readonly struct WriteResult<T>
{
    internal WriteResult(T? item, bool hasItem, ConsumedCapacity? consumedCapacity, ItemCollectionMetrics? itemCollectionMetrics, long? version)
    {
        Item = item;
        HasItem = hasItem;
        ConsumedCapacity = consumedCapacity;
        ItemCollectionMetrics = itemCollectionMetrics;
        Version = version;
    }

    /// <summary>
    /// Gets the item that <c>ReturnOld</c> or <c>ReturnNew</c> asked for, or the default value of <typeparamref name="T"/> if there is none: nothing
    /// was asked for, or <c>ReturnOld</c> found no item under the key.
    /// </summary>
    public T? Item { get; }

    /// <summary>Gets a value indicating whether the response holds an item. It is what to test, because an entity may be a struct.</summary>
    [MemberNotNullWhen(true, nameof(Item))]
    public bool HasItem { get; }

    /// <summary>Gets the capacity the write consumed, if <c>ReturnCapacity</c> asked for it.</summary>
    public ConsumedCapacity? ConsumedCapacity { get; }

    /// <summary>
    /// Gets the size estimate of the item collection, if <c>ReturnMetrics</c> asked for it. DynamoDB sends it for a table with a local secondary
    /// index, and it contains the partition key value, so it never reaches telemetry.
    /// </summary>
    public ItemCollectionMetrics? ItemCollectionMetrics { get; }

    /// <summary>
    /// Gets the version the write stored for a <see cref="Mapping.VersionAttribute"/> entity, which is what the next write expects: a put stores the version of its
    /// entity plus one, a put with <c>.IgnoreVersion()</c> the version of its entity, and an update the one of <c>.IfVersion(n)</c> plus one. <see langword="null"/> if it stored
    /// none: a delete, an update with <c>.IgnoreVersion()</c>, or a type without a version. The entity of a put stays as it was.
    /// </summary>
    public long? Version { get; }

    /// <summary>
    /// Gets the version the write stored, as <see cref="Version"/> does, for code that knows it stored one: a put or an update with <c>.IfVersion(n)</c> of a <see cref="Mapping.VersionAttribute"/>
    /// entity always does, so <c>var next = (await orders.Put(order).SendAsync(ct)).StoredVersion;</c> needs no <c>!.Value</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The write stored no version: a delete, an update with <c>.IgnoreVersion()</c>, or a type without a version. The message says which kinds of write store none.</exception>
    public long StoredVersion => Version ?? throw new InvalidOperationException(
        "The write stored no version. A put of a [Version] entity and an update with IfVersion(n) store one; a delete, an update with IgnoreVersion() and a write of a type without [Version] do not, and Version is null for them.");
}
