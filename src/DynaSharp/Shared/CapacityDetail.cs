namespace DynaSharp;

/// <summary>
/// The capacity units consumed on a table or an index, as part of <see cref="ConsumedCapacity"/>.
/// </summary>
public sealed record CapacityDetail
{
    /// <summary>
    /// Gets the total capacity units consumed. <see langword="null"/> when DynamoDB does not report it.
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
}
