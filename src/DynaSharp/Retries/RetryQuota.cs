namespace DynaSharp.Retries;

/// <summary>
/// The retry quota of the AWS "standard" retry mode (<c>botocore.retries.quota</c>): a pool of tokens per client that retries draw
/// from, so that a service that keeps failing is not hammered by every caller at once.
/// </summary>
/// <remarks>
/// Thread-safe without a lock. Acquiring subtracts the cost first and gives it back if that overdrew the pool, so the balance
/// never depends on the order of concurrent callers. Releasing when the pool is full, which is the case for every successful
/// request that was not retried, is a single read.
/// </remarks>
internal sealed class RetryQuota
{
    public const int Capacity = 500;

    private int _available = Capacity;

    /// <summary>Gets the tokens left.</summary>
    public int Available => Volatile.Read(ref _available);

    /// <summary>Takes tokens, if there are enough.</summary>
    /// <param name="amount">The tokens to take.</param>
    /// <returns><see langword="true"/> if the tokens were taken; <see langword="false"/> if the pool holds fewer and is left unchanged.</returns>
    public bool TryAcquire(int amount)
    {
        if (Interlocked.Add(ref _available, -amount) >= 0)
        {
            return true;
        }

        // Overdrawn: undo. A caller that looks at the pool in between may be refused although the tokens are back a moment later.
        Interlocked.Add(ref _available, amount);
        return false;
    }

    /// <summary>Puts tokens back, up to <see cref="Capacity"/>.</summary>
    /// <param name="amount">The tokens to give back.</param>
    public void Release(int amount)
    {
        if (Volatile.Read(ref _available) >= Capacity)
        {
            return;
        }

        // Each caller removes what its own addition put above the capacity. Over concurrent callers that sums to the total excess,
        // so the pool settles at the capacity and never above it.
        var after = Interlocked.Add(ref _available, amount);
        var excess = Math.Min(amount, after - Capacity);
        if (excess > 0)
        {
            Interlocked.Add(ref _available, -excess);
        }
    }
}
