namespace DynaSharp.Requests;

/// <summary>
/// Reads the Time to Live setting of a table (DynamoDB <c>DescribeTimeToLive</c>).
/// </summary>
public sealed record DescribeTimeToLiveRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }
}
