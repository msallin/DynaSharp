namespace DynaSharp.Streams;

/// <summary>
/// Reads the settings of a stream and its shards (DynamoDB Streams <c>DescribeStream</c>).
/// </summary>
/// <remarks>
/// The shards come in pages of at most 100. Call again with <see cref="StreamDescription.LastEvaluatedShardId"/> as
/// <see cref="ExclusiveStartShardId"/> until it is <see langword="null"/>. A stream has a strict limit of 10 calls per second.
/// </remarks>
public sealed record DescribeStreamRequest
{
    /// <summary>Gets the ARN of the stream.</summary>
    public required string StreamArn { get; init; }

    /// <summary>
    /// Gets the maximum number of shards to return, at least 1; DynamoDB returns at most 100. <see langword="null"/> sends nothing.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets the identifier of the last shard of the previous page: <see cref="StreamDescription.LastEvaluatedShardId"/> of the
    /// previous response, unchanged.
    /// </summary>
    public string? ExclusiveStartShardId { get; init; }

    /// <summary>Gets a filter on the shards, or <see langword="null"/> to list them all.</summary>
    public ShardFilter? ShardFilter { get; init; }
}
