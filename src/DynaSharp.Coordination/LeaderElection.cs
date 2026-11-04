using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Coordination;

/// <summary>Leader election on a lock: of all the processes that run it for the same lock, one at a time does the work.</summary>
public static class LeaderElection
{
    /// <summary>
    /// Contends for a lock until the caller cancels: acquires it with <see cref="LockTable.AcquireAsync"/>, runs <paramref name="work"/> while the lease is
    /// held, and when the lease is lost, waits for the work to stop and contends again. The token the work gets is cancelled when the caller cancels or
    /// the lease is lost, so work that honours it stops in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work ends in one of three ways. If the lease was lost, whatever the work returned or threw, the election contends again. If the caller cancelled,
    /// the lease is released and this throws <see cref="OperationCanceledException"/>, whatever the work returned or threw. If the work returned or threw on its own, the lease is released and this
    /// returns or throws that exception: a leader that is done hands the lock on.
    /// </para>
    /// <para>
    /// An acquire that fails with a transient error, a <see cref="DynamoDbException"/> whose <see cref="DynamoDbException.IsTransient"/> is
    /// <see langword="true"/>, a <see cref="TimeoutException"/>, an <see cref="HttpRequestException"/> or a <see cref="CredentialsUnavailableException"/>, after the retries of the client, is logged, and
    /// the election contends again after a third of the lease. Any other error ends it, <see cref="ResourceNotFoundException"/> included. An acquire whose answer came only after a whole lease starts no term, and the election contends again.
    /// </para>
    /// <para>
    /// Work that does not honour the token runs on after the lease is lost, and another process may then be leader at the same time. Writes that must not
    /// happen then take <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/>, or the <see cref="Lease.FencingToken"/> to a system that checks it.
    /// </para>
    /// </remarks>
    /// <param name="locks">The table of the lock.</param>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long a lease lasts without a renewal, from 1 second to 1 day. It is also about how long the work of a leader that crashed stays undone.</param>
    /// <param name="work">The work of the leader. It gets the lease and a token to stop at.</param>
    /// <param name="cancellationToken">Ends the election.</param>
    /// <returns>A task that completes when the work completed on its own, and fails when the work failed on its own, the caller cancelled or an acquire failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locks"/> or <paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below 1 second or above 1 day.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled, also when the work then threw something else.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock is not a lock item written by <see cref="LockTable"/>; see <see cref="LockTable.AcquireAsync"/>. Or the work threw it.</exception>
    public static async Task RunAsync(LockTable locks, string name, TimeSpan leaseDuration, Func<Lease, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locks);
        LockTable.Validate(name, leaseDuration);
        ArgumentNullException.ThrowIfNull(work);
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

    // Runs the work of one term. True if the lease was lost, so the election goes on.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Whatever work that lost its lease throws ends that term only; the loss is logged.")]
    private static async Task<bool> LeadAsync(LockTable locks, Lease lease, Func<Lease, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        CoordinationLog.Elected(locks.Logger, lease.Name, locks.Table.Name, lease.FencingToken);
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
            CoordinationLog.TermEnded(locks.Logger, lease.Name, locks.Table.Name, end);
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
