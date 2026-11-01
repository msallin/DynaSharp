using System.Runtime.CompilerServices;
using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// The terminals of <c>Query</c> and <c>Scan</c>: each follows pages its own way, and all release the state once, when the read is over. The caller entered
/// the state and from here on the read owns it.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="T">How the items are read.</typeparam>
internal static class PagedRead<TOperation, T>
    where TOperation : ICommandOperation<PageData<T>>
    where T : IDynamoDbDocument<T>
{
    /// <summary>Sends the next page. The key of the page before is in the state, and the key of this one lands there.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, a task that the caller awaits exactly once.</returns>
    public static ValueTask<PageData<T>> SendAsync(CommandState state, CancellationToken cancellationToken) =>
        state.Table.Core.SendAsync<PageCall<TOperation, T>, PageData<T>>(new PageCall<TOperation, T>(state), cancellationToken);

    /// <summary>Reads all items of all pages, on enumeration.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The items.</returns>
    public static IAsyncEnumerable<T> Items(CommandState state, CancellationToken cancellationToken) =>
        new OnceEnumerable<T>(state, TOperation.Name, ReadItemsAsync(state, cancellationToken));

    /// <summary>Reads all pages, on enumeration.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pages.</returns>
    public static IAsyncEnumerable<Page<T>> Pages(CommandState state, CancellationToken cancellationToken) =>
        new OnceEnumerable<Page<T>>(state, TOperation.Name, ReadPagesAsync(state, cancellationToken));

    /// <summary>Reads the page after a position.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="after">The position.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<Page<T>> PageAsync(CommandState state, ContinuationToken after, CancellationToken cancellationToken)
    {
        var version = state.Version;
        try
        {
            after.CopyKeyTo(state.PageKey);
            return ToPage(await SendAsync(state, cancellationToken).ConfigureAwait(false), state);
        }
        finally
        {
            state.Release(version);
        }
    }

    /// <summary>Reads pages up to the first that holds an item, which makes a page a filter left empty no reason to stop, and the next page no reason to go on.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The first item, or the default value if there is none, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<T?> FirstAsync(CommandState state, CancellationToken cancellationToken)
    {
        var version = state.Version;
        try
        {
            PageData<T> page;
            do
            {
                page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
                if (page.Items.Length > 0)
                {
                    return page.Items[0];
                }
            }
            while (page.HasMore);

            return default;
        }
        finally
        {
            state.Release(version);
        }
    }

    /// <summary>Counts the items of all pages without reading them.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of items, a task that the caller awaits exactly once.</returns>
    /// <exception cref="InvalidOperationException">The read has a projection, which DynamoDB rejects together with this <c>Select</c>.</exception>
    public static ValueTask<int> CountAsync(CommandState state, CancellationToken cancellationToken)
    {
        ThrowIfProjected(state);
        state.Select = Select.Count;
        return SumAsync(state, cancellationToken);
    }

    /// <summary>Stops a count with a projection, and gives its state back.</summary>
    /// <param name="state">The state of the read.</param>
    /// <exception cref="InvalidOperationException">The read has a projection, which DynamoDB rejects together with <c>Select: COUNT</c>.</exception>
    public static void ThrowIfProjected(CommandState state)
    {
        if (state.Expressions[Clause.Projection].WrittenCount > 0)
        {
            var operation = TOperation.Name;
            var table = state.Table.Name;
            state.Release(state.Version);
            throw new InvalidOperationException($"This {operation} on {table} counts items and reads none, so it has no use for Project.");
        }
    }

    // An overflow throws: a count that wraps around would look like an answer.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<int> SumAsync(CommandState state, CancellationToken cancellationToken)
    {
        var version = state.Version;
        try
        {
            var total = 0;
            PageData<T> page;
            do
            {
                page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
                total = checked(total + page.Count);
            }
            while (page.HasMore);

            return total;
        }
        finally
        {
            state.Release(version);
        }
    }

    private static async IAsyncEnumerable<T> ReadItemsAsync(CommandState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PageData<T> page;
        do
        {
            page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                yield return item;
            }
        }
        while (page.HasMore);
    }

    private static async IAsyncEnumerable<Page<T>> ReadPagesAsync(CommandState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PageData<T> page;
        do
        {
            page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            yield return ToPage(page, state);
        }
        while (page.HasMore);
    }

    private static Page<T> ToPage(PageData<T> page, CommandState state) => new(
        page.Items,
        page.Count,
        page.ScannedCount,
        state.ResponseCapacity,
        page.HasMore ? ContinuationToken.FromRawKey(state.PageKey.WrittenSpan) : default);
}
