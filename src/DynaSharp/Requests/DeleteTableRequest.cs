namespace DynaSharp.Requests;

/// <summary>
/// Deletes a table and all its items (DynamoDB <c>DeleteTable</c>).
/// </summary>
/// <remarks>
/// Deletion is asynchronous and cannot be undone, except through a backup: the table moves to <see cref="TableStatus.Deleting"/> and
/// disappears when DynamoDB is done. A table with deletion protection cannot be deleted.
/// </remarks>
public sealed record DeleteTableRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }
}
