using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Fluent;

/// <summary>The two calls about a table that a user of a table makes, without going to the request API.</summary>
internal static class TableAdministration
{
    // Tables turn active within seconds, usually. DescribeTable is a control plane call with its own, lower limits, so the poll backs off.
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongestDelay = TimeSpan.FromSeconds(5);

    /// <summary>Describes a table.</summary>
    /// <param name="table">The table.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description.</returns>
    public static async ValueTask<TableDescription> DescribeAsync(TableModel table, CancellationToken cancellationToken)
    {
        var response = await table.Core
            .ExecuteAsync<DescribeTableOperation, DescribeTableRequest, DescribeTableResponse>(new DescribeTableRequest { TableName = table.Name }, cancellationToken)
            .ConfigureAwait(false);
        return response.Table;
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

            await Task.Delay(delay < left ? delay : left, clock, cancellationToken).ConfigureAwait(false);
            delay = delay * 2 < LongestDelay ? delay * 2 : LongestDelay;
        }
    }
}
