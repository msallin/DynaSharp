using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.Expr;

namespace DynaSharp.Coordination;

/// <summary>
/// Named locks in a DynamoDB table, each held through a <see cref="Lease"/> with a fencing token. The semantics are those of the DynamoDB Lock Client:
/// a lease has a relative duration and a record version number, its holder renews it, and a contender takes it over only after it has seen the same
/// record version for a whole lease on its own monotonic clock. No two clocks are ever compared, so the clocks of the machines may differ by any amount.
/// </summary>
/// <remarks>
/// <para>
/// A lock is one item in the table the caller passes, so it fits a single-table design. Its partition key is <c>LOCK#</c> and the name of the lock,
/// its sort key, if the table has one, is <c>LOCK</c>, and both key attributes must be strings. Its attributes are <c>_type</c> (<c>LOCK</c>),
/// <c>owner</c>, <c>rvn</c>, <c>fence</c> and <c>leaseMs</c>; the names are fixed. A release keeps the item, so that the fencing token never goes back:
/// the item has no time to live, and deleting it starts the token at 1 again, which a system that checks the token refuses.
/// </para>
/// <para>
/// It uses the <see cref="DynamoDbClientOptions.TimeProvider"/> of the client of the table for every wait and for the monotonic clock, and its
/// <see cref="DynamoDbClientOptions.LoggerFactory"/> for the log category <c>DynaSharp.Coordination.LockTable</c>. The requests go through the client
/// with its retries. It is thread-safe; hold one per table, because it remembers which holders it has seen, which a wait for a lock depends on.
/// </para>
/// </remarks>
public sealed class LockTable
{
    // Below a second a lease cannot outlast the round trips and retries of its renewals, and the heartbeat would renew it in a busy loop.
    private static readonly TimeSpan MinLeaseDuration = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<string, Sighting> _sightings = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="LockTable"/> class over a table without a schema.</summary>
    /// <param name="table">The table, opened with <see cref="DynamoDbClient.Table(string, string, string?)"/>. Its key attributes must be strings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public LockTable(Table<Item> table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        var options = table.Model.Core.Options;
        Time = options.TimeProvider;
        Logger = options.LoggerFactory?.CreateLogger<LockTable>() ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>Gets the table that holds the locks.</summary>
    public Table<Item> Table { get; }

    /// <summary>Gets the clock of the client: waits and the monotonic clock.</summary>
    internal TimeProvider Time { get; }

    /// <summary>Gets the logger, which logs nothing without a logger factory in the options of the client.</summary>
    internal ILogger Logger { get; }

    /// <summary>
    /// Tries to acquire a lock once, without waiting. It succeeds if the lock is free, released, or held by a lease that this <see cref="LockTable"/>
    /// has seen unchanged for that lease's whole duration, which means its holder stopped renewing it; then it takes the lock over.
    /// </summary>
    /// <remarks>
    /// A call that finds the lock held remembers the record version of the holder and the time on the monotonic clock, so a later call takes the lock
    /// over once the holder's lease has run out since then without a renewal. Polling this is how a contender waits; <see cref="AcquireAsync"/> does the polling.
    /// The acquire is one conditional <c>UpdateItem</c> that raises the fencing token with <c>ADD</c>, and a strongly consistent <c>GetItem</c> if the lock is held.
    /// </remarks>
    /// <param name="name">The name of the lock. Every process that names it contends for the same item.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from 1 second to 1 day. The lease renews itself every third of it.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The lease, or <see langword="null"/> if another holder has the lock.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below 1 second or above 1 day.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed the request, after the retries of the client.</exception>
    public async Task<Lease?> TryAcquireAsync(string name, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration);
        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.TryAcquireSpan, Table.Name, name);
        try
        {
            var attempt = await AttemptAsync(name, leaseDuration, cancellationToken).ConfigureAwait(false);
            CoordinationTelemetry.Acquire(activity, attempt.Lease, attempt.TakeOver);
            return attempt.Lease;
        }
        catch (Exception exception)
        {
            CoordinationTelemetry.Fail(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Acquires a lock, waiting as long as it takes. While another holder has the lock it polls every third of that holder's lease, and takes the lock
    /// over once the holder has not renewed it for a whole lease, measured on the monotonic clock of the client from the first time this saw that record version.
    /// </summary>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from 1 second to 1 day.</param>
    /// <param name="cancellationToken">Ends the wait with an <see cref="OperationCanceledException"/>.</param>
    /// <returns>The lease.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below 1 second or above 1 day.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed a request, after the retries of the client.</exception>
    public async Task<Lease> AcquireAsync(string name, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration);
        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.AcquireSpan, Table.Name, name);
        try
        {
            while (true)
            {
                var attempt = await AttemptAsync(name, leaseDuration, cancellationToken).ConfigureAwait(false);
                if (attempt.Lease is { } lease)
                {
                    CoordinationTelemetry.Acquire(activity, lease, attempt.TakeOver);
                    return lease;
                }

                await Task.Delay(attempt.Wait, Time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            CoordinationTelemetry.Fail(activity, exception);
            throw;
        }
    }

    /// <summary>Checks the arguments of an acquire.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">The lease duration.</param>
    internal static void Validate(string name, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(leaseDuration, MinLeaseDuration);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(leaseDuration, MaxLeaseDuration);
    }

    private async Task<Attempt> AttemptAsync(string name, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var partitionKey = LockItem.PartitionKey(name);
        var sortKey = LockItem.SortKeyOf(Table);
        var seen = _sightings.TryGetValue(name, out var sighting) && Time.GetElapsedTime(sighting.Timestamp) >= sighting.LeaseDuration ? sighting : null;
        var owner = LockItem.NewId();
        var sent = Time.GetTimestamp();
        try
        {
            var update = Table.Update(partitionKey, sortKey)
                .Set(LockItem.Type, LockItem.Discriminator)
                .Set(LockItem.Owner, owner)
                .Set(LockItem.RecordVersion, LockItem.NewId())
                .Set(LockItem.LeaseDuration, LockItem.Milliseconds(leaseDuration))
                .Add(LockItem.FencingToken, 1L);

            // A free lock has no owner. A holder that did not renew for a whole lease still has the record version that was seen then.
            update = seen is null
                ? update.If(Attr(LockItem.Owner).NotExists())
                : update.If(Attr(LockItem.Owner).NotExists().Or(Attr(LockItem.RecordVersion).Eq(seen.RecordVersion)));
            var fencingToken = await update.Return(Attr<long>(LockItem.FencingToken)).SendAsync(cancellationToken).ConfigureAwait(false);
            return Acquired(name, owner, fencingToken, leaseDuration, sent, seen);
        }
        catch (ConditionalCheckFailedException)
        {
            var holder = await Table.Get(partitionKey, sortKey).Consistent().ItemAsync(cancellationToken).ConfigureAwait(false);

            // The client retries a request whose response was lost, and the retry fails the condition that its first attempt met: the lock is ours.
            return holder?.GetString(LockItem.Owner) == owner
                ? Acquired(name, owner, holder[LockItem.FencingToken].AsNumber<long>(), leaseDuration, sent, seen)
                : new(null, false, Observe(name, holder));
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The lease goes to the caller, who disposes it.")]
    private Attempt Acquired(string name, string owner, long fencingToken, TimeSpan leaseDuration, long sent, Sighting? seen)
    {
        _sightings.TryRemove(name, out _);
        if (seen is not null)
        {
            CoordinationLog.TakenOver(Logger, name, Table.Name, seen.LeaseDuration);
        }

        return new(new Lease(this, name, owner, fencingToken, leaseDuration, sent), seen is not null, default);
    }

    // Remembers the holder the lock has now, and says how long to wait before the next try: at once if it is free, else a third of the holder's lease,
    // or less if the lease runs out before.
    private TimeSpan Observe(string name, Item? holder)
    {
        if (holder?.GetString(LockItem.Owner) is null)
        {
            _sightings.TryRemove(name, out _);
            return TimeSpan.Zero;
        }

        var recordVersion = holder[LockItem.RecordVersion].AsString();
        var leaseDuration = TimeSpan.FromMilliseconds(holder[LockItem.LeaseDuration].AsNumber<long>());
        var sighting = _sightings.AddOrUpdate(
            name,
            static (_, now) => now,
            static (_, before, now) => before.RecordVersion == now.RecordVersion ? before : now,
            new Sighting(recordVersion, Time.GetTimestamp(), leaseDuration));
        var left = sighting.LeaseDuration - Time.GetElapsedTime(sighting.Timestamp);
        return TimeSpan.FromTicks(Math.Clamp(left.Ticks, 0, sighting.LeaseDuration.Ticks / 3));
    }

    /// <summary>What a try gave: a lease, or how long to wait before the next try.</summary>
    private readonly record struct Attempt(Lease? Lease, bool TakeOver, TimeSpan Wait);

    /// <summary>A holder as a contender saw it first: its record version, when on the monotonic clock, and the duration of its lease.</summary>
    private sealed record Sighting(string RecordVersion, long Timestamp, TimeSpan LeaseDuration);
}
