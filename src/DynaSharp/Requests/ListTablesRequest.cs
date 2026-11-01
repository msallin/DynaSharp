namespace DynaSharp.Requests;

/// <summary>
/// Lists the names of the tables of the account in the region (DynamoDB <c>ListTables</c>).
/// </summary>
/// <remarks>
/// The result is paged and sorted by name. A table that was just created may not be listed yet, and one that was just deleted may
/// still be.
/// </remarks>
public sealed record ListTablesRequest
{
    /// <summary>
    /// Gets the name of the last table of the previous page: <see cref="ListTablesResponse.LastEvaluatedTableName"/> of the previous
    /// response, unchanged. <see langword="null"/> starts at the first table.
    /// </summary>
    public string? ExclusiveStartTableName { get; init; }

    /// <summary>
    /// Gets the maximum number of names to return, from 1 to 100; DynamoDB rejects other values. <see langword="null"/> sends
    /// nothing, and DynamoDB returns up to 100.
    /// </summary>
    public int? Limit { get; init; }
}
