using System.Runtime.CompilerServices;

namespace DynaSharp.Fluent;

/// <summary>
/// The terminals of <c>Query</c> and <c>Scan</c>: each follows pages its own way, and all release the state once, when the read is over. The caller entered
/// the state and from here on the read owns it.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="T">How the items are read.</typeparam>
internal static class PagedRead<TOperation, T>
    where TOperation : ICommandOperation<PageData<T>>
{
    /// <summary>Sends the next page. The key of the page before is in the state, and the key of this one lands there.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, a task that the caller awaits exactly once.</returns>
    public static ValueTask<PageData<T>> SendAsync(CommandState state, CancellationToken cancellationToken)
    {
        var sending = state.Table.Core.SendAsync<PageCall<TOperation, T>, PageData<T>>(new PageCall<TOperation, T>(state), cancellationToken);
        return state.Read.OnMappingFailure is { } report ? ReportAsync(sending, state, report) : sending;
    }

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

    /// <summary>Reads the page after a position; with <see cref="ReadOptions.Take"/>, as many responses as it takes to give that many items, as one page.</summary>
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
            after.ThrowIfNotFor(state, plan: null);
            after.CopyKeyTo(state.PageKey);
            var page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            return state.Read.Take == 0 ? ToPage(page, state) : await TakeAsync(page, state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            state.Release(version);
        }
    }

    /// <summary>Reads pages up to the first that holds an item, which makes a page a filter left empty no reason to stop, and the next page no reason to go on, as <see cref="FindAsync"/> does.</summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The first item, or the default value if there is none, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<T?> FirstAsync(CommandState state, CancellationToken cancellationToken)
    {
        var first = await FindAsync(state, cancellationToken).ConfigureAwait(false);
        return first.Found ? first.Value : default;
    }

    /// <summary>
    /// Reads pages up to the first that holds an item, and says whether there was one, for a type whose default value is a value. A read without a filter, a <c>Limit</c> and a <c>Take</c> sends
    /// <c>Limit: 1</c>, because its first item read is the one it gives.
    /// </summary>
    /// <param name="state">The state of the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The first item and whether there is one, with the capacity of the last page, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<SelectResult<T>> FindAsync(CommandState state, CancellationToken cancellationToken)
    {
        var version = state.Version;

        // Without a filter the first item read is the one given, so a page of one costs one item; with a filter, or a limit the caller chose, the pages stay as they are.
        if (state.Read.Limit == 0 && state.Read.Take == 0 && state.Expressions[Clause.Filter].WrittenCount == 0)
        {
            state.Read.Limit = 1;
        }

        try
        {
            PageData<T> page;
            do
            {
                page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
                if (page.Items.Length > 0)
                {
                    return new SelectResult<T>(page.Items[0], found: true, state.ResponseCapacity);
                }
            }
            while (page.HasMore);

            return new SelectResult<T>(default!, found: false, state.ResponseCapacity);
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
        state.Read.Select = SelectKind.Count;
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
                state.Read.Taken = total;
            }
            while (page.HasMore && !state.Read.IsTaken);

            return total;
        }
        finally
        {
            state.Release(version);
        }
    }

    // After the call and not in it: the callback is the caller's code, so what it throws must neither look like a response that did not parse nor be retried.
    // An item counts as skipped once the callback has had it.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<PageData<T>> ReportAsync(ValueTask<PageData<T>> sending, CommandState state, Action<DynamoDbMappingException> report)
    {
        var page = await sending.ConfigureAwait(false);
        if (page.Skipped is { } skipped)
        {
            foreach (var failure in skipped)
            {
                report(failure);
                state.Table.Core.CountSkippedItem(TOperation.Name, state.Table.Name);
            }
        }

        return page;
    }

    private static async IAsyncEnumerable<T> ReadItemsAsync(CommandState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PageData<T> page;
        do
        {
            page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            state.Read.Taken += page.Items.Length;
            foreach (var item in page.Items)
            {
                yield return item;
            }
        }
        while (page.HasMore && !state.Read.IsTaken);
    }

    private static async IAsyncEnumerable<Page<T>> ReadPagesAsync(CommandState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PageData<T> page;
        do
        {
            page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            state.Read.Taken += page.Items.Length;
            yield return ToPage(page, state);
        }
        while (page.HasMore && !state.Read.IsTaken);
    }

    // Each response is sized to what is still to give, so the items of all of them fit, and the key of the last one continues after the last item given.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<Page<T>> TakeAsync(PageData<T> page, CommandState state, CancellationToken cancellationToken)
    {
        state.Read.Taken = page.Items.Length;
        if (!page.HasMore || state.Read.IsTaken)
        {
            return ToPage(page, state);
        }

        var items = new List<T>();
        var count = 0;
        var scannedCount = 0;
        ConsumedCapacity? capacity = null;
        while (true)
        {
            items.AddRange(page.Items);
            count += page.Count;
            scannedCount += page.ScannedCount;
            capacity = CapacitySum.Add(capacity, state.ResponseCapacity);
            if (!page.HasMore || state.Read.IsTaken)
            {
                return new Page<T>(items, count, scannedCount, capacity, NextOf(page, state));
            }

            page = await SendAsync(state, cancellationToken).ConfigureAwait(false);
            state.Read.Taken += page.Items.Length;
        }
    }

    private static Page<T> ToPage(PageData<T> page, CommandState state) => new(page.Items, page.Count, page.ScannedCount, state.ResponseCapacity, NextOf(page, state));

    private static ContinuationToken NextOf(PageData<T> page, CommandState state) =>
        page.HasMore ? ContinuationToken.FromRawKey(state.PageKey.WrittenSpan, state, plan: null) : default;
}
