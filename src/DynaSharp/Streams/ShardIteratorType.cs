namespace DynaSharp.Streams;

/// <summary>
/// Where in a shard reading starts.
/// </summary>
public enum ShardIteratorType
{
    /// <summary>A position this library does not know. Never valid in a request.</summary>
    Unknown = 0,

    /// <summary>The oldest record that is still in the shard, DynamoDB name <c>TRIM_HORIZON</c>. Records are kept for 24 hours.</summary>
    TrimHorizon,

    /// <summary>Just after the newest record, DynamoDB name <c>LATEST</c>: only records written after the iterator was created are read.</summary>
    Latest,

    /// <summary>The record with the given sequence number, DynamoDB name <c>AT_SEQUENCE_NUMBER</c>. Needs <see cref="GetShardIteratorRequest.SequenceNumber"/>.</summary>
    AtSequenceNumber,

    /// <summary>The record after the one with the given sequence number, DynamoDB name <c>AFTER_SEQUENCE_NUMBER</c>. Needs <see cref="GetShardIteratorRequest.SequenceNumber"/>; the way to resume after the last processed record.</summary>
    AfterSequenceNumber,
}
