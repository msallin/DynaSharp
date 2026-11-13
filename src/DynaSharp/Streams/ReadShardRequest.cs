namespace DynaSharp.Streams;

/// <summary>
/// Reads one shard of a stream from a position to its end, for <see cref="DynamoDbStreamsClient.ReadShardAsync"/>.
/// </summary>
/// <remarks>
/// The position is the one of <see cref="GetShardIteratorRequest"/>: <see cref="ShardIteratorType.TrimHorizon"/> for everything still kept,
/// <see cref="ShardIteratorType.Latest"/> for what is written from now on, and <see cref="ShardIteratorType.AfterSequenceNumber"/> with the sequence
/// number of the last record a reader handled, to resume where it left off.
/// </remarks>
public sealed record ReadShardRequest
{
    /// <summary>Gets the ARN of the stream.</summary>
    public required string StreamArn { get; init; }

    /// <summary>Gets the identifier of the shard, from <see cref="DynamoDbStreamsClient.DescribeStreamAsync"/>.</summary>
    public required string ShardId { get; init; }

    /// <summary>Gets where in the shard reading starts.</summary>
    public required ShardIteratorType ShardIteratorType { get; init; }

    /// <summary>
    /// Gets the sequence number to start at or after. Required, and not empty, for <see cref="Streams.ShardIteratorType.AtSequenceNumber"/> and
    /// <see cref="Streams.ShardIteratorType.AfterSequenceNumber"/>, and not allowed for the other types.
    /// </summary>
    public string? SequenceNumber { get; init; }

    /// <summary>
    /// Gets the maximum number of records of one <c>GetRecords</c> call, 1 to 1,000. <see langword="null"/> sends nothing, and DynamoDB returns up
    /// to 1,000 records or 1 MB a call.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets how long the reader waits after a page without records before it asks again, on <see cref="DynamoDbClientOptions.TimeProvider"/>.
    /// The default is one second: a shard allows five <c>GetRecords</c> calls per second, shared by all its readers, and an open shard
    /// answers an empty page as long as nothing changes.
    /// </summary>
    /// <remarks>
    /// Zero asks again at once. The delay must stay below the 15 minutes an iterator lives, or every wait would end with an expired iterator.
    /// </remarks>
    public TimeSpan EmptyPageDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets what is called with the id of the shard when the reading may have skipped records, or <see langword="null"/> for nothing: a shard read from
    /// <see cref="Streams.ShardIteratorType.Latest"/> whose iterator expired before it returned a record is read from <see cref="Streams.ShardIteratorType.Latest"/> again,
    /// and what was written while the iterator was expired is not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resync on it.</b> DynamoDB Streams has no position by time, so nothing reads those records afterwards: reload what the reader keeps from the table, or read from
    /// <see cref="Streams.ShardIteratorType.TrimHorizon"/> where repeating what is kept is cheaper. An iterator expires after 15 minutes without a call, when the caller takes that
    /// long between two records, or when a <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> outage lasts that long. No other position skips: after a record the reading goes on
    /// after it, and a position DynamoDB trimmed throws <see cref="TrimmedDataAccessException"/>.
    /// </para>
    /// <para>
    /// It is called on the task of the reader, before the new iterator is taken, so keep it short, such as setting a flag or completing a <see cref="TaskCompletionSource"/>;
    /// an exception from it ends the reading with that exception. The client also counts it in <c>dynasharp.client.stream.gaps</c> and logs a warning (event 15), with or without it.
    /// </para>
    /// </remarks>
    public Action<string>? OnGap { get; init; }
}
