namespace DynaSharp.Streams;

/// <summary>
/// Gets an iterator that marks a position in a shard, to read records from (DynamoDB Streams <c>GetShardIterator</c>).
/// </summary>
/// <remarks>
/// The iterator is valid for 15 minutes. After that <c>GetRecords</c> fails with <see cref="ExpiredIteratorException"/> and a new
/// iterator is needed: take one with <see cref="ShardIteratorType.AfterSequenceNumber"/> and the sequence number of the last record
/// processed. Requesting a position older than the 24 hours of retention fails with <see cref="TrimmedDataAccessException"/>.
/// </remarks>
public sealed record GetShardIteratorRequest
{
    /// <summary>Gets the ARN of the stream.</summary>
    public required string StreamArn { get; init; }

    /// <summary>Gets the identifier of the shard.</summary>
    public required string ShardId { get; init; }

    /// <summary>Gets where in the shard reading starts.</summary>
    public required ShardIteratorType ShardIteratorType { get; init; }

    /// <summary>
    /// Gets the sequence number to start at or after. Required for <see cref="Streams.ShardIteratorType.AtSequenceNumber"/> and
    /// <see cref="Streams.ShardIteratorType.AfterSequenceNumber"/>, and not allowed for the other types.
    /// </summary>
    public string? SequenceNumber { get; init; }
}
