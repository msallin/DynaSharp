namespace DynaSharp.Streams;

/// <summary>
/// Reads records from a shard (DynamoDB Streams <c>GetRecords</c>).
/// </summary>
/// <remarks>
/// A shard allows five calls per second, and a call returns at most 1 MB of data. Read in a loop: pass
/// <see cref="GetRecordsResponse.NextShardIterator"/> as the next <see cref="ShardIterator"/>, until it is <see langword="null"/>.
/// </remarks>
public sealed record GetRecordsRequest
{
    /// <summary>Gets the iterator from <c>GetShardIterator</c> or from the previous <c>GetRecords</c>. It is valid for 15 minutes.</summary>
    public required string ShardIterator { get; init; }

    /// <summary>
    /// Gets the maximum number of records to return, 1 to 1,000. <see langword="null"/> sends nothing, and DynamoDB returns up to
    /// 1,000 records or 1 MB.
    /// </summary>
    public int? Limit { get; init; }
}
