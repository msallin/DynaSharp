namespace DynaSharp.Coordination.Testing;

/// <summary>
/// What other processes do to a lock, for tests: hold it, release it, delete its item; and what a test looks at: the state of a lock, and a contender that
/// was turned away. They read and write the item of the lock through the table of the <see cref="LockTable"/>, as those processes would, so a test sets up
/// and checks a situation without knowing the format of the item. They work on any table, a <c>FakeDynamoDb</c> or an emulator, and are not meant for a
/// table in production: they write without a condition.
/// </summary>
/// <remarks>
/// A contender takes a lock over only after it has seen the same holder for a whole lease on its own clock, so a test that wants a takeover holds the lock
/// with <see cref="SimulateHolderAsync"/>, lets the <see cref="LockTable"/> see it, and then moves the fake clock of the client by the lease.
/// </remarks>
public static class LockTableTesting
{
    /// <summary>
    /// Makes another process hold the lock, as if it had just acquired it and then never renewed it, as a holder that crashed: a new owner and record
    /// version, the fencing token raised by one, the lease duration, and the label, if one is given. A lease of this process on the lock is lost at its
    /// next renewal, as <c>taken_over</c>, and a contender takes the lock over once it has seen this holder for a whole lease.
    /// </summary>
    /// <param name="locks">The lock table.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">The lease of the other holder, from <see cref="LockTable.MinLeaseDuration"/> to <see cref="LockTable.MaxLeaseDuration"/>.</param>
    /// <param name="holderLabel">The label of the other holder, as <see cref="LockTableOptions.HolderLabel"/> would write it, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The fencing token of the other holder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below <see cref="LockTable.MinLeaseDuration"/> or above <see cref="LockTable.MaxLeaseDuration"/>.</exception>
    public static async Task<long> SimulateHolderAsync(this LockTable locks, string name, TimeSpan leaseDuration, string? holderLabel = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        LockTable.Validate(name, leaseDuration);
        var update = locks.Stamp(Update(locks, name))
            .Set(LockItem.Type, LockItem.Discriminator)
            .Set(LockItem.Owner, LockItem.NewId())
            .Set(LockItem.RecordVersion, LockItem.NewId())
            .Set(LockItem.LeaseDuration, LockItem.Milliseconds(leaseDuration))
            .Add(LockItem.FencingToken, 1L);
        update = holderLabel is null ? update.Remove(LockItem.HolderLabel) : update.Set(LockItem.HolderLabel, holderLabel);
        var after = (await update.ReturnNew().SendAsync(cancellationToken).ConfigureAwait(false)).Item;
        return LockItem.FencingTokenOf(after, name, locks.Table.Name);
    }

    /// <summary>
    /// Releases the lock as its holder would, whoever holds it: the owner, the lease and the label are removed, the fencing token stays. A lease of this
    /// process on the lock is lost at its next renewal, as <c>taken_over</c>, and the next acquire finds the lock free. A lock without an item gets a free one.
    /// </summary>
    /// <param name="locks">The lock table.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the lock is free.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static async Task SimulateReleaseAsync(this LockTable locks, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentException.ThrowIfNullOrEmpty(name);
        await locks.Stamp(Update(locks, name))
            .Remove(LockItem.Owner)
            .Remove(LockItem.LeaseDuration)
            .Remove(LockItem.HolderLabel)
            .Set(LockItem.Type, LockItem.Discriminator)
            .Set(LockItem.RecordVersion, LockItem.NewId())
            .SendAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the item of the lock, as time to live or an operator would. A lease of this process on the lock is lost at its next renewal, as
    /// <c>deleted</c>, and the next acquire starts the fencing token at 1 again. A lock without an item stays without one.
    /// </summary>
    /// <param name="locks">The lock table.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the item is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static async Task SimulateDeleteAsync(this LockTable locks, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentException.ThrowIfNullOrEmpty(name);
        await locks.Table.Delete(LockItem.PartitionKey(name), LockItem.SortKeyOf(locks.Table)).SendAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the item of a lock with a strongly consistent <c>GetItem</c> and says what it holds: whether a lease holds the lock, the holder label, the
    /// lease duration and the fencing token. A test asserts on this instead of on the attributes of the item.
    /// </summary>
    /// <param name="locks">The lock table.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The state, or <see langword="null"/> if the lock has no item: never acquired, or deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock has an <c>owner</c> but is not a lock item written by <see cref="LockTable"/>.</exception>
    public static async Task<LockState?> GetStateAsync(this LockTable locks, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var item = await locks.Table.Get(LockItem.PartitionKey(name), LockItem.SortKeyOf(locks.Table)).Consistent().ItemAsync(cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        var holder = LockItem.HolderOf(item, name, locks.Table.Name);
        return new LockState(holder is not null, LockItem.HolderLabelOf(item), holder?.LeaseDuration, LockItem.FencingTokenOf(item, name, locks.Table.Name));
    }

    /// <summary>
    /// Waits until a try of this <see cref="LockTable"/> to acquire the lock finds it held by another lease and is turned away: a <c>TryAcquireAsync</c>
    /// that returns <see langword="null"/>, or a poll of <c>AcquireAsync</c> or <c>RunAsLeaderAsync</c> that waits on. A test sees that a contender
    /// tried and was refused without counting its requests, which change with how the contender polls.
    /// </summary>
    /// <remarks>
    /// The task waits for the next refusal after this call, so take it before the contender starts: a refusal that came before is not counted, and the task
    /// then completes at the contender's next poll, which on a fake clock needs the clock to move.
    /// </remarks>
    /// <param name="locks">The lock table of the contender.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="cancellationToken">Ends the wait with an <see cref="OperationCanceledException"/>.</param>
    /// <returns>A task that completes at the next refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static Task WaitForRefusalAsync(this LockTable locks, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return locks.NextRefusal(name).WaitAsync(cancellationToken);
    }

    private static UpdateCommand<Item> Update(LockTable locks, string name) => locks.Table.Update(LockItem.PartitionKey(name), LockItem.SortKeyOf(locks.Table));
}
