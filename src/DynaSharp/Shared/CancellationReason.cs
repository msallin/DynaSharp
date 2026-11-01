namespace DynaSharp;

/// <summary>
/// Why one action of a cancelled transaction did not take effect, reported by
/// <see cref="TransactionCanceledException.CancellationReasons"/>.
/// </summary>
/// <remarks>
/// An action that caused no problem has the code <c>None</c>. A transaction is cancelled because of at least one other action
/// then, and the actions with the code <c>None</c> did not take effect either.
/// </remarks>
public sealed record CancellationReason
{
    /// <summary>
    /// Gets the code DynamoDB gives the reason, as a string because the service adds codes. The documented ones are <c>None</c>,
    /// <c>ConditionalCheckFailed</c>, <c>ItemCollectionSizeLimitExceeded</c>, <c>TransactionConflict</c>,
    /// <c>ProvisionedThroughputExceeded</c>, <c>ThrottlingError</c> and <c>ValidationError</c>. <see langword="null"/> only if
    /// DynamoDB leaves the code out.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>Gets the message that belongs to the code, or <see langword="null"/> when DynamoDB sends none, as for <c>None</c>.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the item the condition was evaluated against, when the action set
    /// <see cref="ReturnValuesOnConditionCheckFailure.AllOld"/>, its condition failed and an item existed; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Item { get; init; }
}
