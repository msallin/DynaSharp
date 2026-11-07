namespace DynaSharp;

/// <summary>
/// Which lost races <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, ConflictKind, int, Action{int, DynamoDbException}, CancellationToken)"/> runs its delegate again on. Without it, a retry takes
/// <see cref="Any"/>.
/// </summary>
/// <remarks>
/// A failed condition and a transaction in the way are different answers. A failed condition says that the item is not what the run expected, which a version check expects to see again after another
/// writer got through, and which can also be the answer itself, such as an address that is taken. A transaction in the way says nothing about the item: only asking again tells. A run that turns every failed
/// condition into its result, or whose failed condition is an answer that asking again does not change, wants <see cref="TransactionConflict"/> alone.
/// </remarks>
[Flags]
public enum ConflictKind
{
    /// <summary>
    /// A failed condition: a <see cref="ConditionalCheckFailedException"/>, or a <see cref="TransactionCanceledException"/> with <see cref="TransactionFailure.ConditionalCheckFailed"/> among its reasons. This is what
    /// optimistic locking loses with, where the run reads the new version and writes again.
    /// </summary>
    ConditionalCheckFailed = 1,

    /// <summary>
    /// A transaction in the way: a <see cref="TransactionConflictException"/>, which a write outside a transaction gets on an item a transaction holds, or a <see cref="TransactionCanceledException"/> with
    /// <see cref="TransactionFailure.TransactionConflict"/> among its reasons. The item may be what the run expects once the other one is done.
    /// </summary>
    TransactionConflict = 2,

    /// <summary>Both, which is what the overloads without a <see cref="ConflictKind"/> retry.</summary>
    Any = ConditionalCheckFailed | TransactionConflict,
}
