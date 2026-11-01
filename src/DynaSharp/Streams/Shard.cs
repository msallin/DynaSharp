namespace DynaSharp.Streams;

/// <summary>
/// A shard of a stream: a sequence of records that is read in order.
/// </summary>
/// <remarks>
/// <para>
/// Shards form a tree. When a shard fills up or DynamoDB repartitions the table, it is closed and its children take over; the
/// children name it in <see cref="ParentShardId"/>. To read all changes of an item in order, finish the parent before its
/// children. A shard is open while <see cref="SequenceNumberRange.EndingSequenceNumber"/> is <see langword="null"/>.
/// </para>
/// <para>
/// DynamoDB removes a shard 24 hours after it was closed. The list of shards can change between two calls of <c>DescribeStream</c>.
/// </para>
/// </remarks>
public sealed record Shard
{
    /// <summary>Gets the identifier of the shard.</summary>
    public required string ShardId { get; init; }

    /// <summary>Gets the first and last sequence number of the shard, or <see langword="null"/> if DynamoDB did not report them.</summary>
    public SequenceNumberRange? SequenceNumberRange { get; init; }

    /// <summary>
    /// Gets the identifier of the parent shard, or <see langword="null"/> for a shard without one. The parent may no longer be
    /// listed once its records have expired.
    /// </summary>
    public string? ParentShardId { get; init; }
}
