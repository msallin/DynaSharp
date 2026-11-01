namespace DynaSharp.Streams;

/// <summary>
/// The result of <see cref="DescribeStreamRequest"/>.
/// </summary>
public sealed record DescribeStreamResponse
{
    /// <summary>
    /// Gets the description of the stream with one page of its shards. DynamoDB sends one with every response: <see langword="null"/> only if the response omits it.
    /// </summary>
    public StreamDescription? StreamDescription { get; init; }
}
