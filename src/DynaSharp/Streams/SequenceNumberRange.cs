namespace DynaSharp.Streams;

/// <summary>
/// The sequence numbers of the first and last record of a shard.
/// </summary>
/// <remarks>
/// A sequence number is an opaque string that grows within a shard; compare sequence numbers of different shards never.
/// </remarks>
public sealed record SequenceNumberRange
{
    /// <summary>Gets the sequence number of the first record the shard ever had, which may already be trimmed.</summary>
    public string? StartingSequenceNumber { get; init; }

    /// <summary>
    /// Gets the sequence number of the last record of the shard. <see langword="null"/> while the shard is open, that is while
    /// DynamoDB can still write records to it; set once the shard is closed.
    /// </summary>
    public string? EndingSequenceNumber { get; init; }
}
