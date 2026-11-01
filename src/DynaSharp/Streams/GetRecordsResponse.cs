namespace DynaSharp.Streams;

/// <summary>
/// The result of <see cref="GetRecordsRequest"/>.
/// </summary>
/// <remarks>
/// An empty <see cref="Records"/> list with a <see cref="NextShardIterator"/> is normal: the shard has no new records yet, or
/// DynamoDB read a part of the shard without finding any. Keep reading with the new iterator, and wait a moment between empty
/// responses. Only a <see langword="null"/> <see cref="NextShardIterator"/> ends a shard.
/// </remarks>
public sealed record GetRecordsResponse
{
    /// <summary>Gets the records in the order DynamoDB wrote them. Empty when there are none yet: that is not an error and not the end of the shard.</summary>
    public IReadOnlyList<ChangeRecord> Records { get; init; } = [];

    /// <summary>
    /// Gets the iterator to read the next records with. <see langword="null"/> when the shard is closed and all its records have
    /// been returned: the reader is done with this shard and continues with its child shards (see <c>DescribeStream</c> with a
    /// <see cref="ShardFilter"/>). While the shard is open it is never <see langword="null"/>, even when <see cref="Records"/> is
    /// empty. Valid for 15 minutes; treat it like a credential.
    /// </summary>
    public string? NextShardIterator { get; init; }
}
