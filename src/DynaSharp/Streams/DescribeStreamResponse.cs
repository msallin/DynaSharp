namespace DynaSharp.Streams;

/// <summary>
/// The result of <see cref="DescribeStreamRequest"/>.
/// </summary>
public sealed record DescribeStreamResponse
{
    /// <summary>Gets the description of the stream with one page of its shards.</summary>
    public required StreamDescription StreamDescription { get; init; }
}
