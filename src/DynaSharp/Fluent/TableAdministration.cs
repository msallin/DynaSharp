using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Fluent;

/// <summary>The calls about a table that a user of a table makes, without going to the request API. They run the operations of that API, so they have its spans and metrics.</summary>
internal static class TableAdministration
{
    /// <summary>How long a wait lasts unless a caller says otherwise. A table turns active within minutes, and only the largest restores take longer: those know what to pass.</summary>
    public static readonly TimeSpan DefaultActiveTimeout = TimeSpan.FromMinutes(5);

    // Tables turn active within seconds, usually. DescribeTable is a control plane call with its own, lower limits, so the poll backs off.
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(100);
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

    /// <summary>Polls a table on the clock of the client until it is active.</summary>
    /// <param name="table">The table.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TimeoutException">The table was not active in time.</exception>
    public static async ValueTask<TableDescription> WaitUntilActiveAsync(TableModel table, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        var clock = table.Core.TimeProvider;
        var start = clock.GetTimestamp();
        var delay = FirstDelay;
        while (true)
        {
            ResourceNotFoundException? missing = null;
            try
            {
                var description = await DescribeAsync(table, cancellationToken).ConfigureAwait(false);
                if (description.TableStatus == TableStatus.Active)
                {
                    return description;
                }
            }
            catch (ResourceNotFoundException exception)
            {
                // DescribeTable is eventually consistent, so right after CreateTable the table may not be there yet.
                missing = exception;
            }

            var left = timeout - clock.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                throw new TimeoutException($"The table {table.Name} was not ACTIVE after {timeout}.", missing);
            }

            await Task.Delay(left < ShortestDelay ? ShortestDelay : delay < left ? delay : left, clock, cancellationToken).ConfigureAwait(false);
            delay = delay * 2 < LongestDelay ? delay * 2 : LongestDelay;
        }
    }

    // The service model requires no member of these responses. A description that says nothing has the status Unknown, so a wait goes on, and a caller is spared a null DynamoDB does not send in practice.
    private static TableDescription NoDescription(TableModel table) => new() { TableName = table.Name };
}
