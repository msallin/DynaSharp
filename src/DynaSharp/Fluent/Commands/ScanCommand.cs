using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>Scan</c> under construction: every item of a table or index, in no useful order, at the cost of reading them all. Chain the options and end with
/// <see cref="ItemsAsync"/>, <see cref="PagesAsync"/>, <see cref="PageAsync(ContinuationToken, CancellationToken)"/> or <see cref="CountAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// A scan is what a query is without its key condition, and <see cref="QueryCommand{T}"/> says how pages and discriminators work. It charges capacity for the
/// whole table whatever the filter leaves, so a scan on a hot path usually wants to be a query of an index.
/// </para>
/// <para>
/// To go faster, <see cref="Parallel"/> reads several segments at once and merges them. <see cref="Segment"/> reads one segment, for workers that each own one.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct ScanCommand<T> : IFilterable<ScanCommand<T>>, IRetryable<ScanCommand<T>>, IReadCommand
    where T : IDynamoDbDocument<T>
{
    /// <summary>The most segments DynamoDB accepts.</summary>
    internal const int MaxSegments = 1_000_000;

    private const string Operation = "Scan";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal ScanCommand(CommandState state, TableModel table)
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
    public static implicit operator CommandHandle(ScanCommand<T> command) => command.Handle;

    /// <summary>
    /// Adds a filter in the grammar of DynamoDB, for what struct conditions do not cover. An attribute in a hole stands for its name and anything else is a
    /// value. DynamoDB applies it after reading, so capacity is charged for every item scanned, not for those returned, and a page can come back empty. A
    /// second filter means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public ScanCommand<T> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Filter);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public ScanCommand<T> Filter<TCondition>(TCondition condition)
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
    public ScanCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        var state = Enter();
        state.Expressions.Project(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public ScanCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Project(attributes);
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads one attribute instead of the entity, for what a service wants from an item more often than the item: <c>await foreach (var total in orders.Scan().Select(Order.Attrs.Total).ValuesAsync(ct))</c>. It projects the attribute and nothing else: the filter on the discriminator keeps items of other types away, so it saves bandwidth, not capacity: DynamoDB
    /// charges for whole items. The result is the value and no entity, so a <c>required</c> member cannot be in the way, which is what this has over <c>Project</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain ends here but for its terminal, as with <c>Return</c>: <see cref="Filter{TCondition}(TCondition)"/>, <see cref="Limit"/>, <see cref="Consistent"/>, <see cref="Segment"/>, <see cref="ReturnCapacity"/> and <see cref="MaxAttempts"/> come before this, a second <c>Select</c> and a <c>Project</c> after it do not compile, and a <c>Project</c> before it throws.
    /// </para>
    /// <para>
    /// A missing attribute follows the type of the handle: null for one of a nullable property, an empty set for a set, because DynamoDB has none, and otherwise a <see cref="DynamoDbMappingException"/>. Never a default, and that is where this differs from a read of the entity,
    /// which leaves a property that is not <c>required</c> at its initial value: an <c>int</c> without its attribute is 0 there and throws here, and so does a <c>[Version]</c> on an item without one, which an <c>Update</c> with <c>IgnoreVersion()</c> creates, with a message that points at <c>VersionMissing.Allow</c>.
    /// In a nested path the last handle decides, so <c>ShipTo.At(Address.Attrs.City)</c> throws for an item without <c>ShipTo</c> when <c>City</c> cannot be null, which reading <c>ShipTo</c> itself would not.
    /// </para>
    /// <para>
    /// A <c>[KeyOnly]</c> property has no attribute and no handle. A stored property that a key of the table holds has both, and an item that an update created has only the key: a read of the entity restores
    /// the property from it, and this throws, which <c>DYNA209</c> reports.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be read: DynamoDB returns a shortened list, in which its position is not known.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public ScanSelectCommand<TValue, TConverter> Select<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public ScanSelectCommand<TValue?, NullableConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableHandleConverter<TValue, TConverter>> attribute)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public ScanSelectCommand<TValue?, NullableReferenceConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableReferenceHandleConverter<TValue, TConverter>> attribute)
        where TValue : class
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>
    /// Limits the items DynamoDB reads <em>per page</em>, before the filter. It does not limit the result: <see cref="ItemsAsync"/> reads on until the end, in
    /// pages of this size. A cap on items is <c>.ItemsAsync(ct).Take(n)</c>, which reads until it has them, across empty filtered pages, and stops. A small limit is a way to
    /// spread a scan out over time, because each page is one call.
    /// </summary>
    /// <param name="items">The most items per page; per segment, for a segmented scan.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> is below 1.</exception>
    public ScanCommand<T> Limit(int items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(items, 1);
        var state = Enter();
        state.Read.Limit = items;
        return new(state, state.Table);
    }

    /// <summary>
    /// Asks for a strongly consistent read, which sees every write that succeeded before it started and costs twice the capacity. A read is eventually consistent
    /// otherwise. A global secondary index is always eventually consistent, so this throws on one that is known to be global, which is an index opened through its entity or one with another
    /// partition key than the table, and DynamoDB answers with a <see cref="ValidationException"/> for any other.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The index is known to be global.</exception>
    public ScanCommand<T> Consistent()
    {
        var state = Enter();
        state.Table.ThrowIfNotConsistent();
        state.Read.ConsistentRead = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Asks for the consumed capacity of each page, which goes to <see cref="Page{T}.ConsumedCapacity"/>, the span of the page and the capacity metric.
    /// <see cref="ItemsAsync"/> has no result to put it on, so there it reaches telemetry only.
    /// </summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public ScanCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public ScanCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads one segment of a scan that several workers share. DynamoDB splits the table into <paramref name="of"/> parts, and every worker reads its own, with
    /// the same number of segments and the same options. A segment can be empty, and <see cref="Page{T}.HasMore"/> is per segment.
    /// </summary>
    /// <param name="segment">The segment, from zero.</param>
    /// <param name="of">The number of segments, from 1 to 1,000,000.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="of"/> is outside its range, or <paramref name="segment"/> is not one of the segments.</exception>
    public ScanCommand<T> Segment(int segment, int of)
    {
        ThrowIfOutOfRange(of, nameof(of));
        ArgumentOutOfRangeException.ThrowIfNegative(segment);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segment, of);
        var state = Enter();
        state.Read.Segment = segment;
        state.Read.TotalSegments = of;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads the whole scan in segments at once and merges them. The result is another command, which has only <see cref="ParallelScanCommand{T}.ItemsAsync"/>:
    /// a merged page has no single key to continue from. It does not change what is read or what capacity it costs, it spends the same capacity sooner, so
    /// it can use up the throughput of a table that serves other traffic.
    /// </summary>
    /// <param name="segments">The number of segments, which is also the most requests in flight at once, from 1 to 1,000,000.</param>
    /// <returns>The command of the parallel scan, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is outside its range.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or <see cref="Segment"/> chose one segment.</exception>
    public ParallelScanCommand<T> Parallel(int segments)
    {
        ThrowIfOutOfRange(segments, nameof(segments));
        var state = Enter();
        if (state.Read.TotalSegments != 0)
        {
            throw new InvalidOperationException($"This Scan on {state.Table.Name} reads one segment already, so it cannot read them all in parallel as well.");
        }

        state.Read.TotalSegments = segments;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads all items of all pages as they come, following pages that a filter left empty. Nothing is sent until the enumeration starts, and only what the
    /// enumeration reaches is read, so breaking out early saves the remaining pages. Capacity is charged for the items scanned, whatever the filter leaves;
    /// it is not reported here, only on the spans and the metric, see <see cref="PagesAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per page.</param>
    /// <returns>The items, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<T> ItemsAsync(CancellationToken cancellationToken = default) =>
        PagedRead<ScanPageOperation<T>, T>.Items(Prepare(), cancellationToken);

    /// <summary>
    /// Reads all pages as they come, for code that wants <see cref="Page{T}.ConsumedCapacity"/> or works page by page. It gives every response, also one that a
    /// filter left empty. Otherwise as <see cref="ItemsAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The pages, which can be enumerated once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<Page<T>> PagesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<ScanPageOperation<T>, T>.Pages(Prepare(), cancellationToken);

    /// <summary>
    /// Reads one page, for paging that a client drives across requests: build the same scan each time and pass the position of the page before. The page can be
    /// empty and still have a next one, so a client that wants items keeps going while <see cref="Page{T}.HasMore"/> holds.
    /// </summary>
    /// <param name="after">The position to continue from, or the default value for the first page. It must come from the same scan, and the same segment.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="FormatException"><paramref name="after"/> does not fit what <see cref="DynamoDbClientOptions.TokenSigner"/> says: a client that signs gets a plain token that came as text, or one whose signature does not hold for this read; a client that does not sign gets a signed one. The message quotes nothing of the token, and no request goes out.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another scan; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default) =>
        PagedRead<ScanPageOperation<T>, T>.PageAsync(Prepare(), after, cancellationToken);

    /// <summary>Reads the first page, as <see cref="PageAsync(ContinuationToken, CancellationToken)"/> does at the default position, for a caller that has a token and no position yet.</summary>
    /// <param name="cancellationToken">Cancels the call. It has no default, because <c>PageAsync()</c> belongs to the overload with the position.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(CancellationToken cancellationToken) => PageAsync(default, cancellationToken);

    /// <summary>
    /// Counts the items of all pages with <c>Select: COUNT</c>, so no item travels. It costs the capacity of reading the whole table, as many calls as a scan
    /// takes, and the count of a table that changes meanwhile is only an estimate. <see cref="Limit"/> sets the size of each page and not the count.
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
        PagedRead<ScanPageOperation<T>, T>.CountAsync(Enter(), cancellationToken);

    /// <summary>
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is meant for tests and debugging.
    /// The text contains the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => Prepare().RenderJson();

    private static void ThrowIfOutOfRange(int segments, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments, MaxSegments, parameterName);
    }

    private CommandState Selecting(string attribute)
    {
        Fluent.Selection.ThrowIfNoAttribute(attribute);
        var state = Enter();
        state.SelectAttribute(attribute, state.Table.DiscriminatorOnScan);
        return state;
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    private CommandState Prepare()
    {
        var state = Enter();
        state.Table.AddRequiredNames(state);
        return state;
    }
}
