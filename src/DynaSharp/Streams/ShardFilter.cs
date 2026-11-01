namespace DynaSharp.Streams;

/// <summary>
/// Restricts the shards that <c>DescribeStream</c> lists.
/// </summary>
/// <remarks>
/// With <see cref="ShardFilterType.ChildShards"/> the call returns only the children of one shard, which is how a reader that
/// finished a shard finds the shards to continue with without listing the whole stream again.
/// </remarks>
public sealed record ShardFilter
{
    /// <summary>Gets the kind of filter.</summary>
    public required ShardFilterType Type { get; init; }

    /// <summary>Gets the identifier of the shard whose children are listed.</summary>
    public required string ShardId { get; init; }
}
