namespace DynaSharp.Streams;

/// <summary>
/// The kind of filter in a <see cref="ShardFilter"/>.
/// </summary>
public enum ShardFilterType
{
    /// <summary>A filter this library does not know. Never valid in a request.</summary>
    Unknown = 0,

    /// <summary>Only the shards that are children of the given shard, DynamoDB name <c>CHILD_SHARDS</c>.</summary>
    ChildShards,
}
