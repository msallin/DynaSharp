using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using static DynaSharp.Expr;

namespace DynaSharp.Coordination;

/// <summary>
/// A held lock: the lease that <see cref="LockTable.TryAcquireAsync"/> or <see cref="LockTable.AcquireAsync"/> handed out. It renews itself every third of
/// its duration until it is disposed or lost, and <see cref="Lost"/> says when it ends. Dispose it to release the lock: <c>await using var lease = ...</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lease is held from the moment its acquire was sent, for its duration on the monotonic clock of the client, and each renewal extends it by the
/// duration from the moment that renewal was sent. A contender starts its wait only once it has read the item, after the write, so on the holder's
/// clock the lease always ends before a contender may take it over, whatever the clocks of the two machines say.
/// </para>
/// <para>
/// It is lost when a renewal finds another owner in the item (a contender took it over, or someone deleted it), when DynamoDB answers a renewal with
/// <see cref="ResourceNotFoundException"/>, when a renewal fails twice in a row with any other error after the retries of the client, and when the lease
/// runs out on the monotonic clock without a renewal that succeeded. Losing it is final: acquire the lock again for a new lease.
/// </para>
/// <para>
/// A lease guards only what checks it. Code that writes to DynamoDB adds <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/> to the
/// transaction of the write, and a system outside compares <see cref="FencingToken"/> with the highest one it has seen.
/// </para>
/// </remarks>
public sealed class Lease : IAsyncDisposable
{
    private const int Released = 1;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Lost must stay readable after the lease ended, and a source that was cancelled has no timer left to free.")]
    private readonly CancellationTokenSource _ended;
    private readonly Task _heartbeat;
    private readonly Lock _extending = new();
    private TaskCompletionSource _beat = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _renewedAt;
    private int _state;
    private string? _lossReason;

    internal Lease(LockTable locks, string name, string owner, long fencingToken, TimeSpan duration, long sent)
    {
        Locks = locks;
        Name = name;
        Owner = owner;
        FencingToken = fencingToken;
        Duration = duration;
        _renewedAt = sent;
        _ended = new CancellationTokenSource(Left(sent), locks.Time);
        Lost = _ended.Token;
        _ended.Token.UnsafeRegister(static lease => ((Lease)lease!).Lose(LossReasons.Expired), this);
        _heartbeat = HeartbeatAsync();
    }

    /// <summary>Gets the name of the lock.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the fencing token: a number that each acquire of this lock raises by one, so a later holder always has a higher one. A system that a holder
    /// writes to keeps the highest token it has seen and refuses a lower one; that stops a holder that lost its lease, but does not know yet, from
    /// overwriting the work of the next. The count starts at 1 again only for a lock whose item was deleted.
    /// </summary>
    public long FencingToken { get; }

    /// <summary>
    /// Gets a token that is cancelled once the lease is no longer held: lost, see the remarks of <see cref="Lease"/>, or released by <see cref="DisposeAsync"/>.
    /// Pass it to the work that needs the lock, so that the work stops when the lease does.
    /// </summary>
    public CancellationToken Lost { get; }

    /// <summary>Gets the lock table that handed out the lease.</summary>
    internal LockTable Locks { get; }

    /// <summary>Gets the owner, the id this acquire wrote into the item.</summary>
    internal string Owner { get; }

    /// <summary>Gets the duration of the lease.</summary>
    internal TimeSpan Duration { get; }

    /// <summary>Gets why the lease was lost, or <see langword="null"/> while it is held or once it was released.</summary>
    internal string? LossReason => Volatile.Read(ref _lossReason);

    /// <summary>Gets a task that completes after the heartbeat has renewed once more, or failed to, and has armed its next wait, or has stopped.</summary>
    internal Task NextBeat => Volatile.Read(ref _beat).Task;

    /// <summary>
    /// Renews the lease now, outside the heartbeat, which renews it every third of its duration anyway. Useful before a step that must not start on a lease
    /// that is about to run out. A renewal that fails with an error other than a failed condition is tried once more at once, and the lease is lost if that fails too.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request; the lease stays held.</param>
    /// <returns><see langword="true"/> if the lease was renewed, <see langword="false"/> if it is lost or released, also if it was before the call.</returns>
    /// <exception cref="ResourceNotFoundException">The table does not exist. The lease is lost.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed both requests, after the retries of the client. The lease is lost.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<bool> RenewAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lost);
        try
        {
            return await RenewCoreAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Lost.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the heartbeat and releases the lock if this lease still holds it: one <c>UpdateItem</c> that removes the owner, on condition that the owner is
    /// this lease, so a lock that someone else took over is left alone. The fencing token stays in the item. A release that fails is logged, not thrown,
    /// and the lock is then free once the lease runs out. <see cref="Lost"/> is cancelled. Calling it again does nothing.
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
        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.ReleaseSpan, Locks.Table.Name, Name);
        try
        {
            await Locks.Table.Update(LockItem.PartitionKey(Name), LockItem.SortKeyOf(Locks.Table))
                .Remove(LockItem.Owner)
                .Remove(LockItem.LeaseDuration)
                .Set(LockItem.RecordVersion, LockItem.NewId())
                .If(Attr(LockItem.Owner).Eq(Owner))
                .SendAsync()
                .ConfigureAwait(false);
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nobody observes the heartbeat. Every way out of it ends with the lease lost, released, or running out.")]
    private async Task HeartbeatAsync()
    {
        var period = Duration / 3;
        try
        {
            while (true)
            {
                var wait = Task.Delay(period, Locks.Time, Lost);
                Beat();
                await wait.ConfigureAwait(false);
                if (!await RenewCoreAsync(Lost).ConfigureAwait(false))
                {
                    return;
                }
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

    // A renewal that fails with an error that is neither a failed condition nor a missing table is tried once more at once.
    private async Task<bool> RenewCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RenewOnceAsync(last: false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ResourceNotFoundException))
        {
            CoordinationLog.RenewRetried(Locks.Logger, Name, Locks.Table.Name, CoordinationTelemetry.ErrorTypeOf(exception));
            return await RenewOnceAsync(last: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> RenewOnceAsync(bool last, CancellationToken cancellationToken)
    {
        if (Lost.IsCancellationRequested)
        {
            return false;
        }

        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.RenewSpan, Locks.Table.Name, Name);
        var sent = Locks.Time.GetTimestamp();
        try
        {
            await Locks.Table.Update(LockItem.PartitionKey(Name), LockItem.SortKeyOf(Locks.Table))
                .Set(LockItem.RecordVersion, LockItem.NewId())
                .If(Attr(LockItem.Owner).Eq(Owner))
                .SendAsync(cancellationToken)
                .ConfigureAwait(false);
            CoordinationTelemetry.Renew(this, null);
            return Extend(sent);
        }
        catch (ConditionalCheckFailedException exception)
        {
            Fail(activity, exception, LossReasons.TakenOver);
            return false;
        }
        catch (ResourceNotFoundException exception)
        {
            Fail(activity, exception, LossReasons.TableNotFound);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(activity, exception, last ? LossReasons.RenewFailed : null);
            throw;
        }
    }

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

        return !Lost.IsCancellationRequested;
    }

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
        public const string Expired = "expired";
        public const string RenewFailed = "renew_failed";
        public const string TableNotFound = "table_not_found";
    }
}
