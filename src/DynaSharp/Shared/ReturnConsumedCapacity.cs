namespace DynaSharp;

/// <summary>
/// Whether DynamoDB reports the capacity units an operation consumed.
/// </summary>
/// <remarks>
/// <see cref="None"/> (the default) sends nothing, which DynamoDB treats as its own <c>NONE</c>.
/// </remarks>
public enum ReturnConsumedCapacity
{
    /// <summary>Do not report consumed capacity. Sends no <c>ReturnConsumedCapacity</c>, which DynamoDB treats as <c>NONE</c>.</summary>
    None = 0,

    /// <summary>Report the total consumed capacity of the operation, DynamoDB name <c>TOTAL</c>.</summary>
    Total,

    /// <summary>Report the total and how it splits across the table and each secondary index the operation touched, DynamoDB name <c>INDEXES</c>.</summary>
    Indexes,
}
