using System.Runtime.ExceptionServices;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Fluent;

/// <summary>The calls about a table that a user of a table makes, without going to the request API. They run the operations of that API, so they have its spans and metrics.</summary>
internal static class TableAdministration
{
    /// <summary>
    /// How many looks bear with a description that does not show yet what a call just did, because <c>DescribeTable</c> is eventually consistent. With the waits of <see cref="FirstDelay"/> in between, doubling,
    /// they span 1.5 seconds.
    /// </summary>
    public const int CatchUpLooks = 5;

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

    /// <summary>Waits for a table as its entity declares it. See <see cref="WaitUntilActiveAsync(TableModel, IReadOnlyList{string}, TimeSpan, bool, CancellationToken)"/>.</summary>
    /// <param name="table">The table.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    public static ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, TimeSpan timeout, CancellationToken cancellationToken) =>
        WaitUntilActiveAsync(table, GlobalIndexNames(table.Entity?.Indexes ?? []), timeout, unlistedIsFinal: false, cancellationToken);

    /// <summary>
    /// Polls a table on the clock of the client until it and all its global secondary indexes are active. A table reads <c>ACTIVE</c> while an index backfills, so its status alone says too little, and
    /// neither does the list of a description, which may not hold an index yet that <c>UpdateTable</c> just added. So the indexes the entities declare have to be in it.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="declared">The global secondary indexes that have to be listed.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="unlistedIsFinal">
    /// <see langword="true"/> if every <c>UpdateTable</c> that adds a declared index was sent before the wait, so that an index the table has not listed at <see cref="CatchUpLooks"/> looks in a row will not turn up,
    /// and the wait ends there instead of at the timeout. A caller that cannot know, such as the wait of a <see cref="Table{T}"/>, passes <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time, or, with <paramref name="unlistedIsFinal"/>, a declared index was not listed.</exception>
    public static async ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, IReadOnlyList<string> declared, TimeSpan timeout, bool unlistedIsFinal, CancellationToken cancellationToken)
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
                unlisted = unlistedIsFinal ? FindUnlisted(description, declared) : null;
                unlistedLooks = unlisted is null ? 0 : unlistedLooks + 1;
            }
            catch (ResourceNotFoundException exception)
            {
                // DescribeTable is eventually consistent, so right after CreateTable the table may not be there yet.
                missing = exception;
            }

            if (unlistedLooks == CatchUpLooks)
            {
                throw new TableNotActiveException(
                    $"The table {table.Name} was {status}, and the index {unlisted} was not listed at {CatchUpLooks} looks in a row. CreateTable adds no index to a table that exists: add it with UpdateTable first.",
                    table.Name,
                    status,
                    unlisted);
            }

            var left = timeout - clock.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                throw new TableNotActiveException($"The table {table.Name} and its indexes were not ACTIVE after {timeout}: {LastLook(status, pending)}.", table.Name, status, pending?.Name, missing);
            }

            await Task.Delay(left < ShortestDelay ? ShortestDelay : delay < left ? delay : left, clock, cancellationToken).ConfigureAwait(false);
            delay = delay * 2 < LongestDelay ? delay * 2 : LongestDelay;
        }
    }

    /// <summary>Makes sure that an active table has time to live on an attribute, as <see cref="CreateTableCommand.TimeToLive(string)"/> says.</summary>
    /// <param name="table">The table.</param>
    /// <param name="attributeName">The attribute.</param>
    /// <param name="cancellationToken">Cancels the calls.</param>
    /// <returns><see langword="true"/> if this call turned time to live on; <see langword="false"/> if it was on, or turning on, already, also when another instance got there first.</returns>
    /// <exception cref="ValidationException">DynamoDB refused, and that was not because another instance got there first.</exception>
    public static async ValueTask<bool> EnsureTimeToLiveAsync(TableModel table, string attributeName, CancellationToken cancellationToken)
    {
        if (await TimeToLiveIsOnAsync(table, attributeName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        ValidationException rejected;
        try
        {
            var request = new UpdateTimeToLiveRequest { TableName = table.Name, TimeToLiveSpecification = new TimeToLiveSpecification { Enabled = true, AttributeName = attributeName } };
            await table.Core.ExecuteAsync<UpdateTimeToLiveOperation, UpdateTimeToLiveRequest, UpdateTimeToLiveResponse>(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ValidationException exception)
        {
            rejected = exception;
        }

        // Outside the catch: a throw after an await in one has a branch of the compiler that nothing can take.
        if (!await TimeToLiveIsOnAsync(table, attributeName, cancellationToken).ConfigureAwait(false))
        {
            ExceptionDispatchInfo.Throw(rejected);
        }

        return false;
    }

    private static async ValueTask<bool> TimeToLiveIsOnAsync(TableModel table, string attributeName, CancellationToken cancellationToken)
    {
        var response = await table.Core
            .ExecuteAsync<DescribeTimeToLiveOperation, DescribeTimeToLiveRequest, DescribeTimeToLiveResponse>(new DescribeTimeToLiveRequest { TableName = table.Name }, cancellationToken)
            .ConfigureAwait(false);
        return response.TimeToLiveDescription is { TimeToLiveStatus: TimeToLiveStatus.Enabled or TimeToLiveStatus.Enabling } description && description.AttributeName == attributeName;
    }

    // What a response says of an index comes first, because a status says more than a name that is missing.
    private static PendingIndex? FindPending(TableDescription description, IReadOnlyList<string> declared)
    {
        foreach (var index in description.GlobalSecondaryIndexes)
        {
            if (index.IndexStatus != IndexStatus.Active)
            {
                return new PendingIndex(index.IndexName, $"was {index.IndexStatus}");
            }
        }

        return FindUnlisted(description, declared) is { } unlisted ? new PendingIndex(unlisted, "was not listed") : null;
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

    private static string LastLook(TableStatus? status, PendingIndex? pending)
    {
        if (status is not { } seen)
        {
            return "the table was not found";
        }

        return pending is { } index ? $"the table was {seen}, and the index {index.Name} {index.State}" : $"the table was {seen}";
    }

    // The service model requires no member of these responses. A description that says nothing has the status Unknown, so a wait goes on, and a caller is spared a null DynamoDB does not send in practice.
    private static TableDescription NoDescription(TableModel table) => new() { TableName = table.Name };

    /// <summary>The index that holds a wait up.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="State">How the last look found it, as a message says it.</param>
    private readonly record struct PendingIndex(string? Name, string State);
}
