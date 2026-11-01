namespace DynaSharp.Requests;

/// <summary>
/// Changes the capacity of a global secondary index, as part of a <see cref="GlobalSecondaryIndexUpdate"/>.
/// </summary>
/// <remarks>
/// Only an index of a table with <see cref="BillingMode.Provisioned"/> capacity has capacity of its own. The key and the projection of
/// an index cannot be changed: delete the index and create it again.
/// </remarks>
public sealed record UpdateGlobalSecondaryIndexAction
{
    /// <summary>Gets the name of the index to change.</summary>
    public required string IndexName { get; init; }

    /// <summary>Gets the new capacity of the index.</summary>
    public required ProvisionedThroughput ProvisionedThroughput { get; init; }
}
