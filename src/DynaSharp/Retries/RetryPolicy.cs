using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Retries;

/// <summary>
/// The AWS "standard" retry mode, tuned for DynamoDB: a bounded number of attempts, full-jitter exponential backoff and a retry
/// quota per client. One instance per client.
/// </summary>
/// <param name="maxAttempts">The number of attempts including the first. 1 disables retries.</param>
/// <param name="random">The source of the jitter. A seam for tests.</param>
internal sealed class RetryPolicy(int maxAttempts, Random random)
{
    public const int RetryCost = 5;
    public const int TimeoutRetryCost = 10;
    public const int SuccessRefund = 1;

    // DynamoDB answers in single-digit milliseconds, so a retry after throttling pays off after tens of milliseconds, not after the second that the generic standard mode
    // waits. Any other failure says less about the table and more about the way to it: a dependency that is still coming up, a proxy that answers before what is behind it.
    // Those take longer than a table, so the default of 4 attempts gives them up to 700 ms, and a change here changes the docs of DynamoDbClientOptions.MaxAttempts.
    public const double ThrottlingBaseMilliseconds = 50;
    public const double TransientBaseMilliseconds = 100;
    public const double MaxDelayMilliseconds = 20_000;

    // A conflict is over when the writer it lost to is done, which takes milliseconds, so the wait only keeps writers that failed together from
    // meeting again. A longer ceiling would not help: what outlasts this one is contention, and waiting does not make that go away.
    public const double ConflictBaseMilliseconds = 10;
    public const double MaxConflictDelayMilliseconds = 1_000;

    // ReadStreamAsync reads a shard again once the attempts of a call failed, which took up to a second or so. What outlasts them is an outage of minutes, so the
    // reader backs off from a second, to at most half a minute between two tries, which a shard's five GetRecords per second shrug off.
    public const double ShardRetryBaseMilliseconds = 1_000;
    public const double MaxShardRetryDelayMilliseconds = 30_000;

    private readonly RetryQuota _quota = new();

    /// <summary>Gets the retry quota tokens left.</summary>
    public int AvailableQuota => _quota.Available;

    /// <summary>Decides what follows a failed attempt, and takes the quota a retry needs.</summary>
    /// <param name="kind">How the attempt failed.</param>
    /// <param name="resendAtOnce">
    /// Whether the client fixed what made the attempt fail, by correcting its signing clock or by dropping expired credentials. The attempt is
    /// then sent again without a wait and without taking quota, however permanent its error looks.
    /// </param>
    /// <param name="attempt">The number of the failed attempt, starting at 1.</param>
    /// <param name="lastRetryCost">The quota the last retry of this call took, or zero if there was none yet.</param>
    /// <param name="callMaxAttempts">The number of attempts of this call, or zero for that of the client.</param>
    /// <returns>The decision.</returns>
    public RetryDecision Decide(FailureKind kind, bool resendAtOnce, int attempt, int lastRetryCost, int callMaxAttempts = 0)
    {
        if (kind == FailureKind.Permanent && !resendAtOnce)
        {
            return new RetryDecision(RetryVerdict.Surface);
        }

        var limit = callMaxAttempts > 0 ? callMaxAttempts : maxAttempts;
        if (attempt >= limit)
        {
            return new RetryDecision(limit == 1 ? RetryVerdict.Surface : RetryVerdict.AttemptsExhausted);
        }

        if (resendAtOnce)
        {
            return new RetryDecision(RetryVerdict.Resend, lastRetryCost: lastRetryCost);
        }

        var cost = kind == FailureKind.Timeout ? TimeoutRetryCost : RetryCost;
        return _quota.TryAcquire(cost)
            ? new RetryDecision(RetryVerdict.Retry, GetDelay(kind, attempt), cost)
            : new RetryDecision(RetryVerdict.QuotaExhausted);
    }

    /// <summary>Returns quota after a request succeeded.</summary>
    /// <param name="lastRetryCost">The quota the last retry of the request took, or zero if it was not retried.</param>
    public void OnSuccess(int lastRetryCost) => _quota.Release(lastRetryCost > 0 ? lastRetryCost : SuccessRefund);

    /// <summary>
    /// Picks a delay uniformly from zero to <c>min(20 s, base * 2^(attempt - 1))</c> ("full jitter"), so that clients that failed at
    /// the same moment do not retry at the same moment.
    /// </summary>
    /// <param name="kind">How the attempt failed; throttling has its own base.</param>
    /// <param name="attempt">The number of the failed attempt, starting at 1.</param>
    /// <returns>The delay.</returns>
    public TimeSpan GetDelay(FailureKind kind, int attempt) =>
        Jitter(kind == FailureKind.Throttling ? ThrottlingBaseMilliseconds : TransientBaseMilliseconds, MaxDelayMilliseconds, attempt);

    /// <summary>
    /// Picks the wait between two runs of a conflict retry, uniformly from zero to <c>min(1 s, 10 ms * 2^(run - 1))</c>. It has the jitter of a retry and nothing else of
    /// one: it asks no quota, because a conflict is no sign that the service is in trouble.
    /// </summary>
    /// <param name="run">The number of the run that failed, starting at 1.</param>
    /// <returns>The delay.</returns>
    public TimeSpan GetConflictDelay(int run) => Jitter(ConflictBaseMilliseconds, MaxConflictDelayMilliseconds, run);

    /// <summary>
    /// Picks the wait before <c>ReadStreamAsync</c> reads a shard again after a transient failure, uniformly from zero to <c>min(30 s, 1 s * 2^(retry - 1))</c>. Like the wait of a
    /// conflict retry it asks no quota: the calls it repeats ask their own.
    /// </summary>
    /// <param name="retry">The number of the retry of this outage of the shard, starting at 1.</param>
    /// <returns>The delay.</returns>
    public TimeSpan GetShardRetryDelay(int retry) => Jitter(ShardRetryBaseMilliseconds, MaxShardRetryDelayMilliseconds, retry);

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Jitter spreads retries in time; it protects nothing.")]
    private TimeSpan Jitter(double baseMilliseconds, double maxMilliseconds, int attempt) =>
        TimeSpan.FromMilliseconds(random.NextDouble() * Math.Min(maxMilliseconds, baseMilliseconds * Math.Pow(2, attempt - 1)));
}
