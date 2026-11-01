using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace DynaSharp;

/// <summary>
/// The whole transaction was cancelled, so none of its actions took effect. Not retried by the client, because repeating the same
/// transaction usually meets the same cause. <see cref="ReasonFor"/> says what became of an action, <see cref="ItemFor"/> what its condition
/// saw, <see cref="Failures"/> which actions failed, and <see cref="CancellationReasons"/> holds what DynamoDB sent.
/// </summary>
/// <remarks>
/// A <c>TransactWriteItems</c> call is cancelled when a condition does not hold, when an item takes part in another transaction,
/// when a table is throttled, when an item or an index grows too large, and when an action is invalid. A <c>TransactGetItems</c>
/// call is cancelled when it conflicts with a concurrent write or is throttled. Some causes pass (a conflict, throttling), so
/// whether to repeat the transaction is the decision of the caller, who can look at the reasons; <see cref="TransactionFailure"/> says which.
/// </remarks>
public sealed class TransactionCanceledException : DynamoDbException
{
    // The codes of the service. When adding one: add a member to TransactionFailure, with what a retry does.
    private static readonly FrozenDictionary<string, TransactionFailure> Failure = new Dictionary<string, TransactionFailure>(StringComparer.Ordinal)
    {
        ["None"] = TransactionFailure.None,
        ["ConditionalCheckFailed"] = TransactionFailure.ConditionalCheckFailed,
        ["ItemCollectionSizeLimitExceeded"] = TransactionFailure.ItemCollectionSizeLimitExceeded,
        ["TransactionConflict"] = TransactionFailure.TransactionConflict,
        ["ProvisionedThroughputExceeded"] = TransactionFailure.Throttled,
        ["ThrottlingError"] = TransactionFailure.Throttled,
        ["ValidationError"] = TransactionFailure.ValidationError,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionCanceledException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="cancellationReasons">The reasons, one per action and in the order of the actions. <see langword="null"/> is treated as none.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public TransactionCanceledException(string message, IReadOnlyList<CancellationReason>? cancellationReasons = null, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "TransactionCanceledException", statusCode, requestId)
    {
        CancellationReasons = cancellationReasons ?? [];
        Failures = FindFailures(CancellationReasons);
    }

    /// <summary>
    /// Gets the reasons, one for each action of the request and in the same order: the reason at an index belongs to the action at
    /// that index. An action that caused no problem has the code <c>None</c>. Empty when DynamoDB sends no reasons, for example for a
    /// cancellation that is not tied to a particular action. Never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<CancellationReason> CancellationReasons { get; }

    /// <summary>
    /// Gets the actions that made the transaction fail, in the order of the transaction, each with its position and reason: every action but those that caused no
    /// problem. It holds no keys or values, so it is what to log. Empty when DynamoDB sends no reasons.
    /// </summary>
    public IReadOnlyList<TransactionActionFailure> Failures { get; }

    /// <summary>Gets what became of an action.</summary>
    /// <param name="marker">The marker of the action.</param>
    /// <returns>
    /// The reason. <see cref="TransactionFailure.None"/> if this action caused no problem, which does not mean it took effect: nothing did.
    /// <see cref="TransactionFailure.Unknown"/> for a code this library does not know, and when DynamoDB sent no reason for the action.
    /// </returns>
    /// <exception cref="ArgumentException">The marker is a default value, so the action was never added.</exception>
    public TransactionFailure ReasonFor(TransactMarker marker) => ReasonAt(marker) is { } reason ? ToFailure(reason.Code) : TransactionFailure.Unknown;

    /// <summary>
    /// Gets the item the condition of an action saw, when the action set <c>ReturnOldOnFailure()</c>, its condition failed and an item existed. On a typed table,
    /// <see cref="Item.To{T}"/> reads it as the entity.
    /// </summary>
    /// <param name="marker">The marker of the action.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">The marker is a default value, so the action was never added.</exception>
    [SuppressMessage("Style", "IDE0028:Simplify collection initialization", Justification = "An item has no collection form that takes a dictionary.")]
    public Item? ItemFor(TransactMarker marker) => ReasonAt(marker)?.Item switch
    {
        null => null,
        Item item => item,
        var attributes => new Item(attributes),
    };

    private static TransactionFailure ToFailure(string? code) => code is not null && Failure.TryGetValue(code, out var failure) ? failure : TransactionFailure.Unknown;

    private static List<TransactionActionFailure> FindFailures(IReadOnlyList<CancellationReason> reasons)
    {
        var failures = new List<TransactionActionFailure>();
        for (var i = 0; i < reasons.Count; i++)
        {
            var failure = ToFailure(reasons[i].Code);
            if (failure != TransactionFailure.None)
            {
                failures.Add(new TransactionActionFailure(i, failure));
            }
        }

        return failures;
    }

    private CancellationReason? ReasonAt(TransactMarker marker)
    {
        var index = marker.Index;
        return index < CancellationReasons.Count ? CancellationReasons[index] : null;
    }
}
