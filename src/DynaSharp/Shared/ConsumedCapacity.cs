namespace DynaSharp;

/// <summary>
/// The capacity units an operation consumed, reported when the request asked for it with
/// <see cref="ReturnConsumedCapacity.Total"/> or <see cref="ReturnConsumedCapacity.Indexes"/>.
/// </summary>
/// <remarks>
/// For on-demand tables these are request units; for provisioned tables, capacity units. The read and write split is
/// only present when DynamoDB reports it.
/// </remarks>
public sealed record ConsumedCapacity
{
    /// <summary>
    /// Gets the table the capacity was consumed on. Non-null in the capacity DynamoDB reports; <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    /// Gets the total capacity units consumed. Non-null in the capacity DynamoDB reports; <see langword="null"/> only if the response omits it.
    /// </summary>
    public double? CapacityUnits { get; init; }

    /// <summary>
    /// Gets the read capacity units consumed. <see langword="null"/> when DynamoDB does not report it separately.
    /// </summary>
    public double? ReadCapacityUnits { get; init; }

    /// <summary>
    /// Gets the write capacity units consumed. <see langword="null"/> when DynamoDB does not report it separately.
    /// </summary>
    public double? WriteCapacityUnits { get; init; }

    /// <summary>Gets the capacity consumed on the table itself. Only with <see cref="ReturnConsumedCapacity.Indexes"/>.</summary>
    public CapacityDetail? Table { get; init; }

    /// <summary>Gets the capacity consumed per local secondary index. Only with <see cref="ReturnConsumedCapacity.Indexes"/>.</summary>
    public IReadOnlyDictionary<string, CapacityDetail>? LocalSecondaryIndexes { get; init; }

    /// <summary>Gets the capacity consumed per global secondary index. Only with <see cref="ReturnConsumedCapacity.Indexes"/>.</summary>
    public IReadOnlyDictionary<string, CapacityDetail>? GlobalSecondaryIndexes { get; init; }
}
