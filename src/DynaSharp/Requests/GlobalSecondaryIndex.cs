namespace DynaSharp.Requests;

/// <summary>
/// A global secondary index of a table that is created: its own partition key and optional sort key, possibly other attributes
/// than those of the table.
/// </summary>
/// <remarks>
/// A global secondary index is eventually consistent, has throughput of its own, and a throttled index throttles the writes to the
/// table. An item only appears in the index if it has all key attributes of the index (a sparse index). A table can have at most
/// 20 global secondary indexes, and more can be added later.
/// </remarks>
public sealed record GlobalSecondaryIndex
{
    /// <summary>Gets the index name: 3 to 255 characters of letters, digits, underscore, hyphen and dot, unique within the table.</summary>
    public required string IndexName { get; init; }

    /// <summary>Gets the key of the index: the partition key, followed by the sort key if there is one.</summary>
    public required IReadOnlyList<KeySchemaElement> KeySchema { get; init; }

    /// <summary>Gets the attributes copied into the index.</summary>
    public required IndexProjection Projection { get; init; }

    /// <summary>
    /// Gets the capacity of the index. Required when the table has <see cref="BillingMode.Provisioned"/> capacity and not allowed
    /// with <see cref="BillingMode.PayPerRequest"/>.
    /// </summary>
    public ProvisionedThroughput? ProvisionedThroughput { get; init; }
}
