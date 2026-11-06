using System.Diagnostics.CodeAnalysis;

namespace DynaSharp;

/// <summary>What a put, an update or a delete returns.</summary>
/// <typeparam name="T">The type of the item.</typeparam>
public readonly struct WriteResult<T>
{
    internal WriteResult(T? item, bool hasItem, ConsumedCapacity? consumedCapacity, ItemCollectionMetrics? itemCollectionMetrics, long? version, bool conditionMet)
    {
        ConditionFailed = !conditionMet;
        Item = item;
        HasItem = hasItem;
        ConsumedCapacity = consumedCapacity;
        ItemCollectionMetrics = itemCollectionMetrics;
        Version = version;
    }

    /// <summary>
    /// Gets a value indicating whether the condition of the write held, so that the write took effect. It is <see langword="true"/> on every result of <c>SendAsync</c>, which throws
    /// <see cref="ConditionalCheckFailedException"/> instead, and on a write without a condition. <see langword="false"/> comes only from <c>TrySendAsync</c>, for a condition that did not hold:
    /// nothing was written, <see cref="Item"/> is the item the condition saw if <c>ReturnOldOnFailure()</c> asked for it, and <see cref="Version"/>, <see cref="ConsumedCapacity"/> and
    /// <see cref="ItemCollectionMetrics"/> are <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// After a response that was lost, the client sends the write again, and a conditional write that took effect the first time then fails against itself. On the wire that is the
    /// same as a write that an earlier delivery of the same change already made, so <see langword="false"/> does not say which: compare what <see cref="Item"/> holds with what the
    /// write sent. Equal means that the change is stored, by this call or an earlier one.
    /// </remarks>
    public bool ConditionMet => !ConditionFailed;

    /// <summary>
    /// Gets the item that <c>ReturnOld</c> or <c>ReturnNew</c> asked for, or the default value of <typeparamref name="T"/> if there is none: nothing
    /// was asked for, or <c>ReturnOld</c> found no item under the key. When <see cref="ConditionMet"/> is <see langword="false"/>, it is the item the condition saw, if
    /// <c>ReturnOldOnFailure()</c> asked for it and an item had the key; <c>ReturnOld</c> and <c>ReturnNew</c> give nothing then.
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
    /// none: a delete, an update with <c>.IgnoreVersion()</c>, a type without a version, or a write of <c>TrySendAsync</c> whose condition did not hold, which wrote nothing.
    /// The entity of a put stays as it was.
    /// </summary>
    /// <remarks>
    /// Whether a write stores a version depends on the chain and, for <c>TrySendAsync</c>, on the answer, so the type cannot say it, and the value is the one place that does.
    /// Code that knows its write stored one, a put of a <see cref="Mapping.VersionAttribute"/> entity or an update with <c>.IfVersion(n)</c> sent with <c>SendAsync</c>, takes
    /// <c>result.Version!.Value</c>, and code that does not asks with <c>result.Version is { } next</c>.
    /// </remarks>
    public long? Version { get; }

    // Kept the other way round, so that the default value of the struct reads as a write whose condition held, as every result of SendAsync does.
    private bool ConditionFailed { get; }
}
