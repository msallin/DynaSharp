using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> under construction: the items of one partition, in the order of the sort key. Chain the options and end with <see cref="ItemsAsync"/>,
/// <see cref="PagesAsync"/>, <see cref="PageAsync"/> or <see cref="CountAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB answers in pages of at most 1 MB of data read, before any filter. <see cref="ItemsAsync"/> and <see cref="PagesAsync"/> follow them until the end, which
/// makes a filtered page without items no reason to stop. Each page is one call, with its own span, retries and metrics. See <see cref="GetCommand{T}"/>
/// for what a command is and what it cannot do. Reads are eventually consistent unless <see cref="Consistent"/> says otherwise.
/// </para>
/// <para>
/// A read of an entity with a discriminator filters on it (<c>#_type = :t</c>), so that a table shared by several types gives back its own items, and that
/// filter is in the request for everyone to see. It adds to <see cref="Page{T}.ScannedCount"/> what it removes from <see cref="Page{T}.Count"/> and changes no
/// capacity. To read several types at once, read the table as <see cref="Item"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct QueryCommand<T> : IFilterable<QueryCommand<T>>, IRetryable<QueryCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "Query";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal QueryCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(QueryCommand<T> command) => command.Handle;

    /// <summary>
    /// Adds a filter in the grammar of DynamoDB, for what struct conditions do not cover. An attribute in a hole stands for its name and anything else is a
    /// value. DynamoDB applies it after reading, so capacity is charged for every item scanned, not for those returned, and a page can come back empty. A filter
    /// cannot name a key of the table or index that is read; that is what the key condition is for. A second filter means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Filter);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public QueryCommand<T> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Filter, condition);
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads only some attributes. It saves bandwidth, not capacity: DynamoDB charges for whole items. A read of an entity needs its discriminator and its
    /// keys, so they join the projection on their own, and it needs whatever else the entity requires, so a projection is mostly for <see cref="Item"/>.
    /// A global secondary index gives only what it projects.
    /// </summary>
    /// <param name="paths">The names or document paths. Reserved words need no care, and a second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public QueryCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        var state = Enter();
        state.Expressions.Project(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public QueryCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Project(attributes);
        return new(state, state.Table);
    }

    /// <summary>
    /// Limits the items DynamoDB reads <em>per page</em>, before the filter. It does not limit the result: <see cref="ItemsAsync"/> reads on until the end, in
    /// pages of this size, and with a filter a page holds fewer items than this. To stop early, stop enumerating, or read one page with <see cref="PageAsync"/>.
    /// </summary>
    /// <param name="items">The most items per page.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> is below 1.</exception>
    public QueryCommand<T> Limit(int items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(items, 1);
        var state = Enter();
        state.Limit = items;
        return new(state, state.Table);
    }

    /// <summary>Reads the sort key downwards, so that the newest of a time-ordered partition come first (<c>ScanIndexForward: false</c>). It costs the same.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> Descending()
    {
        var state = Enter();
        state.Descending = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Asks for a strongly consistent read, which sees every write that succeeded before it and costs twice the capacity. A read is eventually consistent
    /// otherwise. A global secondary index is always eventually consistent, and DynamoDB rejects this option on one with a <see cref="ValidationException"/>.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> Consistent()
    {
        var state = Enter();
        state.ConsistentRead = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Asks for the consumed capacity of each page, which goes to <see cref="Page{T}.ConsumedCapacity"/>, the span of the page and the capacity metric.
    /// <see cref="ItemsAsync"/> has no result to put it on, so there it reaches telemetry only.
    /// </summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public QueryCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads all items of all pages as they come, following pages that a filter left empty. Nothing is sent until the enumeration starts, and only what the
    /// enumeration reaches is read, so breaking out early saves the remaining pages. Capacity is charged for the items scanned, which with a filter can be
    /// far more than those returned; it is not reported here, only on the spans and the metric, see <see cref="PagesAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per page.</param>
    /// <returns>The items, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<T> ItemsAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.Items(Prepare(), cancellationToken);

    /// <summary>
    /// Reads all pages as they come, for code that wants <see cref="Page{T}.ConsumedCapacity"/> or works page by page. It gives every response, also one that a
    /// filter left empty. Otherwise as <see cref="ItemsAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The pages, which can be enumerated once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<Page<T>> PagesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.Pages(Prepare(), cancellationToken);

    /// <summary>
    /// Reads one page, for paging that a client drives across requests: build the same query each time and pass the position of the page before. The page can be
    /// empty and still have a next one, so a client that wants items keeps going while <see cref="Page{T}.HasMore"/> holds.
    /// </summary>
    /// <param name="after">The position to continue from, or the default value for the first page. It must come from the same query.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another query; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.PageAsync(Prepare(), after, cancellationToken);

    /// <summary>
    /// Counts the items of all pages with <c>Select: COUNT</c>, so no item travels. It costs the capacity of reading them all, as a count of a filtered read costs
    /// the items scanned, and it takes as many calls as the read would. <see cref="Limit"/> sets the size of each page and not the count.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of items that pass the filter. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it has a projection, which is no use for a count and which DynamoDB rejects.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    /// <exception cref="OverflowException">There are more items than an <see cref="int"/> holds.</exception>
    public ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.CountAsync(Enter(), cancellationToken);

    /// <summary>
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is meant for tests and debugging.
    /// The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => Prepare().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    private CommandState Prepare()
    {
        var state = Enter();
        state.Table.AddRequiredNames(state);
        return state;
    }
}
