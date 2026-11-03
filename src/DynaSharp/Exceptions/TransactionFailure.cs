namespace DynaSharp;

/// <summary>
/// Why one action of a cancelled transaction did not take effect, as a value: <see cref="TransactionCanceledException.ReasonFor"/>. Each member says whether
/// repeating the same transaction can pass, because some causes go away by themselves and others never do.
/// </summary>
/// <remarks>When adding a member: map its code in <see cref="TransactionCanceledException"/>, say whether it passes later for <see cref="DynamoDbException.IsTransient"/>, and add it to <c>StubResponse.Reason</c> in <c>DynaSharp.Testing</c>. A code this library does not know reads as <see cref="Unknown"/>.</remarks>
public enum TransactionFailure
{
    /// <summary>A code this library does not know, which <see cref="CancellationReason.Code"/> shows, or no reason at all for the action. Whether a retry can pass is not known.</summary>
    Unknown = 0,

    /// <summary>
    /// The action caused no problem. It did not take effect, because another one failed. Look at the others: the transaction was cancelled because of them.
    /// </summary>
    None,

    /// <summary>The condition of the action did not hold. Will not pass on a retry: the data must change first, so read it, or use <see cref="TransactionCanceledException.ItemFor"/>, and decide anew.</summary>
    ConditionalCheckFailed,

    /// <summary>The item collection of a table with a local secondary index would exceed 10 GB. Will not pass on a retry: data must go first.</summary>
    ItemCollectionSizeLimitExceeded,

    /// <summary>An item takes part in another transaction or request right now. May pass on a retry after a short wait: the conflict is over when the other one is.</summary>
    TransactionConflict,

    /// <summary>
    /// DynamoDB did not have the capacity for the action (the codes <c>ProvisionedThroughputExceeded</c> and <c>ThrottlingError</c>). May pass on a retry with a backoff, once
    /// the table has capacity again.
    /// </summary>
    Throttled,

    /// <summary>The action is invalid, for example an expression that does not parse or a key that does not fit the table. Will not pass on a retry: the request must change.</summary>
    ValidationError,
}
