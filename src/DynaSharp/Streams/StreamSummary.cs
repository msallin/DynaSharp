namespace DynaSharp.Streams;

/// <summary>
/// The summary of a stream of a table, as listed by <c>ListStreams</c>.
/// </summary>
/// <remarks>
/// A table has at most one enabled stream at a time. Switching streams off and on again creates a new stream with a new ARN; the
/// old one stays readable for 24 hours.
/// </remarks>
public sealed record StreamSummary
{
    /// <summary>Gets the ARN of the stream, which identifies it in all other operations.</summary>
    public required string StreamArn { get; init; }

    /// <summary>
    /// Gets the name of the table the stream belongs to. DynamoDB sends it with every stream: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    /// Gets the label of the stream: a timestamp of its creation, which is also the last part of the ARN. DynamoDB sends it with every stream:
    /// <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? StreamLabel { get; init; }
}
