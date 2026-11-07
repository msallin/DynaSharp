namespace DynaSharp;

/// <summary>
/// One page of a <c>Query</c> or <c>Scan</c>: what one response of DynamoDB holds. A page ends at 1 MB of data read, or at the limit, whichever
/// comes first. A page of a fan-out is the exception: it is a stretch of the merge, of the size that was asked for, made of as many responses as that took.
/// </summary>
/// <remarks>
/// A page can be empty and still have a next one, because a filter runs after the read: <see cref="HasMore"/> is what tells the end, never the number of
/// items.
/// </remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public readonly struct Page<T>
{
    internal Page(IReadOnlyList<T> items, int count, int scannedCount, ConsumedCapacity? consumedCapacity, ContinuationToken next)
    {
        Items = items;
        Count = count;
        ScannedCount = scannedCount;
        ConsumedCapacity = consumedCapacity;
        Next = next;
    }

    /// <summary>Gets the items of the page, in the order of the sort key for a query and in no useful order for a scan. It is empty after <c>CountAsync</c>.</summary>
    /// <remarks>
    /// <b>Items of other entities.</b> A typed read holds only its own entity. The items of other entities under the same partition key are read, count against the limit and are in
    /// <see cref="ScannedCount"/>, so a page can hold fewer items than its limit. <see cref="QueryCommand{T}"/> says how a condition on the sort key reads the entity alone.
    /// </remarks>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets the number of items after the filter.</summary>
    public int Count { get; }

    /// <summary>
    /// Gets the number of items read before the filter, which is what capacity is charged for. A typed read of an entity with a discriminator filters on
    /// it, so items of other types of the table are in this number and not in <see cref="Count"/>.
    /// </summary>
    public int ScannedCount { get; }

    /// <summary>Gets the capacity this page consumed, if <c>ReturnCapacity</c> asked for it. For a page of a fan-out it is the sum of all its calls.</summary>
    public ConsumedCapacity? ConsumedCapacity { get; }

    /// <summary>Gets a value indicating whether DynamoDB has more to read. The next page can turn out empty: DynamoDB does not look ahead.</summary>
    public bool HasMore => Next.HasValue;

    /// <summary>Gets where the next page starts, or the default value at the end of the read. Pass it to <c>PageAsync</c>, or hand <c>Next.ToString()</c> to a client.</summary>
    public ContinuationToken Next { get; }
}
