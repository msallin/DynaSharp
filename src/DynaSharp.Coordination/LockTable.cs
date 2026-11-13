using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.Expr;

namespace DynaSharp.Coordination;

/// <summary>
/// Named locks in a DynamoDB table, each held through a <see cref="Lease"/> with a fencing token: <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/> and <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/> take one,
/// <see cref="RunAsLeaderAsync"/> elects a leader.
/// </summary>
/// <example>
/// <code>
/// var locks = new LockTable(client.Table("Shop", "pk", "sk"));
///
/// var lease = await locks.TryAcquireAsync("cache-refresh", TimeSpan.FromSeconds(30), ct);
/// if (lease is null)
/// {
///     return; // Another process holds it; this one tries again on its next round.
/// }
///
/// await using (lease)
/// {
///     await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(ct);
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Semantics.</b> They are those of the DynamoDB Lock Client: a lease has a relative duration and a record version number, its holder renews it, and a contender takes it over only
/// after it has seen the same record version for a whole lease on its own monotonic clock. No two clocks are ever compared, so the clocks of the machines may be set apart by any
/// offset; what it assumes is that they run at about the same rate. A host that is suspended, or a virtual machine that freezes, can still hold a lease that has ended without
/// knowing it, which is what <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/> and <see cref="Lease.FencingToken"/> are for.
/// </para>
/// <para>
/// <b>The item.</b> A lock is one item in the table the caller passes, so it fits a single-table design. Its partition key is <c>LOCK#</c> and the name of the lock,
/// its sort key, if the table has one, is <c>LOCK</c>, and both key attributes must be strings. Its attributes are <c>_type</c> (<c>LOCK</c>),
/// <c>owner</c>, <c>rvn</c>, <c>fence</c> and <c>leaseMs</c>, with <c>holder</c> and a time to live attribute if the <see cref="LockTableOptions"/> ask for
/// them; the names are fixed. An item at that key with an <c>owner</c> whose attributes are not those,
/// such as an entity whose key happens to match, makes an acquire throw <see cref="InvalidOperationException"/> naming the lock and the table, rather
/// than guess how long it is held. A release keeps the item, so that the fencing token never goes back: without
/// <see cref="LockTableOptions.TimeToLiveAttribute"/> the item has no time to live, and deleting it starts the token at 1 again, which a system that checks the token refuses.
/// </para>
/// <para>
/// <b>Clock, logs and retries.</b> It uses the <see cref="DynamoDbClientOptions.TimeProvider"/> of the client of the table for every wait and for the monotonic clock, and its
/// <see cref="DynamoDbClientOptions.LoggerFactory"/> for the log category <c>DynaSharp.Coordination.LockTable</c>. The requests go through the client
/// with its retries. It is thread-safe; hold one per table, because it remembers which holders it has seen, which a takeover depends on.
/// </para>
/// <para>
/// <b>One region.</b> The lock table must have a single region that writes. A global table that takes writes in more than one region can grant a lock twice, with the same
/// fencing token, because each region applies its conditional write before the other one's arrives.
/// </para>
/// </remarks>
public sealed class LockTable
{
    /// <summary>
    /// The partition key of a table that holds only locks, as <see cref="Open"/> opens it and <see cref="CreateTable"/> creates it: a string, without a sort key. A test of a fake creates that table with
    /// <c>fake.CreateTable(name, LockTable.PartitionKeyName)</c>.
    /// </summary>
    public const string PartitionKeyName = "pk";

    /// <summary>The shortest lease: below a second a lease cannot outlast the round trips and retries of its renewals.</summary>
    public static readonly TimeSpan MinLeaseDuration = TimeSpan.FromSeconds(1);

    /// <summary>The longest lease, a day.</summary>
    public static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromDays(1);

    // A sighting older than this many of its leases counts as none and is dropped; see Sighting.IsStale.
    private const int SightingLifetimeInLeases = 10;

    // A contender polls a held lock at least this often, so a lock with a long lease that is released is not left free for hours.
    private static readonly TimeSpan MaxPoll = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Sighting> _sightings = new(StringComparer.Ordinal);

    // The waits of LockTableTesting.WaitForRefusalAsync, by lock name; empty outside tests, which costs a try one check.
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _refusals = new(StringComparer.Ordinal);

    // When the next sweep of stale sightings is due, on the monotonic clock; a sweep runs at most once every MaxPoll.
    private long _nextSweep;

    /// <summary>Initializes a new instance of the <see cref="LockTable"/> class over a table without a model.</summary>
    /// <param name="table">The table, opened with <see cref="DynamoDbClient.Table(string, string, string?)"/>. Its key attributes must be strings.</param>
    /// <param name="options">What the items of the locks carry beyond the lock, or <see langword="null"/> for nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The <paramref name="options"/> are not valid: see <see cref="LockTableOptions"/>.</exception>
    public LockTable(Table<Item> table, LockTableOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        Options = Validate(options ?? new LockTableOptions(), table);
        var clientOptions = table.Client.Options;
        Time = clientOptions.TimeProvider;
        Logger = clientOptions.LoggerFactory?.CreateLogger<LockTable>() ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>Gets the table that holds the locks, without a model.</summary>
    public Table<Item> Table { get; }

    /// <summary>Gets what the items of the locks carry beyond the lock.</summary>
    public LockTableOptions Options { get; }

    /// <summary>Gets the clock of the client: waits and the monotonic clock.</summary>
    internal TimeProvider Time { get; }

    /// <summary>Gets the logger, which logs nothing without a logger factory in the options of the client.</summary>
    internal ILogger Logger { get; }

    /// <summary>Gets how many locks this table holds a sighting of, for the tests of how long it keeps them.</summary>
    internal int SightingCount => _sightings.Count;

    /// <summary>
    /// Makes a lock table over any table, typed or not, for <c>LockTable.For(client.Table&lt;Order&gt;("Shop"))</c>: it needs only the name of the table,
    /// the names of its keys and its client, so the locks share a table with entities without restating its keys.
    /// </summary>
    /// <typeparam name="T">How the table reads and writes items. The locks do not use it.</typeparam>
    /// <param name="table">The table. Its key attributes must be strings.</param>
    /// <param name="options">What the items of the locks carry beyond the lock, or <see langword="null"/> for nothing.</param>
    /// <returns>The lock table, over the same table opened without a model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The <paramref name="options"/> are not valid: see <see cref="LockTableOptions"/>.</exception>
    public static LockTable For<T>(Table<T> table, LockTableOptions? options = null)
        where T : DynaSharp.Mapping.IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        return new(table as Table<Item> ?? table.Client.Table(table.Name, table.PartitionKeyName, table.SortKeyName), options);
    }

    /// <summary>
    /// Opens a table that holds only locks, whose key is the string <see cref="PartitionKeyName"/> without a sort key: <c>LockTable.Open(client, "Locks")</c>. <see cref="CreateTable"/> creates it.
    /// </summary>
    /// <example>
    /// <code>
    /// var locks = LockTable.Open(client, "Locks");
    /// await locks.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct);
    ///
    /// await using var lease = await locks.AcquireAsync("order-sweeper", TimeSpan.FromSeconds(30), ct);
    /// </code>
    /// </example>
    /// <param name="client">The client of the table.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="options">What the items of the locks carry beyond the lock, or <see langword="null"/> for nothing.</param>
    /// <returns>The lock table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is <see langword="null"/> or empty, or the <paramref name="options"/> are not valid: see <see cref="LockTableOptions"/>.</exception>
    public static LockTable Open(DynamoDbClient client, string tableName, LockTableOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new(client.Table(tableName, PartitionKeyName), options);
    }

    /// <summary>
    /// Starts the creation of the table of the locks, with the keys it was opened with as strings, on demand, and time to live on <see cref="LockTableOptions.TimeToLiveAttribute"/> if the options name one.
    /// Send it once, at deployment or start-up: <c>await locks.CreateTable().IfNotExists().WaitUntilActive().SendAsync(ct)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A table of its own.</b> It is meant for a table that <see cref="Open"/> opened. A table shared with entities, of <see cref="For{T}"/>, is created from its entities with
    /// <c>Table&lt;T&gt;.CreateTable()</c>, which knows their indexes; this one creates its keys alone.
    /// </para>
    /// </remarks>
    /// <returns>The command, which sends nothing until it is sent.</returns>
    public CreateTableCommand CreateTable() =>
        Options.TimeToLiveAttribute is { } attribute ? Table.CreateTable().TimeToLive(attribute) : Table.CreateTable();

    /// <summary>
    /// Tries to acquire a lock once, without waiting: <see langword="null"/> if another holder has it. It takes over a lock whose holder stopped renewing it, which this
    /// <see cref="LockTable"/> knows only after watching it for a whole lease.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Takeover.</b> It succeeds if the lock is free, released, or held by a lease that this <see cref="LockTable"/> has seen unchanged for that lease's whole duration. A call that finds the lock held remembers the record version of the holder and the time on the monotonic clock, so a later call of the same
    /// <see cref="LockTable"/> takes the lock over once the holder's lease has run out since then without a renewal. Polling this is how a contender waits;
    /// <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/> does the polling.
    /// </para>
    /// <para>
    /// <b>Short-lived processes.</b> A takeover needs a <see cref="LockTable"/> that lives longer than a lease. A process that lives shorter, such as a scheduled job, a function or a
    /// command line tool, never takes over a lock whose holder crashed with this, whatever it waits between its runs: it calls <see cref="TryAcquireAsync(string, TimeSpan, TimeSpan, CancellationToken)"/>
    /// with a wait of a little more than one lease, which waits out a crashed holder and gives up on a live one. So does a process that tries less often than every ten leases: a <see cref="LockTable"/> forgets a
    /// holder that no try has looked at for ten of its leases, so that a contender that gives up on many locks does not remember them all.
    /// </para>
    /// <para>
    /// <b>Requests.</b> A <see cref="LockTable"/> that has not seen the lock held writes at once: one conditional <c>UpdateItem</c> that raises the fencing token with
    /// <c>ADD</c>, and which returns the item that failed its condition if the lock is held. Once it has seen a holder, it reads the item with a strongly
    /// consistent <c>GetItem</c> first, and writes only if the lock is free or the holder is due for a takeover. So a contender that waits for a live holder
    /// sends reads, and a failed conditional write happens only when contenders race for a lock that is free or due.
    /// </para>
    /// <para>
    /// <b>A transaction on the item.</b> A write that meets a transaction on the lock item, such as the holder's <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/>, counts as
    /// refused, not as an error: it reads the item and returns <see langword="null"/> while the lock is held, and the <see cref="TransactionConflictException"/> does not reach the caller.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the lock. Every process that names it contends for the same item.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from <see cref="MinLeaseDuration"/> to <see cref="MaxLeaseDuration"/>. The lease renews itself every third of it.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>
    /// The lease, or <see langword="null"/> if another holder has the lock. An acquire whose answer came only after a whole lease hands out a lease that
    /// has ended already, see <see cref="Lease.Ended"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below <see cref="MinLeaseDuration"/> or above <see cref="MaxLeaseDuration"/>.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed the request, after the retries of the client.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock has an <c>owner</c> but is not a lock item written by <see cref="LockTable"/>.</exception>
    public async Task<Lease?> TryAcquireAsync(string name, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration);
        using var activity = CoordinationTelemetry.Start(CoordinationTelemetry.TryAcquireSpan, Table.Name, name);
        try
        {
            var attempt = await AttemptAsync(name, leaseDuration, new Claim(), cancellationToken).ConfigureAwait(false);
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
    /// Tries to acquire a lock, waiting at most <paramref name="wait"/>: <see langword="null"/> if it has not got it by then. A wait of a little more than one lease takes over the lock
    /// of a holder that crashed, also in a process that runs once, such as a scheduled job or a function.
    /// </summary>
    /// <example>
    /// <code>
    /// var lease = await locks.TryAcquireAsync("nightly-report", TimeSpan.FromSeconds(30), wait: TimeSpan.FromSeconds(40), ct);
    /// if (lease is null)
    /// {
    ///     return; // Another process runs the report.
    /// }
    ///
    /// await using (lease)
    /// {
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// <inheritdoc cref="AcquireAsync(string, TimeSpan, TimeSpan, CancellationToken)" path="/remarks/para"/>
    /// <para>
    /// <b>Two endings.</b> It waits as <see cref="AcquireAsync(string, TimeSpan, TimeSpan, CancellationToken)"/> does, which throws <see cref="TimeoutException"/> where this returns <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from <see cref="MinLeaseDuration"/> to <see cref="MaxLeaseDuration"/>.</param>
    /// <param name="wait">How long to wait for the lock, more than zero, or <see cref="Timeout.InfiniteTimeSpan"/> for as long as it takes.</param>
    /// <param name="cancellationToken">Ends the wait with an <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The lease, or <see langword="null"/> if the lock was not acquired when <paramref name="wait"/> was over. An acquire whose answer came only after a whole lease hands out a lease that has
    /// ended already, see <see cref="Lease.Ended"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="leaseDuration"/> is below <see cref="MinLeaseDuration"/> or above <see cref="MaxLeaseDuration"/>, or <paramref name="wait"/> is zero or negative and not <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed a request, after the retries of the client.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock has an <c>owner</c> but is not a lock item written by <see cref="LockTable"/>.</exception>
    public Task<Lease?> TryAcquireAsync(string name, TimeSpan leaseDuration, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration, wait);
        return ContendAsync(name, leaseDuration, Limit(wait), new Claim(), null, timeoutThrows: false, cancellationToken);
    }

    /// <summary>
    /// Acquires a lock, waiting as long as it takes: cancel the token to give up. It takes a lock over once its holder has not renewed it for a whole lease.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Polling.</b> While another holder has the lock it reads the item every third of that holder's lease, or every 10 seconds if that is sooner, and takes the lock over once the
    /// holder has not renewed it for a whole lease, measured on the monotonic clock of the client from the first time this saw that record version. The wait sends one strongly consistent <c>GetItem</c> per poll and no write while the holder renews; it writes when it finds the lock free or the
    /// holder due, see <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/>. A try that meets a transaction on the lock item, such as the holder's <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/>, is refused and waits as for a held lock.
    /// </para>
    /// <para>
    /// <b>Giving up.</b> A process that must not wait for a live holder takes a wait of a little more than one lease, <see cref="TryAcquireAsync(string, TimeSpan, TimeSpan, CancellationToken)"/>
    /// or <see cref="AcquireAsync(string, TimeSpan, TimeSpan, CancellationToken)"/>: that is long enough to take over the lock of a holder that crashed, which
    /// <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/> of a short-lived process cannot.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from <see cref="MinLeaseDuration"/> to <see cref="MaxLeaseDuration"/>.</param>
    /// <param name="cancellationToken">Ends the wait with an <see cref="OperationCanceledException"/>.</param>
    /// <returns>The lease. An acquire whose answer came only after a whole lease hands out a lease that has ended already, see <see cref="Lease.Ended"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below <see cref="MinLeaseDuration"/> or above <see cref="MaxLeaseDuration"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed a request, after the retries of the client.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock has an <c>owner</c> but is not a lock item written by <see cref="LockTable"/>.</exception>
    public async Task<Lease> AcquireAsync(string name, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration);
        return (await ContendAsync(name, leaseDuration, null, new Claim(), null, timeoutThrows: true, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Acquires a lock, waiting at most <paramref name="wait"/>, and throws <see cref="TimeoutException"/> naming the lock when it has not got it by then. It takes a lock over once its holder has not renewed
    /// it for a whole lease, so a wait of a little more than one lease gets the lock of a holder that crashed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wait.</b> It polls as <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/> does, on the <see cref="DynamoDbClientOptions.TimeProvider"/> of the client, and tries once more when the
    /// wait is over, so a holder whose lease runs out right then is taken over. The wait never cuts off a request in flight, only <paramref name="cancellationToken"/> does, so a write that took the lock is
    /// never left behind without a lease: the call can end later than <paramref name="wait"/> by the time of its last request.
    /// </para>
    /// <para>
    /// <b>Retrying later.</b> A contender that gives up keeps what it saw of the holder, so a later acquire of the same <see cref="LockTable"/>, such as the retry of a message, still counts the time since then
    /// towards the takeover, as long as it comes within ten leases.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal, from <see cref="MinLeaseDuration"/> to <see cref="MaxLeaseDuration"/>.</param>
    /// <param name="wait">How long to wait for the lock, more than zero, or <see cref="Timeout.InfiniteTimeSpan"/> for as long as it takes.</param>
    /// <param name="cancellationToken">Ends the wait with an <see cref="OperationCanceledException"/>.</param>
    /// <returns>The lease. An acquire whose answer came only after a whole lease hands out a lease that has ended already, see <see cref="Lease.Ended"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="leaseDuration"/> is below <see cref="MinLeaseDuration"/> or above <see cref="MaxLeaseDuration"/>, or <paramref name="wait"/> is zero or negative and not <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// The lock was not acquired when <paramref name="wait"/> was over: another holder had it, or the last try found it free and lost it to another write. The message names the lock, the table and which of the two.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB failed a request, after the retries of the client.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock has an <c>owner</c> but is not a lock item written by <see cref="LockTable"/>.</exception>
    public async Task<Lease> AcquireAsync(string name, TimeSpan leaseDuration, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration, wait);
        return (await ContendAsync(name, leaseDuration, Limit(wait), new Claim(), null, timeoutThrows: true, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Leads on a lock: of all the processes that run this for the same lock, one at a time runs <paramref name="work"/>, and when its lease is lost another takes over.
    /// </summary>
    /// <example>
    /// <code>
    /// await locks.RunAsLeaderAsync("order-sweeper", TimeSpan.FromSeconds(30), async (lease, stop) =>
    /// {
    ///     while (true)
    ///     {
    ///         await client.TransactWrite().IsHeld(lease).Put(orders, order).SendAsync(stop);
    ///         await Task.Delay(TimeSpan.FromMinutes(1), stop);
    ///     }
    /// }, ct);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <b>A term.</b> It acquires the lock with <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/>, runs the work while the lease is held, and when the lease is lost, waits for the work to stop and contends again. The
    /// token the work gets is cancelled when the caller cancels or the lease is lost, so work that honours it stops in time.
    /// </para>
    /// <para>
    /// <b>How the work ends.</b> The work ends in one of three ways. If the caller cancelled, the lease is released and this throws <see cref="OperationCanceledException"/>,
    /// whatever the work returned or threw. If the work returned while its token was not cancelled, it is done, also when the lease ends a moment later: the lease is released and this
    /// returns. Otherwise, if the lease was lost, whatever the work returned or threw after its token was cancelled, this contends again, and the work runs again in the next term. If the
    /// work threw on its own, the lease is released and this throws that exception: a leader that is done hands the lock on. A service that should lead for as long as it runs calls this in a loop,
    /// as the guide shows for a <c>BackgroundService</c>.
    /// </para>
    /// <para>
    /// <b>Errors of the election.</b> An acquire that fails with a transient error, a <see cref="DynamoDbException"/> whose <see cref="DynamoDbException.IsTransient"/> is
    /// <see langword="true"/>, a <see cref="TimeoutException"/>, an <see cref="HttpRequestException"/> or a <see cref="CredentialsUnavailableException"/>, after the retries of the client, is counted, and
    /// this contends again after a third of the lease. Only the first failure of a row is logged as a warning, and the next try that DynamoDB answers as an information, so an outage
    /// of an hour logs two lines and not one per try. Any other error ends it, <see cref="ResourceNotFoundException"/> included. An acquire whose answer came only after a whole lease starts no term, and this contends again.
    /// The tries after a failure write the owner id of the first, so an acquire that DynamoDB applied but whose answer was lost is found as this process's own on the next try, with its lease
    /// counted from the first, instead of leaving the lock taken until that lease runs out.
    /// </para>
    /// <para>
    /// <b>Fencing.</b> Work that does not honour the token runs on after the lease is lost, and another process may then be leader at the same time. Writes that must not
    /// happen then take <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain, Lease)"/>, or the <see cref="Lease.FencingToken"/> to a system that checks it.
    /// </para>
    /// <para>
    /// <b><see cref="Lease.Current"/>.</b> During a term it is the lease of the term, for the work and what it starts, so code deep in the work fences its writes with
    /// <see cref="LeaseTransactExtensions.IsHeld{TChain}(TChain)"/> without the lease passed down to it.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long a lease lasts without a renewal, from <see cref="MinLeaseDuration"/> to <see cref="MaxLeaseDuration"/>. It is also about how long the work of a leader that crashed stays undone.</param>
    /// <param name="work">The work of the leader. It gets the lease and a token to stop at.</param>
    /// <param name="cancellationToken">Ends the leadership: the work is stopped and the lock released.</param>
    /// <returns>A task that completes when the work completed on its own, and fails when the work failed on its own, the caller cancelled or an acquire failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is below <see cref="MinLeaseDuration"/> or above <see cref="MaxLeaseDuration"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled, also when the work then threw something else.</exception>
    /// <exception cref="InvalidOperationException">The item at the key of the lock is not a lock item written by <see cref="LockTable"/>; see <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/>. Or the work threw it.</exception>
    public async Task RunAsLeaderAsync(string name, TimeSpan leaseDuration, Func<Lease, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        Validate(name, leaseDuration);
        ArgumentNullException.ThrowIfNull(work);
        await Leadership.RunAsync(this, name, leaseDuration, work, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Gets a task that completes the next time a try of this table finds the lock held, for <c>LockTableTesting.WaitForRefusalAsync</c>.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <returns>The task, shared by every caller that waits for the same refusal.</returns>
    internal Task NextRefusal(string name) =>
        _refusals.GetOrAdd(name, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    /// <summary>Adds what the options ask every write of a lock item to carry: the time to live, from now on the wall clock.</summary>
    /// <param name="update">The write of a lock item.</param>
    /// <returns>The write, which replaces it.</returns>
    internal UpdateCommand<Item> Stamp(UpdateCommand<Item> update) =>
        Options.TimeToLiveAttribute is { } attribute
            ? update.Set(attribute, Time.GetUtcNow().ToUnixTimeSeconds() + (long)Options.TimeToLiveRetention.TotalSeconds)
            : update;

    /// <summary>Acquires a lock as <see cref="AcquireAsync(string, TimeSpan, CancellationToken)"/> does, with checked arguments: the one wait of every acquire that waits.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <param name="leaseDuration">How long the lease lasts without a renewal.</param>
    /// <param name="wait">How long to wait, or <see langword="null"/> for as long as it takes.</param>
    /// <param name="claim">The owner id the tries write, a new one for each acquire, or the one of an earlier acquire that failed, see <see cref="Claim"/>.</param>
    /// <param name="answered">Called after each try that DynamoDB answered, with the lock free or held, or <see langword="null"/>.</param>
    /// <param name="timeoutThrows">Whether a wait that is over throws <see cref="TimeoutException"/>, in the span of an acquire, or returns <see langword="null"/>, in the span of a try.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>The lease, or <see langword="null"/> when the wait is over and <paramref name="timeoutThrows"/> is <see langword="false"/>.</returns>
    internal async Task<Lease?> ContendAsync(string name, TimeSpan leaseDuration, TimeSpan? wait, Claim claim, Action? answered, bool timeoutThrows, CancellationToken cancellationToken)
    {
        using var activity = CoordinationTelemetry.Start(timeoutThrows ? CoordinationTelemetry.AcquireSpan : CoordinationTelemetry.TryAcquireSpan, Table.Name, name);
        try
        {
            var started = Time.GetTimestamp();
            while (true)
            {
                var attempt = await AttemptAsync(name, leaseDuration, claim, cancellationToken).ConfigureAwait(false);
                answered?.Invoke();
                if (attempt.Lease is { } lease)
                {
                    CoordinationTelemetry.Acquire(activity, lease, attempt.TakeOver);
                    return lease;
                }

                var delay = attempt.Wait;
                if (wait is { } limit)
                {
                    // The last try is the one at the end of the wait, and no request is cut off by it.
                    var left = limit - Time.GetElapsedTime(started);
                    if (left <= TimeSpan.Zero)
                    {
                        if (!timeoutThrows)
                        {
                            CoordinationTelemetry.Acquire(activity, null, false);
                            return null;
                        }

                        throw new TimeoutException(attempt.Free
                            ? $"The lock {name} of table {Table.Name} was not acquired within a wait of {limit}: the last try found it free, but another write to its item came first."
                            : $"The lock {name} of table {Table.Name} was not acquired within a wait of {limit}: another holder had it at the last try.");
                    }

                    delay = delay < left ? delay : left;
                }

                await Task.Delay(delay, Time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            CoordinationTelemetry.Fail(activity, exception);
            throw;
        }
    }

    private static void Validate(string name, TimeSpan leaseDuration, TimeSpan wait)
    {
        Validate(name, leaseDuration);
        if (wait != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wait, TimeSpan.Zero);
        }
    }

    private static TimeSpan? Limit(TimeSpan wait) => wait == Timeout.InfiniteTimeSpan ? null : wait;

    private static LockTableOptions Validate(LockTableOptions options, Table<Item> table)
    {
        if (options.HolderLabel is { } label && (label.Length == 0 || label.Length > LockTableOptions.MaxHolderLabelLength))
        {
            throw new ArgumentException($"The holder label must have from 1 to {LockTableOptions.MaxHolderLabelLength} characters.", nameof(options));
        }

        if (options.TimeToLiveAttribute is { } attribute
            && (attribute.Length == 0 || attribute.AsSpan().IndexOfAny('.', '[') >= 0 || IsTaken(attribute, table)))
        {
            throw new ArgumentException(
                $"The time to live attribute \"{attribute}\" must be a top-level attribute that is neither a key of table {table.Name} nor one of the lock item.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.TimeToLiveRetention, MaxLeaseDuration, nameof(options));
        return options;
    }

    private static bool IsTaken(string attribute, Table<Item> table) =>
        attribute is LockItem.Type or LockItem.Owner or LockItem.RecordVersion or LockItem.FencingToken or LockItem.LeaseDuration or LockItem.HolderLabel
        || attribute == table.PartitionKeyName || attribute == table.SortKeyName;

    private async Task<Attempt> AttemptAsync(string name, TimeSpan leaseDuration, Claim claim, CancellationToken cancellationToken)
    {
        var partitionKey = LockItem.PartitionKey(name);
        var sortKey = LockItem.SortKeyOf(Table);
        var owner = claim.Owner;

        // A contender that has seen a holder reads before it writes, so that waiting for a live holder costs no failed conditional write.
        Sighting? due = null;
        string? previousHolder = null;
        if (_sightings.TryGetValue(name, out var sighting) && !sighting.IsStale(Time))
        {
            var item = await Table.Get(partitionKey, sortKey).Consistent().ItemAsync(cancellationToken).ConfigureAwait(false);
            if (LockItem.HolderOf(item, name, Table.Name) is { } holder)
            {
                // The write of an earlier try of this claim that failed, applied after all: the lock is this claim's.
                if (holder.Owner == owner)
                {
                    return Acquired(name, owner, holder.FencingToken, leaseDuration, claim.FirstUnanswered!.Value, null);
                }

                if (holder.RecordVersion != sighting.RecordVersion || Time.GetElapsedTime(sighting.Timestamp) < sighting.LeaseDuration)
                {
                    return Refused(name, holder);
                }

                due = sighting;
                previousHolder = LockItem.HolderLabelOf(item);
            }
        }

        var sent = Time.GetTimestamp();
        try
        {
            var update = Table.Update(partitionKey, sortKey)
                .Set(LockItem.Type, LockItem.Discriminator)
                .Set(LockItem.Owner, owner)
                .Set(LockItem.RecordVersion, LockItem.NewId())
                .Set(LockItem.LeaseDuration, LockItem.Milliseconds(leaseDuration))
                .Add(LockItem.FencingToken, 1L);
            update = Options.HolderLabel is { } label ? update.Set(LockItem.HolderLabel, label) : update.Remove(LockItem.HolderLabel);

            // A free lock has no owner. A holder that did not renew for a whole lease still has the record version that was seen then.
            update = due is null
                ? update.If(Attr(LockItem.Owner).NotExists())
                : update.If(Attr(LockItem.Owner).NotExists().Or(Attr(LockItem.RecordVersion).Eq(due.RecordVersion)));

            // The old item says what the write did: the token it raised, and whether it took the lock from a holder or found it free.
            var before = (await Stamp(update).ReturnOld().ReturnOldOnFailure().SendAsync(cancellationToken).ConfigureAwait(false)).Item;
            var takenOver = before?.ContainsKey(LockItem.Owner) == true;
            return Acquired(name, owner, LockItem.FencingTokenOf(before, name, Table.Name) + 1, leaseDuration, sent, takenOver ? new(due!, LockItem.HolderLabelOf(before)) : null);
        }
        catch (DynamoDbException exception) when (exception is ConditionalCheckFailedException or TransactionConflictException)
        {
            // The item that failed the condition comes with the answer; a service that does not send it, such as an emulator, costs a read. A transaction
            // that holds the item, such as a holder's IsHeld, is a refusal too, and the read says by whom; it changes no record version, so the watch of
            // the holder goes on as it was. The client retries a request whose response was lost, and the retry fails the condition that its first
            // attempt met, or meets a transaction: then the owner is this one, and the lock is ours, from the first write of the claim that may have been
            // applied. If that attempt was due to take over, it counts as a takeover from the holder the read before it found, since the item it wrote over is gone.
            Item? item;
            try
            {
                item = (exception as ConditionalCheckFailedException)?.Item ?? await Table.Get(partitionKey, sortKey).Consistent().ItemAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Without the read, the answer does not say whether the first attempt applied the write either.
                claim.Unanswered(sent);
                throw;
            }

            var holder = LockItem.HolderOf(item, name, Table.Name);
            return holder?.Owner == owner
                ? Acquired(name, owner, holder.FencingToken, leaseDuration, claim.FirstUnanswered ?? sent, due is null ? null : new(due, previousHolder))
                : Refused(name, holder);
        }
        catch (Exception)
        {
            // No answer says whether DynamoDB applied the write, so the claim remembers it was sent.
            claim.Unanswered(sent);
            throw;
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The lease goes to the caller, who disposes it.")]
    private Attempt Acquired(string name, string owner, long fencingToken, TimeSpan leaseDuration, long sent, TakeOver? takeOver)
    {
        _sightings.TryRemove(name, out _);
        if (takeOver is not null)
        {
            CoordinationLog.TakenOver(Logger, name, Table.Name, takeOver.From.LeaseDuration, takeOver.PreviousHolder, Options.HolderLabel);
        }

        return new(new Lease(this, name, owner, fencingToken, leaseDuration, sent), takeOver is not null, default, false);
    }

    private Attempt Refused(string name, LockItem.Holder? holder) => new(null, false, Observe(name, holder), holder is null);

    // Remembers the holder the lock has now, and says how long to wait before the next try: at once if it is free, else a third of the holder's lease,
    // or 10 seconds if that is sooner, or less if the lease runs out before.
    private TimeSpan Observe(string name, LockItem.Holder? holder)
    {
        if (holder is null)
        {
            _sightings.TryRemove(name, out _);
            return TimeSpan.Zero;
        }

        if (!_refusals.IsEmpty && _refusals.TryRemove(name, out var refusal))
        {
            refusal.TrySetResult();
        }

        var recordVersion = holder.RecordVersion;
        var leaseDuration = holder.LeaseDuration;
        var sighting = _sightings.AddOrUpdate(
            name,
            static (_, now) => now.Sighting,
            static (_, before, now) => before.RecordVersion == now.Sighting.RecordVersion && !before.IsStale(now.Time) ? before : now.Sighting,
            (Sighting: new Sighting(recordVersion, Time.GetTimestamp(), leaseDuration), Time));
        SweepSightings();
        var left = sighting.LeaseDuration - Time.GetElapsedTime(sighting.Timestamp);
        return TimeSpan.FromTicks(Math.Clamp(left.Ticks, 0, Math.Min(sighting.LeaseDuration.Ticks / 3, MaxPoll.Ticks)));
    }

    // Drops the stale sightings of every lock, so that a contender that gave up on many locks, one per job, does not keep a sighting of each for ever. At most once every
    // MaxPoll, so that a table with many contenders does not walk them all on every poll.
    private void SweepSightings()
    {
        // Two threads that both find a sweep due both sweep, which costs a walk and is otherwise harmless.
        var now = Time.GetTimestamp();
        if (now < Interlocked.Read(ref _nextSweep))
        {
            return;
        }

        Interlocked.Exchange(ref _nextSweep, now + (long)(MaxPoll.TotalSeconds * Time.TimestampFrequency));

        foreach (var entry in _sightings)
        {
            if (entry.Value.IsStale(Time))
            {
                // Only if no try replaced it meanwhile.
                _sightings.TryRemove(entry);
            }
        }
    }

    /// <summary>What a try gave: a lease, or how long to wait before the next try, and whether it found the lock free after its write lost.</summary>
    private readonly record struct Attempt(Lease? Lease, bool TakeOver, TimeSpan Wait, bool Free);

    /// <summary>A holder as a contender saw it first: its record version, when on the monotonic clock, and the duration of its lease.</summary>
    private sealed record Sighting(string RecordVersion, long Timestamp, TimeSpan LeaseDuration)
    {
        /// <summary>
        /// Gets whether the sighting is older than ten of its leases, and counts as none. A try that finds a sighting a lease old takes the lock over or replaces it with a newer one, so a sighting that old
        /// means that no try came for nine leases: its contender gave up. One that comes back after that watches the holder for a whole lease again.
        /// </summary>
        /// <param name="time">The monotonic clock.</param>
        /// <returns><see langword="true"/> if it is stale.</returns>
        public bool IsStale(TimeProvider time) => time.GetElapsedTime(Timestamp) >= LeaseDuration * SightingLifetimeInLeases;
    }

    /// <summary>A takeover: the sighting that made it due, and the label of the holder it took the lock from.</summary>
    private sealed record TakeOver(Sighting From, string? PreviousHolder);

    /// <summary>
    /// The owner id that the tries of an acquire write. A write that DynamoDB applied, but whose answer was lost after the retries of the client, leaves the lock to this
    /// owner; <see cref="Leadership"/> keeps the claim for its next try, which then finds the lock its own instead of waiting a whole lease for it. A claim that
    /// handed out a lease is not used again: a new term writes a new owner, so its fencing token is a new one.
    /// </summary>
    internal sealed class Claim
    {
        /// <summary>Gets the owner id.</summary>
        public string Owner { get; } = LockItem.NewId();

        /// <summary>
        /// Gets when the first write of the claim that went unanswered was sent, on the monotonic clock, or <see langword="null"/>: a lease found in the item counts from there, which is
        /// never later than the write that DynamoDB applied. An item that names the owner was written by one of those, since a write that was answered ended the claim with a lease.
        /// </summary>
        public long? FirstUnanswered { get; private set; }

        /// <summary>Records a write that went unanswered.</summary>
        /// <param name="sent">When it was sent.</param>
        public void Unanswered(long sent) => FirstUnanswered ??= sent;
    }
}
