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
        while (true)
        {
            Lease lease;
            try
            {
                lease = await locks.AcquireAsync(name, leaseDuration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransient(exception))
            {
                var delay = leaseDuration / 3;
                CoordinationLog.ContendFailed(locks.Logger, name, locks.Table.Name, CoordinationTelemetry.ErrorTypeOf(exception), delay);
                await Task.Delay(delay, locks.Time, cancellationToken).ConfigureAwait(false);
                continue;
            }

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
        try
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Ended))
            {
                try
                {
                    await work(lease, stop.Token).ConfigureAwait(false);
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
            return lease.Ended.IsCancellationRequested;
        }
        finally
        {
            var end = EndOf(lease, returned, cancellationToken);
            CoordinationLog.TermEnded(locks.Logger, lease.Name, locks.Table.Name, end, locks.Options.HolderLabel);
        }
    }

    private static string EndOf(Lease lease, bool returned, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return TermEnd.Cancelled;
        }

        if (lease.Ended.IsCancellationRequested)
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
}
