using static DynaSharp.Expr;

namespace DynaSharp.Coordination;

/// <summary>The fencing of a write in DynamoDB: a step of <see cref="DynamoDbClient.TransactWrite"/> that lets the transaction commit only while a lease holds its lock.</summary>
public static class LeaseTransactExtensions
{
    /// <summary>
    /// Adds a condition check on the item of the lock: the transaction commits only if the lock is still held by this lease, with this
    /// <see cref="Lease.FencingToken"/>. A contender that took the lock over has written its own owner and a higher token, so the transaction of a holder
    /// that lost its lease, and may not know it yet, is cancelled instead of overwriting the work of the next one.
    /// For example <c>await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(ct);</c>.
    /// </summary>
    /// <remarks>
    /// The check reads the item at the moment the transaction commits, which is what makes it safe; <see cref="Lease.Lost"/> is only what this process
    /// knows. A transaction that fails it throws <see cref="TransactionCanceledException"/> with <see cref="TransactionFailure.ConditionalCheckFailed"/> for this
    /// step. A lease that ran out on the local clock still passes while nobody has taken the lock over, which is safe, because nobody else holds it.
    /// The lock item must be in a table of the same account and region as the other items of the transaction.
    /// </remarks>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="lease">The lease.</param>
    /// <returns>The step, which takes more conditions with <c>If</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease"/> is <see langword="null"/>.</exception>
    public static ConditionStep IsHeld<TChain>(this TChain chain, Lease lease)
        where TChain : ITransactWriteChain<TChain>, allows ref struct => IsHeld(chain, lease, out _);

    /// <summary>Adds the check of <see cref="IsHeld{TChain}(TChain, Lease)"/>, and hands out its marker, which tells a lost lease from another reason the transaction failed.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="lease">The lease.</param>
    /// <param name="marker">What finds this check in a cancelled transaction: <c>exception.ReasonFor(marker)</c>.</param>
    /// <returns>The step, which takes more conditions with <c>If</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lease"/> is <see langword="null"/>.</exception>
    public static ConditionStep IsHeld<TChain>(this TChain chain, Lease lease, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(lease);
        var table = lease.Locks.Table;
        return chain.Check(table, LockItem.PartitionKey(lease.Name), LockItem.SortKeyOf(table), out marker)
            .If(Attr(LockItem.Owner).Eq(lease.Owner).And(Attr(LockItem.FencingToken).Eq(lease.FencingToken)));
    }
}
