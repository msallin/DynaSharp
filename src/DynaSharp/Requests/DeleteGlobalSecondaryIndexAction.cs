namespace DynaSharp.Requests;

/// <summary>
/// Deletes a global secondary index, as part of a <see cref="GlobalSecondaryIndexUpdate"/>.
/// </summary>
/// <remarks>
/// Deleting an index is asynchronous: it is reported as <see cref="IndexStatus.Deleting"/> for a while and then disappears from the
/// description of the table. The items of the table are not affected.
/// </remarks>
public sealed record DeleteGlobalSecondaryIndexAction
{
    /// <summary>Gets the name of the index to delete.</summary>
    public required string IndexName { get; init; }
}
