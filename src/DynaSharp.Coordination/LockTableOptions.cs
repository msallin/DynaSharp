namespace DynaSharp.Coordination;

/// <summary>
/// What a <see cref="LockTable"/> adds to the items of its locks: a label that tells operators which process holds a lock, and a time to live for the
/// items of locks that are no longer used. Both are off by default, and neither is ever part of a condition, so processes with different options contend
/// for the same locks as equals.
/// </summary>
public sealed class LockTableOptions
{
    /// <summary>The longest <see cref="HolderLabel"/>, in characters.</summary>
    public const int MaxHolderLabelLength = 256;

    /// <summary>
    /// Gets a label of this process, such as the name of its pod or host, which every lease writes into the item of its lock as the attribute <c>holder</c>, for operators who ask who
    /// holds it. <see langword="null"/>, the default, writes none.
    /// </summary>
    /// <remarks>
    /// The logs of the takeover and of the terms of a leader name it too. A release removes it, and an acquire without a label removes the label of the holder before. The label is shown to operators, in the table and in the logs, but never in a metric or on a span. So it must not carry data: no user id, no secret,
    /// nothing that is not a name of the process. From 1 to <see cref="MaxHolderLabelLength"/> characters.
    /// </remarks>
    public string? HolderLabel { get; init; }

    /// <summary>
    /// Gets the name of the time to live attribute of the table, so that DynamoDB deletes the item of a lock that nobody has used for <see cref="TimeToLiveRetention"/>.
    /// <see langword="null"/>, the default, lets lock items live forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is written.</b> Every write of a lock (acquire, renewal, release) sets that attribute to the wall clock of the process plus <see cref="TimeToLiveRetention"/>, in seconds since
    /// the epoch. This is for a table with a lock per entity, thousands of items that would otherwise stay forever. Time to live must be turned on for the table, on this attribute; this option only writes it. A lock whose item was deleted starts its fencing token
    /// at 1 again, so a system that keeps the highest token it has seen refuses its holders until the count passes what it saw: fencing across that gap is
    /// not guaranteed. Keep locks whose tokens a system outside DynamoDB checks without it.
    /// </para>
    /// <para>
    /// A lease is renewed at least every 8 hours, and the retention is at least a day, so the item of a held lock is never due, as long as the clock of the
    /// holder is not off by more than the retention less the lease. The attribute must not be one of the lock or one of the keys of the table.
    /// </para>
    /// </remarks>
    public string? TimeToLiveAttribute { get; init; }

    /// <summary>
    /// Gets how long after its last write a lock item is due for deletion, with <see cref="TimeToLiveAttribute"/>: from 1 day, 30 days by default. DynamoDB
    /// deletes an item some time after it is due, often within days.
    /// </summary>
    public TimeSpan TimeToLiveRetention { get; init; } = TimeSpan.FromDays(30);
}
