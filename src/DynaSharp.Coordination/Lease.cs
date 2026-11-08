using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using static DynaSharp.Expr;

namespace DynaSharp.Coordination;

/// <summary>
/// A held lock: the lease that <see cref="LockTable.TryAcquireAsync"/> or <see cref="LockTable.AcquireAsync(string, TimeSpan, CancellationToken)"/> handed out. It renews itself every third of
/// its duration until it is disposed or lost, and <see cref="Ended"/> says when it ends. Dispose it to release the lock: <c>await using var lease = ...</c>.
/// </summary>
/// <example>
/// <code>
/// await using var lease = await locks.AcquireAsync("nightly-report", TimeSpan.FromSeconds(30), ct);
/// using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Ended);
/// await foreach (var placed in byStatus.Query(Order.Keys.ByStatusPk.Of(OrderStatus.Placed)).ItemsAsync(stop.Token))
/// {
///     await client.TransactWrite().IsHeld(lease).Put(orders, placed with { Status = OrderStatus.Shipped }).SendAsync(stop.Token);
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Timing.</b> The lease is held from the moment its acquire was sent, for its duration on the monotonic clock of the client, and each renewal extends it by the
/// duration from the moment that renewal was sent. The heartbeat sends the next renewal a third of the duration after the last one that succeeded was
/// sent, so a slow answer does not delay the one after it. A contender starts its wait only once it has read the item, after the write, so on the
/// holder's clock the lease ends before a contender may take it over, whatever offset the clocks of the two machines have. That assumes both clocks run
/// at about the same rate. A host that is suspended, or a virtual machine that freezes, stops the holder but not the contender: it may wake up after its
/// lease ended without knowing it yet. That case is what <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/> and <see cref="FencingToken"/> are for.
/// </para>
/// <para>
/// <b>Losing it.</b> It is lost when a renewal finds another owner in the item (a contender took it over, or someone wrote the item), when it finds the item deleted,
/// which starts the fencing token at 1 again for the next acquire, when DynamoDB answers a renewal with
/// <see cref="ResourceNotFoundException"/>, and when the lease runs out on the monotonic clock without a renewal that succeeded. A renewal of the heartbeat
/// that fails with any other error, after the retries of the client, is tried again with a growing wait, up to a sixth of the duration, until one succeeds
/// or the lease runs out. Losing it is final: acquire the lock again for a new lease.
/// </para>
/// <para>
/// <b>Its own transactions.</b> A renewal or release that meets a transaction on the lock item, such as one of this lease's <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/>, is
/// sent again after a short wait, up to five times, so the lease is never lost to a transaction and a release is not left to the end of the lease.
/// </para>
/// <para>
/// <b>Fencing.</b> A lease guards only what checks it. Code that writes to DynamoDB adds <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/> to the
/// transaction of the write, and a system outside compares <see cref="FencingToken"/> with the highest one it has seen.
/// </para>
/// <para>
/// A lease that is never disposed is renewed for as long as the process runs: the heartbeat keeps it alive, and with it the lock.
/// </para>
/// </remarks>
public sealed class Lease : IAsyncDisposable
{
    private const int Released = 1;

    // The first wait before a failed renewal of the heartbeat is tried again. It doubles up to a sixth of the duration.
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMilliseconds(100);

    // The lease of the term of RunAsLeaderAsync that the code runs in. An AsyncLocal flows down into what the work starts and never back up to its caller.
    private static readonly AsyncLocal<Lease?> CurrentLease = new();

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Ended must stay readable after the lease ended, and a source that was cancelled has no timer left to free.")]
    private readonly CancellationTokenSource _ended;
    private readonly Task _heartbeat;
    private readonly Lock _extending = new();
    private readonly ActivityContext _acquiredIn;
    private TaskCompletionSource _beat = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _renewedAt;
    private int _state;
    private string? _lossReason;

    [SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "The flow control only keeps the heartbeat from capturing the context of the caller; the heartbeat does not use it.")]
    internal Lease(LockTable locks, string name, string owner, long fencingToken, TimeSpan duration, long sent)
    {
        Locks = locks;
        Name = name;
        Owner = owner;
        FencingToken = fencingToken;
        Duration = duration;
        _acquiredIn = CoordinationTelemetry.ContextOf(Activity.Current);
        _renewedAt = sent;
        _ended = new CancellationTokenSource(Left(sent), locks.Time);
        Ended = _ended.Token;
        _ended.Token.UnsafeRegister(static lease => ((Lease)lease!).Lose(LossReasons.Expired), this);

        // The heartbeat outlives the call that acquired the lease, so it must not keep that call's span as its parent, nor its async locals alive, for
        // the lifetime of the lease. Its first wait is armed here, so the heartbeat is running when the acquire returns.
        using (SuppressFlow())
        {
            var first = Task.Delay(NextBeatIn(), locks.Time, Ended);
            _heartbeat = Task.Run(() => HeartbeatAsync(first));
        }
    }

    /// <summary>
    /// Gets the lease of the term of <see cref="LockTable.RunAsLeaderAsync"/> that the calling code runs in, or <see langword="null"/> outside one. It is what
    /// <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain)"/> checks, so that work deep in a call graph fences its writes without the lease passed down to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The leader sets it before it calls the work and clears it when the term ends. It flows as an <see cref="AsyncLocal{T}"/> does: into everything the work
    /// calls, awaits or starts with <see cref="Task.Run(Func{Task})"/> during the term, and never back to the caller of <see cref="LockTable.RunAsLeaderAsync"/>.
    /// There is no setter: a lease from <see cref="LockTable.TryAcquireAsync"/> or <see cref="LockTable.AcquireAsync(string, TimeSpan, CancellationToken)"/> is passed to
    /// <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/> as it is.
    /// </para>
    /// <para>
    /// Code that started before the term, in a constructor or a <c>StartAsync</c>, or that runs under <see cref="ExecutionContext.SuppressFlow"/>, sees
    /// <see langword="null"/>, and so does the heartbeat of a lease. Work that outlives its term, such as a task the work started and did not wait for, keeps the lease
    /// that has ended, and a transaction that checks it fails at DynamoDB, which is the fencing it is for. In a <see cref="LockTable.RunAsLeaderAsync"/> inside the
    /// work of another, the inner lease is current; pass the outer one explicitly.
    /// </para>
    /// </remarks>
    public static Lease? Current => CurrentLease.Value;

    /// <summary>Gets the name of the lock.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the fencing token: a number that each acquire of this lock raises by one, so a later holder always has a higher one. A system that a holder
    /// writes to keeps the highest token it has seen and refuses a lower one; that stops a holder that lost its lease, but does not know yet, from
    /// overwriting the work of the next.
    /// </summary>
    /// <remarks>
    /// The token goes back only if the item of the lock goes back: when it is deleted, which starts the count at 1 again, and when the table is restored
    /// from a backup or to a point in time, which brings an older token back. A global table that takes writes in more than one region can grant the lock
    /// twice, with the same token, because each region applies its conditional write before the other one's arrives: keep the lock table to a single
    /// region that writes.
    /// </remarks>
    public long FencingToken { get; }

    /// <summary>
    /// Gets a token that is cancelled once the lease ends: when it is lost, see the remarks of <see cref="Lease"/>, or released by <see cref="DisposeAsync"/>.
    /// Pass it to the work that needs the lock, so that the work stops when the lease does. An acquire whose answer came after a whole duration hands out
    /// a lease that has ended already.
    /// </summary>
    public CancellationToken Ended { get; }

    /// <summary>Gets the lock table that handed out the lease.</summary>
    internal LockTable Locks { get; }

    /// <summary>Gets the owner, the id this acquire wrote into the item.</summary>
    internal string Owner { get; }

    /// <summary>Gets the duration of the lease.</summary>
    internal TimeSpan Duration { get; }

    /// <summary>Gets why the lease was lost, or <see langword="null"/> while it is held or once it was released.</summary>
    internal string? LossReason => Volatile.Read(ref _lossReason);

    /// <summary>Gets a task that completes after the heartbeat has renewed once more and armed its next wait, or has stopped.</summary>
    internal Task NextBeat => Volatile.Read(ref _beat).Task;

    /// <summary>
    /// Renews the lease now, outside the heartbeat, which renews it every third of its duration anyway. Useful before a step that must not start on a lease
    /// that is about to run out. It sends one renewal, with the retries of the client, and a failure leaves the lease to the heartbeat.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request; the lease stays held.</param>
    /// <returns><see langword="true"/> if the lease was renewed, <see langword="false"/> if it has ended, also if it had before the call.</returns>
    /// <exception cref="ResourceNotFoundException">The table does not exist. The lease is lost.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed the request, after the retries of the client, with any other error. The lease is still held until it runs out, and the heartbeat goes on renewing it.</exception>
    /// <exception cref="HttpRequestException">The request did not reach DynamoDB, after the retries of the client. The lease is still held, as above.</exception>
    /// <exception cref="TimeoutException">An attempt timed out, after the retries of the client. The lease is still held, as above.</exception>
    /// <exception cref="CredentialsUnavailableException">The client has no credentials. The lease is still held, as above.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. The lease is still held.</exception>
    public async Task<bool> RenewAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Ended);
        try
        {
            return await RenewOnceAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Ended.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the heartbeat and releases the lock if this lease still holds it: one <c>UpdateItem</c> that removes the owner and the holder label, on condition that the owner is
    /// this lease, so a lock that someone else took over is left alone. The fencing token stays in the item. A release that fails is logged, not thrown,
    /// and the lock is then free once the lease runs out. <see cref="Ended"/> is cancelled. Calling it again does nothing.
    /// </summary>
    /// <returns>A task that completes when the lock is released.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A release that fails must not fail the dispose: it is logged, and the lock is free once the lease runs out.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, Released) == Released)
        {
            return;
        }

        await _ended.CancelAsync().ConfigureAwait(false);
        await _heartbeat.ConfigureAwait(false);
        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.ReleaseSpan, Locks.Table.Name, Name, _acquiredIn);
        try
        {
            await Locks.Table.RetryOnConflictAsync(SendReleaseAsync, ConflictKind.TransactionConflict, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            // Someone else holds the lock now, or nobody: either way it is not this lease's to release.
        }
        catch (Exception exception)
        {
            CoordinationTelemetry.Fail(activity, exception);
            CoordinationLog.ReleaseFailed(Locks.Logger, Name, Locks.Table.Name, CoordinationTelemetry.ErrorTypeOf(exception));
        }
    }

    /// <summary>Sets <see cref="Current"/> for the code that runs from here in this async flow, which <see cref="Leadership"/> does for a term.</summary>
    /// <param name="lease">The lease of the term, or <see langword="null"/> when it ends.</param>
    internal static void SetCurrent(Lease? lease) => CurrentLease.Value = lease;

    // Suppresses the flow of the execution context, unless the caller did already, which a second suppression would throw for.
    private static AsyncFlowControl? SuppressFlow() => ExecutionContext.IsFlowSuppressed() ? null : ExecutionContext.SuppressFlow();

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nobody observes the heartbeat. Every way out of it ends with the lease lost, released, or running out.")]
    private async Task HeartbeatAsync(Task wait)
    {
        try
        {
            while (true)
            {
                await wait.ConfigureAwait(false);
                if (!await RenewUntilEndedAsync().ConfigureAwait(false))
                {
                    return;
                }

                wait = Task.Delay(NextBeatIn(), Locks.Time, Ended);
                Beat();
            }
        }
        catch (Exception)
        {
            // Released, or lost: a renewal that failed has lost the lease and logged why. Whatever else stops the heartbeat, the lease runs out.
        }
        finally
        {
            Beat();
        }
    }

    // A renewal that fails with an error that is neither a failed condition nor a missing table is tried again, with a growing wait, until the lease ends.
    private async Task<bool> RenewUntilEndedAsync()
    {
        var retry = FirstRetry;
        while (true)
        {
            try
            {
                return await RenewOnceAsync(Ended).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or ResourceNotFoundException))
            {
                CoordinationLog.RenewRetried(Locks.Logger, Name, Locks.Table.Name, CoordinationTelemetry.ErrorTypeOf(exception), retry);
                await Task.Delay(retry, Locks.Time, Ended).ConfigureAwait(false);
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, Duration.Ticks / 6));
            }
        }
    }

    private async Task<bool> RenewOnceAsync(CancellationToken cancellationToken)
    {
        if (Ended.IsCancellationRequested)
        {
            return false;
        }

        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.RenewSpan, Locks.Table.Name, Name, _acquiredIn);
        var sent = Locks.Time.GetTimestamp();
        try
        {
            await Locks.Table.RetryOnConflictAsync(SendRenewalAsync, ConflictKind.TransactionConflict, cancellationToken: cancellationToken).ConfigureAwait(false);
            CoordinationTelemetry.Renew(this, null);
            return Extend(sent);
        }
        catch (ConditionalCheckFailedException exception)
        {
            // The item that failed the condition comes with the answer: none means it was deleted, which also starts its fencing token at 1 again.
            Fail(activity, exception, exception.Item is null ? LossReasons.Deleted : LossReasons.TakenOver);
            return false;
        }
        catch (ResourceNotFoundException exception)
        {
            Fail(activity, exception, LossReasons.TableNotFound);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(activity, exception, null);
            throw;
        }
    }

    // A transaction on the item, such as this lease's own IsHeld, is over within moments and leaves the lock as it was, so the write is sent again after a short wait.
    // A write that met a transaction was not applied, so sending it again is safe.
    private async ValueTask SendRenewalAsync(CancellationToken cancellationToken) =>
        await Locks.Stamp(Locks.Table.Update(LockItem.PartitionKey(Name), LockItem.SortKeyOf(Locks.Table)))
            .Set(LockItem.RecordVersion, LockItem.NewId())
            .If(Attr(LockItem.Owner).Eq(Owner))
            .ReturnOldOnFailure()
            .SendAsync(cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask SendReleaseAsync(CancellationToken cancellationToken) =>
        await Locks.Stamp(Locks.Table.Update(LockItem.PartitionKey(Name), LockItem.SortKeyOf(Locks.Table)))
            .Remove(LockItem.Owner)
            .Remove(LockItem.LeaseDuration)
            .Remove(LockItem.HolderLabel)
            .Set(LockItem.RecordVersion, LockItem.NewId())
            .If(Attr(LockItem.Owner).Eq(Owner))
            .SendAsync(cancellationToken)
            .ConfigureAwait(false);

    // Records a renewal that failed, and loses the lease for a reason.
    private void Fail(Activity? activity, Exception exception, string? reason)
    {
        CoordinationTelemetry.Fail(activity, exception);
        CoordinationTelemetry.Renew(this, CoordinationTelemetry.ErrorTypeOf(exception));
        if (reason is not null)
        {
            Lose(reason);
        }
    }

    // Moves the end of the lease to a whole duration after the renewal was sent. A renewal sent before one that already moved it, whose answer came
    // late, moves nothing: it must neither shorten the lease nor end it. A renewal whose whole duration has passed ends the lease at once.
    private bool Extend(long sent)
    {
        lock (_extending)
        {
            if (sent > _renewedAt)
            {
                _renewedAt = sent;
                _ended.CancelAfter(Left(sent));
            }
        }

        return !Ended.IsCancellationRequested;
    }

    // The next renewal is due a third of the duration after the last one that succeeded was sent, or now, if that has passed.
    private TimeSpan NextBeatIn() => TimeSpan.FromTicks(Math.Max(0, ((Duration / 3) - Locks.Time.GetElapsedTime(Volatile.Read(ref _renewedAt))).Ticks));

    private TimeSpan Left(long sent)
    {
        var left = Duration - Locks.Time.GetElapsedTime(sent);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    // The first reason wins, and a lease that was released is not lost.
    private void Lose(string reason)
    {
        if (Volatile.Read(ref _state) == Released || Interlocked.CompareExchange(ref _lossReason, reason, null) is not null)
        {
            return;
        }

        CoordinationTelemetry.Lose(this, reason);
        CoordinationLog.LeaseLost(Locks.Logger, Name, Locks.Table.Name, reason);
        _ = _ended.CancelAsync();
    }

    private void Beat() => Interlocked.Exchange(ref _beat, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    /// <summary>Why a lease was lost, as <c>dynasharp.lock.loss_reason</c> and the log say it.</summary>
    internal static class LossReasons
    {
        public const string TakenOver = "taken_over";
        public const string Deleted = "deleted";
        public const string Expired = "expired";
        public const string TableNotFound = "table_not_found";
    }
}
