namespace DynaSharp.Requests;

/// <summary>
/// Applies up to 100 puts, updates, deletes and condition checks to one or more tables, all or nothing (DynamoDB
/// <c>TransactWriteItems</c>).
/// </summary>
/// <remarks>
/// <para>
/// Either every action takes effect or none does. The tables must be in one AWS account and Region, no two actions can target the
/// same item (also not a condition check and an update), and the items together can be at most 4 MB; an item can be at most
/// 400 KB. A transaction costs twice the write capacity of the same writes outside a transaction. This is the low-level request: the
/// library does not split a request with more than 100 actions; DynamoDB rejects it with a <see cref="ValidationException"/>.
/// </para>
/// <para>
/// The transaction is cancelled with a <see cref="TransactionCanceledException"/> when a condition does not hold, when an item takes
/// part in another transaction (<c>TransactionConflict</c> in the cancellation reasons), when a table is throttled, or when an
/// action is invalid. The exception lists a reason per action, in the order of <see cref="TransactItems"/>.
/// </para>
/// </remarks>
public sealed record TransactWriteItemsRequest
{
    /// <summary>Gets the actions, 1 to 100. The cancellation reasons of a failed transaction are listed in the same order.</summary>
    public required IReadOnlyList<TransactWriteItem> TransactItems { get; init; }

    /// <summary>Gets whether to report consumed capacity in <see cref="TransactWriteItemsResponse.ConsumedCapacity"/>, one entry per table.</summary>
    public ReturnConsumedCapacity ReturnConsumedCapacity { get; init; }

    /// <summary>
    /// Gets whether to report the size of the item collections that the transaction modified in
    /// <see cref="TransactWriteItemsResponse.ItemCollectionMetrics"/>. Only tables with a local secondary index have item collections.
    /// </summary>
    public ReturnItemCollectionMetrics ReturnItemCollectionMetrics { get; init; }

    /// <summary>
    /// Gets the idempotency token of the transaction, 1 to 36 characters. <see langword="null"/> (the default) lets the library
    /// generate a new GUID for every call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DynamoDB remembers a token for 10 minutes after the first request that used it was completed. A request with the same token
    /// and the same parameters within that time does not apply the transaction again; it returns the outcome of the first one. A
    /// request with the same token and other parameters within that time fails with an
    /// <see cref="IdempotentParameterMismatchException"/>. A request that arrives while the first one is still running fails with a
    /// <see cref="TransactionInProgressException"/>. After 10 minutes the token is forgotten and the request counts as a new one.
    /// </para>
    /// <para>
    /// The library serializes a request once per call, so all attempts of one call, including the retries it makes after a timeout
    /// or a network error, carry the same token. That makes those retries safe: an attempt whose response was lost is not applied a
    /// second time. The generated token is not returned. A caller that repeats a call itself after an exception, for example after
    /// the client gave up, has to supply a token of its own to get the same protection, and must repeat the call within 10 minutes
    /// with unchanged parameters.
    /// </para>
    /// </remarks>
    public string? ClientRequestToken { get; init; }
}
