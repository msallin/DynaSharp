namespace DynaSharp.Streams;

/// <summary>
/// Reads every shard of a stream in one process, for <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>.
/// </summary>
/// <remarks>
/// The position is one of two: <see cref="ShardIteratorType.TrimHorizon"/> for everything still kept, or <see cref="ShardIteratorType.Latest"/> for what
/// is written from now on. It applies to the shards open at the start; a shard that starts later is read from its first sequence number. There is no position to resume
/// at: to resume, read the shard of a record with <see cref="DynamoDbStreamsClient.ReadShardAsync"/> and <see cref="ShardIteratorType.AfterSequenceNumber"/>.
/// </remarks>
public sealed record ReadStreamRequest
{
    /// <summary>Gets the ARN of the stream, <see cref="Requests.TableDescription.LatestStreamArn"/> of its table.</summary>
    public required string StreamArn { get; init; }

    /// <summary>
    /// Gets where reading starts in the shards that are open at the start: <see cref="ShardIteratorType.TrimHorizon"/> or <see cref="ShardIteratorType.Latest"/>.
    /// </summary>
    /// <remarks>
    /// With <see cref="ShardIteratorType.TrimHorizon"/> the shards that are closed at the start are read too, from their oldest record, each before its children.
    /// With <see cref="ShardIteratorType.Latest"/> they have nothing to read from now on, so only their open descendants are, from the start of the reading on, and
    /// a child listed after the start, of a split in progress, from its first record.
    /// </remarks>
    public required ShardIteratorType ShardIteratorType { get; init; }

    /// <summary>
    /// Gets how long the reader of a shard waits after a page without records before it asks again, on <see cref="DynamoDbClientOptions.TimeProvider"/>, as
    /// <see cref="ReadShardRequest.EmptyPageDelay"/> does. The default is one second.
    /// </summary>
    /// <remarks>
    /// The same wait, but at least a second, comes between two looks for the children of a closed shard while the stream is still enabled and DynamoDB lists none
    /// yet. Zero asks a shard again at once. The delay must stay below the 15 minutes an iterator lives.
    /// </remarks>
    public TimeSpan EmptyPageDelay { get; init; } = TimeSpan.FromSeconds(1);
}
