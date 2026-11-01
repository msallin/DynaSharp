namespace DynaSharp.Requests;

/// <summary>
/// Reads the definition and state of a table (DynamoDB <c>DescribeTable</c>).
/// </summary>
/// <remarks>
/// The result is eventually consistent: right after creating a table it may still be reported as missing.
/// </remarks>
public sealed record DescribeTableRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }
}
