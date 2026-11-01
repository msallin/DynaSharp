namespace DynaSharp.Requests;

/// <summary>
/// The read and write capacity of a table or global secondary index with <see cref="BillingMode.Provisioned"/> capacity.
/// </summary>
/// <remarks>
/// One read capacity unit is one strongly consistent read per second of an item up to 4 KB (two eventually consistent reads); one
/// write capacity unit is one write per second of an item up to 1 KB. Requests above the capacity are throttled.
/// </remarks>
public sealed record ProvisionedThroughput
{
    /// <summary>Gets the read capacity units per second. At least 1.</summary>
    public required long ReadCapacityUnits { get; init; }

    /// <summary>Gets the write capacity units per second. At least 1.</summary>
    public required long WriteCapacityUnits { get; init; }
}
