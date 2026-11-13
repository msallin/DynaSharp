using System.Runtime.ExceptionServices;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>The calls about a table that a user of a table makes, without going to the request API. They run the operations of that API, so they have its spans and metrics.</summary>
internal static class TableAdministration
{
    /// <summary>
    /// How many looks bear with a description that does not show yet what a call just did, because <c>DescribeTable</c> is eventually consistent. With the waits of <see cref="FirstDelay"/> in between, doubling,
    /// they span 1.5 seconds.
    /// </summary>
    public const int CatchUpLooks = 5;

    /// <summary>The longest timeout a <see cref="CancellationTokenSource"/> takes: <see cref="uint.MaxValue"/> - 1 milliseconds.</summary>
    public const double MaxTimeoutMilliseconds = 4294967294;

    /// <summary>How long a wait lasts unless a caller says otherwise. A table turns active within minutes. An index that UpdateTable adds can take longer on any table, and the largest restores do too: those know what to pass.</summary>
    public static readonly TimeSpan DefaultActiveTimeout = TimeSpan.FromMinutes(5);

    /// <summary>The first wait between two looks at a table. Tables turn active within seconds, usually. DescribeTable is a control plane call with its own, lower limits, so a poll backs off.</summary>
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan LongestDelay = TimeSpan.FromSeconds(5);

    // Task.Delay truncates to milliseconds and returns at once below one, so this is the shortest wait that really waits. On a clock a test drives, a shorter one would never move it, and the loop would never yield.
    private static readonly TimeSpan ShortestDelay = TimeSpan.FromMilliseconds(1);

    /// <summary>Describes a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description. For a response without one, see <see cref="NoDescription"/>.</returns>
    public static async ValueTask<TableDescription> DescribeAsync(TableModel table, CancellationToken cancellationToken)
    {
        var response = await table.Core
            .ExecuteAsync<DescribeTableOperation, DescribeTableRequest, DescribeTableResponse>(new DescribeTableRequest { TableName = table.Name }, cancellationToken)
            .ConfigureAwait(false);
        return response.Table ?? NoDescription(table);
    }

    /// <summary>Describes a table within a time on the clock of the client, as <see cref="DescribeWithinAsync"/> does.</summary>
    /// <param name="table">The table.</param>
    /// <param name="timeout">The time for the call, with its retries.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description. For a response without one, see <see cref="NoDescription"/>.</returns>
    public static async ValueTask<TableDescription> DescribeAsync(TableModel table, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var response = await DescribeWithinAsync(table.Core, table.Name, timeout, cancellationToken).ConfigureAwait(false);
        return response.Table ?? NoDescription(table);
    }

    /// <summary>
    /// Sends a <c>DescribeTable</c> that the time on the clock of the client bounds, with every retry of the call, and turns its running out into a <see cref="TimeoutException"/> that names the table.
    /// A token of the caller still cancels with an <see cref="OperationCanceledException"/> of that token, also when both happen at once.
    /// </summary>
    /// <param name="core">The client.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="timeout">The time for the call, more than zero and at most <see cref="MaxTimeoutMilliseconds"/>, or <see cref="Timeout.InfiniteTimeSpan"/> for no bound.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is no bound a timer can hold.</exception>
    /// <exception cref="TimeoutException">The time ran out first.</exception>
    public static async Task<DescribeTableResponse> DescribeWithinAsync(ClientCore core, string tableName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ThrowIfNoBound(timeout, "DescribeTable");
        var request = new DescribeTableRequest { TableName = tableName };
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await core.ExecuteAsync<DescribeTableOperation, DescribeTableRequest, DescribeTableResponse>(request, cancellationToken).ConfigureAwait(false);
        }

        using var deadline = new CancellationTokenSource(timeout, core.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await core.ExecuteAsync<DescribeTableOperation, DescribeTableRequest, DescribeTableResponse>(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (deadline.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            // The call saw the linked token, and a caller tells its own cancellation by its own token, which wins when both came.
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(exception.Message, exception, cancellationToken);
            }

            throw new TimeoutException($"The DescribeTable of {tableName} did not answer within its timeout of {timeout}.", exception);
        }
    }

    /// <summary>Checks a timeout that a timer of a <see cref="CancellationTokenSource"/> runs: more than zero and at most <see cref="MaxTimeoutMilliseconds"/>, or <see cref="Timeout.InfiniteTimeSpan"/>.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <param name="operation">What it bounds, for the message.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero, negative other than infinite, or too long.</exception>
    public static void ThrowIfNoBound(TimeSpan timeout, string operation)
    {
        if (timeout != Timeout.InfiniteTimeSpan && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > MaxTimeoutMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, $"The timeout of a {operation} is more than zero and at most {MaxTimeoutMilliseconds} milliseconds, or Timeout.InfiniteTimeSpan for no bound.");
        }
    }

    /// <summary>Logs that a table that <c>CreateTable().IfNotExists()</c> found differs from what the command declares, once per command, since the program that meets it is usually not the one that can fix it.</summary>
    /// <param name="table">The table.</param>
    /// <param name="drift">The differences, at least one. They name the schema alone: tables, indexes and attributes.</param>
    public static void LogDrift(TableModel table, List<TableDrift> drift)
    {
        ClientLog.DriftFound(table.Core.Logger, table.Name, drift.Count, TableDriftException.Join(drift));
    }

    /// <summary>Deletes a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description of the table, which is going away. For a response without one, see <see cref="NoDescription"/>.</returns>
    public static async ValueTask<TableDescription> DeleteAsync(TableModel table, CancellationToken cancellationToken)
    {
        var response = await table.Core
            .ExecuteAsync<DeleteTableOperation, DeleteTableRequest, DeleteTableResponse>(new DeleteTableRequest { TableName = table.Name }, cancellationToken)
            .ConfigureAwait(false);
        return response.TableDescription ?? NoDescription(table);
    }

    /// <summary>Picks the global secondary indexes out of what entities declare. A local index exists from the creation of the table, or not at all, so nothing waits for one.</summary>
    /// <param name="indexes">The indexes.</param>
    /// <returns>The names.</returns>
    public static string[] GlobalIndexNames(IEnumerable<DynamoDbIndexInfo> indexes) => [.. indexes.Where(static index => !index.IsLocal).Select(static index => index.IndexName)];

    /// <summary>Finds out whether a look at a table already shows what a wait waits for.</summary>
    /// <param name="description">The description.</param>
    /// <param name="declared">The global secondary indexes that have to be listed.</param>
    /// <returns><see langword="true"/> if the table and all its indexes are active, and the declared ones are among them.</returns>
    public static bool IsActive(TableDescription description, IReadOnlyList<string> declared) => description.TableStatus == TableStatus.Active && FindPending(description, declared) is null;

    /// <summary>Waits for a table as its entity declares it. See <see cref="WaitUntilActiveAsync(TableModel, IReadOnlyList{string}, TimeSpan, IReadOnlyList{TableDrift}, CancellationToken)"/>.</summary>
    /// <param name="table">The table.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    public static ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, TimeSpan timeout, CancellationToken cancellationToken) =>
        WaitUntilActiveAsync(table, GlobalIndexNames(table.Entity?.Indexes ?? []), timeout, foundDrift: null, cancellationToken);

    /// <summary>
    /// Polls a table on the clock of the client until it and all its global secondary indexes are active. A table reads <c>ACTIVE</c> while an index backfills, so its status alone says too little, and
    /// neither does the list of a description, which may not hold an index yet that <c>UpdateTable</c> just added. So the indexes the entities declare have to be in it.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="declared">The global secondary indexes that have to be listed.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="foundDrift">
    /// Not <see langword="null"/> if every <c>UpdateTable</c> that adds a declared index was sent before the wait, so that an index the table has not listed at <see cref="CatchUpLooks"/> looks in a row will not turn up,
    /// and the wait ends there instead of at the timeout, with a <see cref="TableDriftException"/> that lists this drift, which the caller found before, and the index if it is not among it. A caller that cannot
    /// know, such as the wait of a <see cref="Table{T}"/>, passes <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    /// <exception cref="TableDriftException">With <paramref name="foundDrift"/>, a declared index was not listed.</exception>
    public static async ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, IReadOnlyList<string> declared, TimeSpan timeout, IReadOnlyList<TableDrift>? foundDrift, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var clock = table.Core.TimeProvider;
        var start = clock.GetTimestamp();
        var delay = FirstDelay;

        // Only looks that find the table count, and one that lists every declared index starts over, because a look can still reach a replica that lags behind.
        var unlistedLooks = 0;
        while (true)
        {
            TableStatus? status = null;
            PendingIndex? pending = null;
            string? unlisted = null;
            ResourceNotFoundException? missing = null;
            try
            {
                var description = await DescribeAsync(table, cancellationToken).ConfigureAwait(false);
                pending = FindPending(description, declared);
                if (description.TableStatus == TableStatus.Active && pending is null)
                {
                    return description;
                }

                status = description.TableStatus;
                unlisted = foundDrift is null ? null : FindUnlisted(description, declared);
                unlistedLooks = unlisted is null ? 0 : unlistedLooks + 1;
            }
            catch (ResourceNotFoundException exception)
            {
                // DescribeTable is eventually consistent, so right after CreateTable the table may not be there yet.
                missing = exception;
            }

            if (unlistedLooks == CatchUpLooks)
            {
                throw new TableDriftException(table.Name, WithUnlisted(table.Name, foundDrift!, unlisted!));
            }

            var left = timeout - clock.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                throw new TableNotActiveException(OutOfTime(table.Name, timeout, status, pending), table.Name, status, pending?.Name, missing);
            }

            await Task.Delay(left < ShortestDelay ? ShortestDelay : delay < left ? delay : left, clock, cancellationToken).ConfigureAwait(false);
            delay = delay * 2 < LongestDelay ? delay * 2 : LongestDelay;
        }
    }

    /// <summary>Makes sure that an active table has time to live on an attribute, as <see cref="CreateTableCommand.TimeToLive(string)"/> says.</summary>
    /// <param name="table">The table.</param>
    /// <param name="attributeName">The attribute.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns>
    /// Whether this call turned time to live on, which it did not if it was on, or turning on, already, also when another instance got there first; and the attribute it is on instead, if it is on for another one,
    /// which this call leaves as it is.
    /// </returns>
    /// <exception cref="ValidationException">DynamoDB refused, and that was not because another instance got there first.</exception>
    public static async ValueTask<TimeToLiveOutcome> EnsureTimeToLiveAsync(TableModel table, string attributeName, CancellationToken cancellationToken)
    {
        if (await LookAtTimeToLiveAsync(table, attributeName, cancellationToken).ConfigureAwait(false) is { } found)
        {
            return found;
        }

        ValidationException rejected;
        try
        {
            var request = new UpdateTimeToLiveRequest { TableName = table.Name, TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = attributeName } };
            await table.Core.ExecuteAsync<UpdateTimeToLiveOperation, UpdateTimeToLiveRequest, UpdateTimeToLiveResponse>(request, cancellationToken).ConfigureAwait(false);
            return new TimeToLiveOutcome(TurnedOn: true, OtherAttribute: null);
        }
        catch (ValidationException exception)
        {
            rejected = exception;
        }

        // Outside the catch: a throw after an await in one has a branch of the compiler that nothing can take.
        var again = await LookAtTimeToLiveAsync(table, attributeName, cancellationToken).ConfigureAwait(false);
        if (again is null)
        {
            ExceptionDispatchInfo.Throw(rejected);
        }

        return again.Value;
    }

    // Time to live on another attribute is left alone: turning it to this one means off, an hour, and on, which DynamoDB refuses in one step anyway.
    private static async ValueTask<TimeToLiveOutcome?> LookAtTimeToLiveAsync(TableModel table, string attributeName, CancellationToken cancellationToken)
    {
        var response = await table.Core
            .ExecuteAsync<DescribeTimeToLiveOperation, DescribeTimeToLiveRequest, DescribeTimeToLiveResponse>(new DescribeTimeToLiveRequest { TableName = table.Name }, cancellationToken)
            .ConfigureAwait(false);
        return response.TimeToLiveDescription is { TimeToLiveStatus: TimeToLiveStatus.Enabled or TimeToLiveStatus.Enabling, AttributeName: { } name }
            ? new TimeToLiveOutcome(TurnedOn: false, OtherAttribute: name == attributeName ? null : name)
            : null;
    }

    // What a response says of an index comes first, because a status says more than a name that is missing.
    private static PendingIndex? FindPending(TableDescription description, IReadOnlyList<string> declared)
    {
        foreach (var index in description.GlobalSecondaryIndexes)
        {
            if (index.IndexStatus != IndexStatus.Active)
            {
                return new PendingIndex(index.IndexName, $"was {index.IndexStatus}", Listed: true);
            }
        }

        return FindUnlisted(description, declared) is { } unlisted ? new PendingIndex(unlisted, "was not listed", Listed: false) : null;
    }

    private static string? FindUnlisted(TableDescription description, IReadOnlyList<string> declared)
    {
        foreach (var name in declared)
        {
            if (!description.GlobalSecondaryIndexes.Any(index => index.IndexName == name))
            {
                return name;
            }
        }

        return null;
    }

    // The index leads, because a table reads ACTIVE while an index holds the wait up, and a message that starts with the table reads as if the table were late.
    private static string OutOfTime(string tableName, TimeSpan timeout, TableStatus? status, PendingIndex? pending)
    {
        if (status is not { } seen)
        {
            return $"The table {tableName} was not found within {timeout}.";
        }

        if (pending is not { } index)
        {
            return $"The table {tableName} was {seen} after {timeout}, not ACTIVE.";
        }

        var next = index.Listed
            ? "DynamoDB first provisions an index that UpdateTable added, which takes minutes, and then backfills it, which takes as long as the table is large: wait again with a longer timeout."
            : "Only UpdateTable adds an index to a table that exists: if none is under way, add it.";
        return $"The index {index.Name} of the table {tableName} {index.State} after {timeout}, and the table was {seen}. {next}";
    }

    // An index the first look of IfNotExists() found missing is in the drift already. One it listed, from a replica that lags, or one of a table this call created, gets a drift of its own.
    private static List<TableDrift> WithUnlisted(string tableName, IReadOnlyList<TableDrift> foundDrift, string unlisted)
    {
        List<TableDrift> drift = [.. foundDrift];
        foreach (var found in foundDrift)
        {
            if (found.Kind == TableDriftKind.IndexMissing && found.IndexName == unlisted)
            {
                return drift;
            }
        }

        drift.Add(new TableDrift(
            TableDriftKind.IndexMissing,
            unlisted,
            $"The table {tableName} did not list the global index {unlisted} at {CatchUpLooks} looks in a row, and the command declares it. CreateTable adds no index to a table that exists: add it with UpdateTable."));
        return drift;
    }

    // The service model requires no member of these responses. A description that says nothing has the status Unknown, so a wait goes on, and a caller is spared a null DynamoDB does not send in practice.
    private static TableDescription NoDescription(TableModel table) => new() { TableName = table.Name };

    /// <summary>What <see cref="EnsureTimeToLiveAsync"/> found or did.</summary>
    /// <param name="TurnedOn">Whether its <c>UpdateTimeToLive</c> was accepted.</param>
    /// <param name="OtherAttribute">The attribute time to live is on for instead of the one asked for, or <see langword="null"/>.</param>
    public readonly record struct TimeToLiveOutcome(bool TurnedOn, string? OtherAttribute);

    /// <summary>The index that holds a wait up.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="State">How the last look found it, as a message says it.</param>
    /// <param name="Listed">Whether the table lists it, so that it only has to turn active.</param>
    private readonly record struct PendingIndex(string? Name, string State, bool Listed);
}
