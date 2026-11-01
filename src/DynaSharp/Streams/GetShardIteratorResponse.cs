namespace DynaSharp.Streams;

/// <summary>
/// The result of <see cref="GetShardIteratorRequest"/>.
/// </summary>
public sealed record GetShardIteratorResponse
{
    /// <summary>
    /// Gets the iterator, an opaque string of up to 2,048 characters that is valid for 15 minutes. Treat it like a credential: it
    /// grants read access to the shard, and this library never logs it or puts it into telemetry.
    /// </summary>
    public required string ShardIterator { get; init; }
}
