namespace DynaSharp.Streams;

/// <summary>
/// One page of the result of <see cref="ListStreamsRequest"/>.
/// </summary>
public sealed record ListStreamsResponse
{
    /// <summary>Gets the streams of this page. Empty when there are none: that is not an error.</summary>
    public IReadOnlyList<StreamSummary> Streams { get; init; } = [];

    /// <summary>
    /// Gets the ARN of the last stream evaluated, to pass as <see cref="ListStreamsRequest.ExclusiveStartStreamArn"/> to read the
    /// next page. <see langword="null"/> on the last page, never an empty string. A page can have fewer streams than the limit and
    /// still have a successor; only <see langword="null"/> means the end.
    /// </summary>
    public string? LastEvaluatedStreamArn { get; init; }
}
