namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="DescribeTableRequest"/>.
/// </summary>
public sealed record DescribeTableResponse
{
    /// <summary>
    /// Gets the table description. DynamoDB sends one with every response: <see langword="null"/> only if the response omits it.
    /// </summary>
    public TableDescription? Table { get; init; }
}
