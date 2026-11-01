namespace DynaSharp.Requests;

/// <summary>
/// One page of the result of <see cref="ListTablesRequest"/>.
/// </summary>
public sealed record ListTablesResponse
{
    /// <summary>Gets the table names of this page, sorted. Never <see langword="null"/>; empty when the account has no tables.</summary>
    public IReadOnlyList<string> TableNames { get; init; } = [];

    /// <summary>
    /// Gets the name of the last table evaluated, to pass as <see cref="ListTablesRequest.ExclusiveStartTableName"/> to read the next
    /// page. <see langword="null"/> on the last page; only <see langword="null"/> means the end, so do not stop at a page with fewer
    /// names than the limit.
    /// </summary>
    public string? LastEvaluatedTableName { get; init; }
}
