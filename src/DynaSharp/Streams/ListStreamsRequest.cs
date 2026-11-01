namespace DynaSharp.Streams;

/// <summary>
/// Lists the streams of the account in the region, or of one table (DynamoDB Streams <c>ListStreams</c>).
/// </summary>
public sealed record ListStreamsRequest
{
    /// <summary>Gets the table whose streams to list. <see langword="null"/> lists the streams of all tables.</summary>
    public string? TableName { get; init; }

    /// <summary>
    /// Gets the maximum number of streams to return, at least 1; DynamoDB returns at most 100 whatever the limit. <see langword="null"/>
    /// sends nothing.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Gets the ARN of the last stream of the previous page: <see cref="ListStreamsResponse.LastEvaluatedStreamArn"/> of the
    /// previous response, unchanged.
    /// </summary>
    public string? ExclusiveStartStreamArn { get; init; }
}
