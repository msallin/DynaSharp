using System.Runtime.CompilerServices;
using DynaSharp.Mapping;
using DynaSharp.Operations;

namespace DynaSharp.Fluent;

/// <summary>The terminals of a fan-out query. The state they get is the template of the partitions and never goes out itself; each releases it once, when the read is over.</summary>
/// <typeparam name="T">How the items are read.</typeparam>
internal static class FanOutRead<T>
    where T : IDynamoDbDocument<T>
{
    /// <summary>Reads the merged items, on enumeration.</summary>
    /// <param name="template">The request every partition is a copy of.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The items.</returns>
    public static IAsyncEnumerable<T> Items(CommandState template, FanOutPlan plan, CancellationToken cancellationToken) =>
        new OnceEnumerable<T>(template, QueryOperation.Name, ReadItemsAsync(template, plan, cancellationToken));

    /// <summary>Reads one page of the merge, after a position.</summary>
    /// <param name="template">The request every partition is a copy of.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="pageSize">The items of the page.</param>
    /// <param name="after">The position.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<Page<T>> PageAsync(CommandState template, FanOutPlan plan, int pageSize, ContinuationToken after, CancellationToken cancellationToken)
    {
        var version = template.Version;
        try
        {
            using var merge = new FanOutMerge<T>(template, plan);
            merge.Open(after);

            // No partition can give more than the page, and what it read beyond the page would be read again.
            if (template.Limit == 0)
            {
                template.Limit = pageSize;
            }

            var items = new List<T>();
            while (items.Count < pageSize && await merge.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(merge.Take());
            }

            return new Page<T>(items, items.Count, merge.ScannedCount, merge.ConsumedCapacity, merge.Next());
        }
        finally
        {
            template.Release(version);
        }
    }

    /// <summary>Counts the items of all partitions without reading them.</summary>
    /// <param name="template">The request every partition is a copy of.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of items, a task that the caller awaits exactly once.</returns>
    /// <exception cref="InvalidOperationException">The read has a projection, which DynamoDB rejects together with this <c>Select</c>.</exception>
    public static ValueTask<int> CountAsync(CommandState template, FanOutPlan plan, CancellationToken cancellationToken)
    {
        PagedRead<QueryPageOperation<T>, T>.ThrowIfProjected(template);
        return SumAsync(template, plan, cancellationToken);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<int> SumAsync(CommandState template, FanOutPlan plan, CancellationToken cancellationToken)
    {
        var version = template.Version;
        try
        {
            var total = 0L;
            await BoundedReads.RunAsync(
                plan.PartitionKeys.Length,
                plan.MaxConcurrency,
                async (position, token) =>
                {
                    var count = await PagedRead<QueryPageOperation<T>, T>.CountAsync(template.StartPartition(plan.PartitionKeys[position], null), token).ConfigureAwait(false);
                    Interlocked.Add(ref total, count);
                },
                cancellationToken).ConfigureAwait(false);
            return checked((int)total);
        }
        finally
        {
            template.Release(version);
        }
    }

    private static async IAsyncEnumerable<T> ReadItemsAsync(CommandState template, FanOutPlan plan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var merge = new FanOutMerge<T>(template, plan);
        merge.Open(default);
        while (await merge.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return merge.Take();
        }
    }
}
