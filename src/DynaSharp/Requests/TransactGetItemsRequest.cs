namespace DynaSharp.Requests;

/// <summary>
/// Reads up to 100 items from one or more tables as one consistent snapshot (DynamoDB <c>TransactGetItems</c>).
/// </summary>
/// <remarks>
/// The reads are serializable and strongly consistent: the result reflects the state of the items at one point in time, with
/// respect to the writes of transactions. The tables must be in one AWS account and Region, and the items together can be at most
/// 4 MB. A transaction costs twice the read capacity of the same reads outside a transaction. This is the low-level request: the
/// library does not split a request with more than 100 actions; DynamoDB rejects it with a <see cref="ValidationException"/>.
/// </remarks>
public sealed record TransactGetItemsRequest
{
    /// <summary>Gets the reads, 1 to 100. The response lists the results in the same order.</summary>
    public required IReadOnlyList<TransactGetItem> TransactItems { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="TransactGetItemsResponse.ConsumedCapacity"/>, one entry per table.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }
}
