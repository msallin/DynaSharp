namespace DynaSharp.Retries;

/// <summary>The outcome of <see cref="RetryPolicy.Decide"/>.</summary>
/// <param name="verdict">What to do.</param>
/// <param name="delay">How long to wait before the next attempt. Zero unless the verdict is <see cref="RetryVerdict.Retry"/>.</param>
/// <param name="lastRetryCost">
/// The quota tokens the last retry of the call took, which a success gives back. Zero as long as the call was not retried;
/// meaningful only if the verdict makes another attempt.
/// </param>
internal readonly struct RetryDecision(RetryVerdict verdict, TimeSpan delay = default, int lastRetryCost = 0)
{
    public RetryVerdict Verdict { get; } = verdict;

    public TimeSpan Delay { get; } = delay;

    public int LastRetryCost { get; } = lastRetryCost;

    /// <summary>Gets a value indicating whether another attempt follows.</summary>
    public bool ShouldRetry => Verdict is RetryVerdict.Resend or RetryVerdict.Retry;
}
