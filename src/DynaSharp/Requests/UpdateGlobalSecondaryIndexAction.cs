namespace DynaSharp.Requests;

/// <summary>
/// Changes the capacity or the on-demand maximums of a global secondary index, as part of a <see cref="GlobalSecondaryIndexUpdate"/>.
/// </summary>
/// <remarks>
/// Set <see cref="ProvisionedThroughput"/> for an index of a table with <see cref="BillingMode.Provisioned"/> capacity, and <see cref="OnDemandThroughput"/>
/// for one with <see cref="BillingMode.PayPerRequest"/> capacity. The key and the projection of an index cannot be changed: delete the index and create it again.
/// </remarks>
public sealed record UpdateGlobalSecondaryIndexAction
{
    /// <summary>Gets the name of the index to change.</summary>
    public required string IndexName { get; init; }

    /// <summary>Gets the new capacity of an index of a table with provisioned capacity. <see langword="null"/> sends nothing.</summary>
    public ProvisionedThroughput? ProvisionedThroughput { get; init; }

    /// <summary>
    /// Gets the new maximum throughput of an index of a table with on-demand capacity. <see langword="null"/> keeps the current maximums, and so does a
    /// member of it that is <see langword="null"/>; <c>-1</c> removes a maximum.
    /// </summary>
    public OnDemandThroughput? OnDemandThroughput { get; init; }
}
