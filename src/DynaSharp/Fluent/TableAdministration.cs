using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Fluent;

/// <summary>The calls about a table that a user of a table makes, without going to the request API. They run the operations of that API, so they have its spans and metrics.</summary>
internal static class TableAdministration
{
    /// <summary>How long a wait lasts unless a caller says otherwise. A table turns active within minutes, and only the largest restores and the backfill of an index on a table with much data take longer: those know what to pass.</summary>
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

    /// <summary>Polls a table on the clock of the client until it and all its global secondary indexes are active. A table reads <c>ACTIVE</c> while an index backfills, so its status alone says too little.</summary>
    /// <param name="table">The table.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    public static async ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var clock = table.Core.TimeProvider;
        var start = clock.GetTimestamp();
        var delay = FirstDelay;
        while (true)
        {
            TableStatus? status = null;
            GlobalSecondaryIndexDescription? pending = null;
            ResourceNotFoundException? missing = null;
            try
            {
                var description = await DescribeAsync(table, cancellationToken).ConfigureAwait(false);
                pending = FirstPendingIndex(description);
                if (description.TableStatus == TableStatus.Active && pending is null)
                {
                    return description;
                }

                status = description.TableStatus;
            }
            catch (ResourceNotFoundException exception)
            {
                // DescribeTable is eventually consistent, so right after CreateTable the table may not be there yet.
                missing = exception;
            }

            var left = timeout - clock.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                throw new TableNotActiveException($"The table {table.Name} and its indexes were not ACTIVE after {timeout}: {LastLook(status, pending)}.", table.Name, status, pending?.IndexName, missing);
            }

            await Task.Delay(left < ShortestDelay ? ShortestDelay : delay < left ? delay : left, clock, cancellationToken).ConfigureAwait(false);
            delay = delay * 2 < LongestDelay ? delay * 2 : LongestDelay;
        }
    }

    private static GlobalSecondaryIndexDescription? FirstPendingIndex(TableDescription description)
    {
        foreach (var index in description.GlobalSecondaryIndexes)
        {
            if (index.IndexStatus != IndexStatus.Active)
            {
                return index;
            }
        }

        return null;
    }

    private static string LastLook(TableStatus? status, GlobalSecondaryIndexDescription? pending)
    {
        if (status is not { } seen)
        {
            return "the table was not found";
        }

        return pending is null ? $"the table was {seen}" : $"the table was {seen}, and the index {pending.IndexName} was {pending.IndexStatus}";
    }

    // The service model requires no member of these responses. A description that says nothing has the status Unknown, so a wait goes on, and a caller is spared a null DynamoDB does not send in practice.
    private static TableDescription NoDescription(TableModel table) => new() { TableName = table.Name };
}
