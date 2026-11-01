namespace DynaSharp.Streams;

/// <summary>
/// The result of <see cref="GetShardIteratorRequest"/>.
/// </summary>
public sealed record GetShardIteratorResponse
{
    /// <summary>
    /// Gets the iterator, an opaque string of up to 2,048 characters that is valid for 15 minutes. Treat it like a credential: it
    /// grants read access to the shard, and this library never logs it or puts it into telemetry. <see langword="null"/> if the response has none, which
    /// the service model allows: there is nothing to read from the shard then.
    /// </summary>
    public string? ShardIterator { get; init; }
}
