namespace DynaSharp.Requests;

/// <summary>
/// A local secondary index of a table that is created: the same partition key as the table, a different sort key.
/// </summary>
/// <remarks>
/// Local secondary indexes can only be defined when the table is created and never removed. They share the throughput of the table,
/// can be read with strong consistency, and limit every item collection (all items with one partition key value) to 10 GB.
/// A table can have at most five.
/// </remarks>
public sealed record LocalSecondaryIndex
{
    /// <summary>Gets the index name: 3 to 255 characters of letters, digits, underscore, hyphen and dot, unique within the table.</summary>
    public required string IndexName { get; init; }

    /// <summary>Gets the key of the index: the partition key of the table followed by the sort key of the index.</summary>
    public required IReadOnlyList<KeySchemaElement> KeySchema { get; init; }

    /// <summary>Gets the attributes copied into the index.</summary>
    public required IndexProjection Projection { get; init; }
}
