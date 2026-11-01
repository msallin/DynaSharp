namespace DynaSharp.Requests;

/// <summary>
/// A local secondary index as DynamoDB reports it. Only the commonly needed subset of what DynamoDB returns.
/// </summary>
public sealed record LocalSecondaryIndexDescription
{
    /// <summary>
    /// Gets the index name. DynamoDB sends it with every index: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>Gets the key of the index.</summary>
    public IReadOnlyList<KeySchemaElement> KeySchema { get; init; } = [];

    /// <summary>Gets the projection, or <see langword="null"/> if DynamoDB did not report one.</summary>
    public IndexProjection? Projection { get; init; }
}
