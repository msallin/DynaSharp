namespace DynaSharp.Requests;

/// <summary>
/// A global secondary index as DynamoDB reports it. Only the commonly needed subset of what DynamoDB returns.
/// </summary>
/// <remarks>
/// Creating an index on a table that exists takes minutes, even on an empty table: DynamoDB allocates the index and then fills it from the table (<see cref="Backfilling"/>) before it
/// is <see cref="IndexStatus.Active"/>.
/// </remarks>
public sealed record GlobalSecondaryIndexDescription
{
    /// <summary>
    /// Gets the index name. DynamoDB sends it with every index: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>
    /// Gets the lifecycle state. Poll <c>DescribeTable</c> until it is <see cref="Requests.IndexStatus.Active"/> before querying the
    /// index. <see cref="Requests.IndexStatus.Unknown"/> if DynamoDB reported none.
    /// </summary>
    public IndexStatus IndexStatus { get; init; }

    /// <summary>
    /// Gets a value indicating whether the index is still being filled from the table. While it is, queries on the index may miss
    /// items. <see langword="false"/> once the backfill is over and for an index that was created with the table.
    /// </summary>
    public bool Backfilling { get; init; }

    /// <summary>Gets the key of the index.</summary>
    public IReadOnlyList<KeySchemaElement> KeySchema { get; init; } = [];

    /// <summary>Gets the projection, or <see langword="null"/> if DynamoDB did not report one.</summary>
    public IndexProjection? Projection { get; init; }

    /// <summary>
    /// Gets the capacity of the index. <see langword="null"/> if DynamoDB did not report it; an index of a table with
    /// <see cref="BillingMode.PayPerRequest"/> capacity reports zero units.
    /// </summary>
    public ProvisionedThroughputDescription? ProvisionedThroughput { get; init; }

    /// <summary>
    /// Gets the maximum throughput of an index of a table with <see cref="BillingMode.PayPerRequest"/> capacity, or <see langword="null"/> if DynamoDB
    /// reported none, which it does for an index without a maximum.
    /// </summary>
    public OnDemandThroughput? OnDemandThroughput { get; init; }
}
