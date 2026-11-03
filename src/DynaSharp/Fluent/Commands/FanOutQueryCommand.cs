using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> of several partitions at once, merged by sort key, the read of write shards and time buckets: <c>orders.Query(shards, SortKeyCondition.Ge(from)).Descending().ItemsAsync(ct)</c>.
/// Chain the options and end with <see cref="ItemsAsync"/>, <see cref="PageAsync(int, ContinuationToken, CancellationToken)"/> or <see cref="CountAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// It is one query per partition. A fan-out costs at least one read per partition, and each page of each partition is one call, with its own span, retries and metrics, and its own
/// capacity. It is not a transaction and not a snapshot: partitions are read at different times. The default of a read is eventually consistent, see <see cref="Consistent"/>.
/// </para>
/// <para>
/// Items merge in the order DynamoDB gives within a partition: strings and binary values by their bytes, so that <c>Z</c> comes before <c>a</c>, and numbers by value, at their full 38
/// digits. Items with equal sort keys come by the position of their partition in the list, and <see cref="Descending"/> reverses both. A table or index without a sort key
/// has only ties, so its partitions come one after the other.
/// </para>
/// <para>
/// A partition is read when the merge needs it. To give the first item it needs the first page of every partition, because any of them could hold what comes first. After
/// that it reads the next page of a partition only when that partition's turn comes, so the partitions that have much to give are the ones that are read, and leaving the
/// enumeration early reads nothing more. At most one page per partition is in memory, which is what makes <see cref="Limit"/> worth setting when there are many partitions.
/// </para>
/// <para>
/// As with any read of an entity with a discriminator, the filter holds it, see <see cref="QueryCommand{T}"/>. A filter that passes little makes a partition follow its pages until it
/// passes something, or until the key it has read up to sorts after what the other partitions offer. See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct FanOutQueryCommand<T> : IFilterable<FanOutQueryCommand<T>>, IRetryable<FanOutQueryCommand<T>>, IReadCommand
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "Query";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;
    private readonly FanOutPlan? _plan;

    private FanOutQueryCommand(CommandState state, TableModel table, FanOutPlan plan)
    {
        _state = state;
        _version = state.Version;
        _table = table;
        _plan = plan;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(FanOutQueryCommand<T> command) => new(command._state, command._version, Operation, command._table);

    /// <inheritdoc cref="QueryCommand{T}.Filter(ExpressionHandler)"/>
    public FanOutQueryCommand<T> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Filter);
        return new(state, state.Table, _plan!);
    }

    /// <inheritdoc/>
    public FanOutQueryCommand<T> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Filter, condition);
        return new(state, state.Table, _plan!);
    }

    /// <summary>
    /// Reads only some attributes. It saves bandwidth, not capacity: DynamoDB charges for whole items. The merge needs the keys of an item, so they join the projection on their own,
    /// also for <see cref="Item"/>, where they show up in the result, and a read of an entity needs its discriminator as well. A global secondary index gives only what it projects.
    /// </summary>
    /// <param name="paths">The names or document paths. Reserved words need no care, and a second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public FanOutQueryCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        var state = Enter();
        state.Expressions.Project(paths);
        return new(state, state.Table, _plan!);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public FanOutQueryCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Project(attributes);
        return new(state, state.Table, _plan!);
    }

    /// <summary>
    /// Limits the items DynamoDB reads <em>per page of each partition</em>, before the filter. It does not limit the result, and it is not the size of a merged page, which
    /// <see cref="PageAsync(int, ContinuationToken, CancellationToken)"/> takes. A cap on items is <c>.ItemsAsync(ct).Take(n)</c>, which stops when it has them. A small limit reads less ahead of what the merge gives out, and costs more calls.
    /// </summary>
    /// <param name="items">The most items per page of a partition.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> is below 1.</exception>
    public FanOutQueryCommand<T> Limit(int items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(items, 1);
        var state = Enter();
        state.Read.Limit = items;
        return new(state, state.Table, _plan!);
    }

    /// <summary>Reads every partition downwards and merges from the top, so the newest of all partitions come first. Items with equal sort keys come by falling position, so the result is the exact reverse of the ascending one.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public FanOutQueryCommand<T> Descending()
    {
        var state = Enter();
        state.Read.Descending = true;
        return new(state, state.Table, _plan!);
    }

    /// <summary>
    /// Asks for a strongly consistent read of each partition, which sees every write that succeeded before that read started and costs twice the capacity. Partitions are still read
    /// at different times. A global secondary index is always eventually consistent, so this throws on one that is known to be global, which is an index opened through its entity or one with
    /// another partition key than the table, and DynamoDB answers with a <see cref="ValidationException"/> for any other.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The index is known to be global.</exception>
    public FanOutQueryCommand<T> Consistent()
    {
        var state = Enter();
        state.Table.ThrowIfNotConsistent();
        state.Read.ConsistentRead = true;
        return new(state, state.Table, _plan!);
    }

    /// <summary>
    /// Sets how many requests may be in flight at once, 8 unless set. It takes effect where several partitions are needed at once, above all at the start of a read and in
    /// <see cref="CountAsync"/>. It is the way to keep a fan-out over many partitions from using up the throughput of a table.
    /// </summary>
    /// <param name="requests">The most requests at once.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requests"/> is below 1.</exception>
    public FanOutQueryCommand<T> MaxConcurrency(int requests)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requests, 1);
        var state = Enter();
        _plan!.MaxConcurrency = requests;
        return new(state, state.Table, _plan);
    }

    /// <summary>
    /// Asks for the consumed capacity of each call, for the span of that call and the capacity metric. <see cref="PageAsync(int, ContinuationToken, CancellationToken)"/> also adds up its calls in
    /// <see cref="Page{T}.ConsumedCapacity"/>: units add, details add by the name of the index, and a number that a response leaves out counts as zero. That includes pages whose
    /// items did not make it into the result, so it is what the page cost, and a read again on the next page is charged again. The other terminals have no result to put it on.
    /// </summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public FanOutQueryCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table, _plan!);
    }

    /// <inheritdoc/>
    public FanOutQueryCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table, _plan!);
    }

    /// <summary>
    /// Reads the merged items as they come, following pages that a filter left empty. Nothing is sent until the enumeration starts, a request goes out only when the merge cannot go on
    /// without it, and between two items no request is in flight, so breaking out early saves everything that was not read yet and leaves nothing behind. With no partitions
    /// there is no item and no request.
    /// </summary>
    /// <remarks>
    /// The first failure stops the requests in flight and surfaces once, from <c>MoveNextAsync</c>, as the exception it is. Cancelling stops them too. Items that were merged before
    /// that have been given out, and there is no way to continue: for that, read in pages with <see cref="PageAsync(int, ContinuationToken, CancellationToken)"/>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per item.</param>
    /// <returns>The items, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<T> ItemsAsync(CancellationToken cancellationToken = default) => FanOutRead<T>.Items(Prepare(), _plan!, cancellationToken);

    /// <summary>
    /// Reads one page of the merge, for paging that a client drives across requests: build the same query each time, with the same partitions in the same order and the same
    /// direction, and pass the position of the page before. It reads on through empty pages until the page is full or the partitions are done, so it can make many calls, and only
    /// a page that holds fewer items than <paramref name="pageSize"/> is sure to be the last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Page{T}.Next"/> holds one entry per partition: the key of the last item the caller got from it and the key of the item that comes next, or the fact that it is done.
    /// What a partition had read beyond the page is read again when its turn comes, so no item is lost or repeated. Until then the second key lets the merge pass it by, so a
    /// partition that sorts later, a time bucket for example, costs no read for the pages before it, while a partition that sorts in between is read on the page that reaches it. Its text
    /// grows with the number of partitions, by two keys each, and like every token it shows the key values. A client with <see cref="DynamoDbClientOptions.TokenSigner"/> signs it over the partition keys of the query as well, see <see cref="ContinuationToken"/>. If the table changes
    /// between pages, the merge goes by the keys of the token, so an item written before the next key can come later than its place.
    /// </para>
    /// <para>
    /// Unless <see cref="Limit"/> says otherwise, each partition is read in pages of <paramref name="pageSize"/>, because it cannot give more to this page, and the capacity of
    /// reading on would be wasted. <see cref="Page{T}.ScannedCount"/> and, with <see cref="ReturnCapacity"/>, <see cref="Page{T}.ConsumedCapacity"/> count what all its calls read.
    /// </para>
    /// </remarks>
    /// <param name="pageSize">The items of the page.</param>
    /// <param name="after">The position to continue from, or the default value for the first page. It must come from the same query.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is below 1.</exception>
    /// <exception cref="FormatException"><paramref name="after"/> is not a position of a fan-out, or of one with this number of partitions and this direction, the same as <see cref="ContinuationToken.Parse"/> for text that is no token, or it does not fit what <see cref="DynamoDbClientOptions.TokenSigner"/> says, as for a plain query. It comes from a client, so this is what bad input from there looks like, when the awaited task ends.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects a request, for example a position of another query; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(int pageSize, ContinuationToken after = default, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return FanOutRead<T>.PageAsync(Prepare(), _plan!, pageSize, after, cancellationToken);
    }

    /// <summary>Reads the first page, as <see cref="PageAsync(int, ContinuationToken, CancellationToken)"/> does at the default position, for a caller that has a token and no position yet.</summary>
    /// <param name="pageSize">The items of the page.</param>
    /// <param name="cancellationToken">Cancels the read. It has no default, because <c>PageAsync(pageSize)</c> belongs to the overload with the position.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects a request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(int pageSize, CancellationToken cancellationToken) => PageAsync(pageSize, default, cancellationToken);

    /// <summary>
    /// Counts the items of all partitions with <c>Select: COUNT</c>, so no item travels. It reads every partition to its end, up to <see cref="MaxConcurrency"/> at once, at the
    /// capacity of reading them all, as a count of a filtered read costs the items scanned. With no partitions it sends nothing and gives 0.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of items that pass the filter. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it has a projection, which is no use for a count and which DynamoDB rejects.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects a request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    /// <exception cref="OverflowException">There are more items than an <see cref="int"/> holds.</exception>
    public ValueTask<int> CountAsync(CancellationToken cancellationToken = default) => FanOutRead<T>.CountAsync(Enter(), _plan!, cancellationToken);

    /// <summary>
    /// Renders the body of the first page of the first partition, as DynamoDB JSON, without sending it; the other partitions differ only in <c>:pk</c>. It ends the command, allocates
    /// one string, and is meant for tests and debugging. The text contains the key and the values. With no partitions, <c>:pk</c> has no value.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson()
    {
        var state = Prepare();
        if (_plan!.PartitionKeys.Length > 0)
        {
            state.Expressions.AddPartitionKey(_plan.PartitionKeys[0]);
        }

        return state.RenderJson();
    }

    /// <summary>Starts a fan-out. Everything that is wrong with the partitions is found first, at the call that passed them.</summary>
    /// <param name="partitionKeys">The partitions.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions.</param>
    /// <param name="read">The table or index that is read.</param>
    /// <param name="table">The table of it.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, or there is a condition on a sort key and the table or index has none.</exception>
    internal static FanOutQueryCommand<T> Start(IEnumerable<AttributeValue> partitionKeys, SortKeyCondition sortKey, TableModel read, TableModel table)
    {
        ArgumentNullException.ThrowIfNull(partitionKeys);
        AttributeValue[] keys = [.. partitionKeys];
        return Begin(keys, sortKey, read, table);
    }

    /// <summary>Starts a fan-out over string keys that are enumerated here.</summary>
    /// <param name="partitionKeys">The partitions.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions.</param>
    /// <param name="read">The table or index that is read.</param>
    /// <param name="table">The table of it.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, or there is a condition on a sort key and the table or index has none.</exception>
    internal static FanOutQueryCommand<T> Start(IEnumerable<string> partitionKeys, SortKeyCondition sortKey, TableModel read, TableModel table)
    {
        ArgumentNullException.ThrowIfNull(partitionKeys);
        string[] keys = [.. partitionKeys];
        return Start(keys.AsSpan(), sortKey, read, table);
    }

    /// <summary>Starts a fan-out over string keys. A <see langword="null"/> among them counts as empty.</summary>
    /// <param name="partitionKeys">The partitions.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions.</param>
    /// <param name="read">The table or index that is read.</param>
    /// <param name="table">The table of it.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, or there is a condition on a sort key and the table or index has none.</exception>
    internal static FanOutQueryCommand<T> Start(scoped ReadOnlySpan<string> partitionKeys, SortKeyCondition sortKey, TableModel read, TableModel table)
    {
        var keys = new AttributeValue[partitionKeys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = partitionKeys[i] is { } key ? key : default(AttributeValue);
        }

        return Begin(keys, sortKey, read, table);
    }

    private static FanOutQueryCommand<T> Begin(AttributeValue[] partitionKeys, SortKeyCondition sortKey, TableModel read, TableModel table)
    {
        var plan = FanOutPlan.Create(partitionKeys, read, table);
        return new(read.BeginFanOut(sortKey), read, plan);
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    private CommandState Prepare()
    {
        var state = Enter();
        state.Table.AddRequiredNames(state);
        state.Expressions.CompleteProjection(_plan!.KeyNames);
        return state;
    }
}
