namespace DynaSharp.Requests;

/// <summary>
/// The read and write capacity of a table or global secondary index as DynamoDB reports it. Only the commonly needed subset of what
/// DynamoDB returns.
/// </summary>
/// <remarks>
/// A table with <see cref="BillingMode.PayPerRequest"/> capacity reports zero units. This is the reported counterpart of
/// <see cref="ProvisionedThroughput"/>, which is what a request sets.
/// </remarks>
public sealed record ProvisionedThroughputDescription
{
    /// <summary>Gets the read capacity units per second. 0 for a table with on-demand capacity.</summary>
    public long ReadCapacityUnits { get; init; }

    /// <summary>Gets the write capacity units per second. 0 for a table with on-demand capacity.</summary>
    public long WriteCapacityUnits { get; init; }
}
