using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Coordination;

/// <summary>The loop of <see cref="LockTable.RunAsLeaderAsync"/>, whose remarks are the contract: contend, lead for a term, contend again after a loss.</summary>
internal static class Leadership
{
    /// <summary>Leads on a lock until the work ends on its own or the caller cancels. The arguments are checked.</summary>
    /// <param name="locks">The table of the lock.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long a lease lasts without a renewal.</param>
    /// <param name="work">The work of the leader.</param>
    /// <param name="cancellationToken">Ends the leadership.</param>
    /// <returns>A task that completes as <see cref="LockTable.RunAsLeaderAsync"/> says.</returns>
    public static async Task RunAsync(LockTable locks, string name, TimeSpan leaseDuration, Func<Lease, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var outage = new Outage(locks, name);
        Action answered = outage.End;

        // The claim outlives a failed acquire, whose write DynamoDB may have applied, and ends with the lease it hands out.
        var claim = new LockTable.Claim();
        while (true)
        {
            Lease lease;
            try
            {
                lease = (await locks.ContendAsync(name, leaseDuration, null, claim, answered, timeoutThrows: true, cancellationToken).ConfigureAwait(false))!;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransient(exception))
            {
                var delay = leaseDuration / 3;
                outage.Fail(exception, delay);
                await Task.Delay(delay, locks.Time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            claim = new LockTable.Claim();

            // An acquire whose answer came after a whole lease hands out a lease that has ended: no term starts on it.
            if (lease.Ended.IsCancellationRequested)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            bool lost;
            try
            {
                lost = await LeadAsync(locks, lease, work, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            if (!lost)
            {
                return;
            }
        }
    }

    /// <summary>Gets whether an acquire that failed with an exception is worth another try after a wait.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for an error that a later call can succeed after, with no change.</returns>
    internal static bool IsTransient(Exception exception) =>
        exception is DynamoDbException { IsTransient: true } or TimeoutException or HttpRequestException or CredentialsUnavailableException;

    // Runs the work of one term. True if the lease was lost, so the leadership goes on.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Whatever work that lost its lease throws ends that term only; the loss is logged.")]
    private static async Task<bool> LeadAsync(LockTable locks, Lease lease, Func<Lease, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        CoordinationLog.Elected(locks.Logger, lease.Name, locks.Table.Name, lease.FencingToken, locks.Options.HolderLabel);
        var returned = false;
        var done = false;
        try
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Ended))
            {
                // Lease.Current is set for the work and what it starts. An async method never passes the change of an AsyncLocal back to its caller, so the
                // term ends without a reset.
                Lease.SetCurrent(lease);
                try
                {
                    await work(lease, stop.Token).ConfigureAwait(false);

                    // Work that returned before its token was cancelled finished, also if the lease ends right after: running it again would repeat it.
                    // Work that returned after may have stopped early because of the token, so a loss makes it run again.
                    done = !stop.Token.IsCancellationRequested;
                }
                catch (Exception exception) when (cancellationToken.IsCancellationRequested || lease.Ended.IsCancellationRequested)
                {
                    // The work stopped because the caller cancelled or the lease was lost, by the token or by a failed write. Either way what it threw is
                    // not the answer: a cancellation throws OperationCanceledException below, and a loss contends again. It is logged at Debug.
                    var errorType = CoordinationTelemetry.ErrorTypeOf(exception);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        CoordinationLog.WorkFailedAfterCancel(locks.Logger, lease.Name, locks.Table.Name, errorType);
                    }
                    else
                    {
                        CoordinationLog.WorkFailedAfterLoss(locks.Logger, lease.Name, locks.Table.Name, errorType);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            returned = true;

            // Work that did not finish here saw its token cancelled, or threw after, and the caller did not cancel: the lease was lost.
            return !done;
        }
        finally
        {
            var end = EndOf(lease, returned, done, cancellationToken);
            CoordinationLog.TermEnded(locks.Logger, lease.Name, locks.Table.Name, end, locks.Options.HolderLabel);
        }
    }

    private static string EndOf(Lease lease, bool returned, bool done, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return TermEnd.Cancelled;
        }

        if (!done && lease.Ended.IsCancellationRequested)
        {
            return TermEnd.LeaseLost;
        }

        return returned ? TermEnd.WorkReturned : TermEnd.WorkFailed;
    }

    /// <summary>Why a term of a leader ended, as the log says it.</summary>
    internal static class TermEnd
    {
        public const string Cancelled = "cancelled";
        public const string LeaseLost = "lease_lost";
        public const string WorkReturned = "work_returned";
        public const string WorkFailed = "work_failed";
    }

    /// <summary>
    /// The failures of acquires in a row, which a long outage of DynamoDB makes one every third of a lease. The first one logs a warning (44) and the next
    /// answer an information (49), as the outage of a shard of a stream does, and the metric counts every one. An answer is a try that DynamoDB answered
    /// with the lock free or held, so a follower ends its outage at its next poll, not when it next leads.
    /// </summary>
    /// <param name="locks">The table of the lock.</param>
    /// <param name="name">The name of the lock.</param>
    private sealed class Outage(LockTable locks, string name)
    {
        private long _started;
        private int _retries;

        public void Fail(Exception exception, TimeSpan delay)
        {
            var errorType = CoordinationTelemetry.ErrorTypeOf(exception);
            CoordinationTelemetry.ContentionRetry(locks.Table.Name, errorType);
            if (_retries++ == 0)
            {
                _started = locks.Time.GetTimestamp();
                CoordinationLog.ContendFailed(locks.Logger, name, locks.Table.Name, errorType, delay);
            }
        }

        public void End()
        {
            if (_retries > 0)
            {
                var elapsed = locks.Time.GetElapsedTime(_started);
                CoordinationLog.ContendRecovered(locks.Logger, name, locks.Table.Name, elapsed, _retries);
                _retries = 0;
            }
        }
    }
}
