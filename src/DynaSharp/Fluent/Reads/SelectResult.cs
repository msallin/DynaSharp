namespace DynaSharp;

/// <summary>
/// What a read of one attribute gives when there may be no item: <c>GetItem</c> without one, or a query in which nothing matches. It has the shape of <see cref="GetResult{T}"/> with one difference: there,
/// <see cref="GetResult{T}.Found"/> says that <see cref="GetResult{T}.Item"/> is not null, which an attribute cannot promise, because a nullable one can be missing from an item that exists.
/// </summary>
/// <remarks>
/// <see cref="Found"/> is about the item and <see cref="Value"/> about the attribute, so a nullable attribute tells all three apart: no item, an item without the attribute, and a value. A handle that cannot
/// be null has only two of them, because an item without the attribute throws <see cref="DynamoDbMappingException"/>, unless it has an answer for that: a set is empty, and a version with
/// <see cref="Mapping.VersionMissing.Allow"/> is 0. That answer is about an item that exists. No item is <see cref="Found"/> false, whatever the handle, and <see cref="Value"/> throws.
/// </remarks>
/// <typeparam name="TValue">The type of the attribute, nullable for a handle of a nullable property.</typeparam>
public readonly struct SelectResult<TValue>
{
    internal SelectResult(TValue value, bool found, ConsumedCapacity? consumedCapacity)
    {
        Value = value;
        Found = found;
        ConsumedCapacity = consumedCapacity;
    }

    /// <summary>
    /// Gets the value of the attribute. A read that found no item has none, which is not the same as an item without the attribute, so look at <see cref="Found"/> first: <c>result.Found ? result.Value : fallback</c>,
    /// which <see cref="GetValueOrDefault(TValue)"/> says in one call. <c>DYNA227</c> reports a read of this in a method that never looks at <see cref="Found"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no item, so there is no value. A default would read like one, so test <see cref="Found"/> first.</exception>
    public TValue Value => Found ? field : throw new InvalidOperationException("The read found no item, so it has no value. Look at Found first.");

    /// <summary>Gets a value indicating whether an item exists. It says nothing about the attribute, which can be missing and null for a nullable handle.</summary>
    public bool Found { get; }

    /// <summary>Gets the capacity the read consumed, if <c>ReturnCapacity</c> asked for it. For a query it is that of the last page: the pages before it reach the metric and no other place.</summary>
    public ConsumedCapacity? ConsumedCapacity { get; }

    /// <summary>
    /// Gets the value of the attribute, or the default value of <typeparamref name="TValue"/> when the read found no item, as <see cref="Nullable{T}.GetValueOrDefault()"/> does: <c>0</c> for an <c>int</c>,
    /// <see langword="null"/> for a <c>string?</c>. It does not throw, so it is for code that treats no item as that value. A nullable handle gives <see langword="null"/> also for an item without the
    /// attribute, so there it cannot tell the two apart; <see cref="Found"/> can.
    /// </summary>
    /// <returns>The value, or the default value of the type.</returns>
    public TValue? GetValueOrDefault() => Found ? Value : default;

    /// <summary>Gets the value of the attribute, or <paramref name="defaultValue"/> when the read found no item: <c>(await totals).GetValueOrDefault(-1m)</c>. An item whose attribute is null gives that null, not the fallback.</summary>
    /// <param name="defaultValue">What no item gives.</param>
    /// <returns>The value, or <paramref name="defaultValue"/>.</returns>
    public TValue GetValueOrDefault(TValue defaultValue) => Found ? Value : defaultValue;
}
