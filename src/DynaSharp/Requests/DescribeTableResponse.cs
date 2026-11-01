namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="DescribeTableRequest"/>.
/// </summary>
public sealed record DescribeTableResponse
{
    /// <summary>Gets the table description.</summary>
    public required TableDescription Table { get; init; }
}
