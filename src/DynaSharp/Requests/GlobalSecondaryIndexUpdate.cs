namespace DynaSharp.Requests;

/// <summary>
/// One change to the global secondary indexes of a table, in an <see cref="UpdateTableRequest"/>: an index to create, to update or to
/// delete.
/// </summary>
/// <remarks>
/// Set exactly one of <see cref="Create"/>, <see cref="Update"/> and <see cref="Delete"/>. The library sends whatever is set and
/// leaves the check to DynamoDB, which rejects an update with none or with several members with a <see cref="ValidationException"/>.
/// </remarks>
public sealed record GlobalSecondaryIndexUpdate
{
    /// <summary>
    /// Gets the index to create, or <see langword="null"/> if this change is another kind. The key attributes of the index must be
    /// defined in <see cref="UpdateTableRequest.AttributeDefinitions"/> or in the table already. The index is filled from the table
    /// (<see cref="GlobalSecondaryIndexDescription.Backfilling"/>) after DynamoDB has allocated it, which takes minutes on an empty table and more on one that holds data.
    /// </summary>
    public GlobalSecondaryIndex? Create { get; init; }

    /// <summary>Gets the index whose capacity or on-demand maximums to change, or <see langword="null"/> if this change is another kind.</summary>
    public UpdateGlobalSecondaryIndexAction? Update { get; init; }

    /// <summary>Gets the index to delete, or <see langword="null"/> if this change is another kind.</summary>
    public DeleteGlobalSecondaryIndexAction? Delete { get; init; }
}
