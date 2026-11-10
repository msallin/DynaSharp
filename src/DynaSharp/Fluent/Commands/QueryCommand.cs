using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> under construction: the items of one partition, in the order of the sort key. Chain the options and end with <see cref="ItemsAsync"/>, <see cref="ItemAsync"/>,
/// <see cref="PagesAsync"/>, <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> or <see cref="CountAsync"/> in one expression.
/// </summary>
/// <example>
/// <code>
/// var bigOrders = orders.Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.BeginsWith())
///     .Filter(Order.Attrs.Total.Gt(100m))
///     .Descending()
///     .ItemsAsync(ct);
///
/// await foreach (var order in bigOrders)
/// {
///     Console.WriteLine(order.Number);
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// DynamoDB answers in pages of at most 1 MB of data read, before any filter. <see cref="ItemsAsync"/> and <see cref="PagesAsync"/> follow them until the end, which
/// makes a filtered page without items no reason to stop. Each page is one call, with its own span, retries and metrics. See <see cref="GetCommand{T}"/>
/// for what a command is and what it cannot do. Reads are eventually consistent unless <see cref="Consistent"/> says otherwise.
/// </para>
/// <para>
/// <b>Other entities in the partition.</b> A read of an entity with a discriminator filters on it (<c>#_type = :t</c>), so that a table shared by several types gives back its own items, and that
/// filter is in the request for everyone to see. DynamoDB applies it after reading, so the items of other entities under the same partition key are still read: they count against
/// <see cref="Limit"/> and the capacity, and are in <see cref="Page{T}.ScannedCount"/> but not in <see cref="Page{T}.Count"/> or <see cref="Page{T}.Items"/>.
/// <c>orders.Query(Order.Keys.Pk.Of("dana")).Limit(10)</c> gives 9 orders in its first page when the profile of the customer shares the partition. A condition on the sort key that only
/// this entity matches, <c>orders.Query(Order.Keys.Pk.Of("dana"), Order.Keys.Sk.BeginsWith())</c>, reads its items alone. To read several types at once, read the table as <see cref="Item"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct QueryCommand<T> : IFilterable<QueryCommand<T>>, IRetryable<QueryCommand<T>>, IReadCommand
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
    /// Adds a filter in the grammar of DynamoDB, for what the conditions of handles do not cover: <c>.Filter($"size({Attr("tags")}) &lt; {10}")</c>. DynamoDB applies it after reading, so
    /// capacity is charged for every item read, not for those returned.
    /// </summary>
    /// <remarks>
    /// An interpolated <see cref="Attr"/> stands for an attribute name and any other interpolated value for a value. A page can come back empty. A filter cannot name a key of the table or
    /// index that is read; that is what the key condition is for. A second filter means both must hold.
    /// </remarks>
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
        state.Expressions.Projection.Add(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public QueryCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Projection.Add(attributes);
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads one attribute of each item instead of the whole entity: <c>await foreach (var total in orders.Query(pk).Select(Order.Attrs.Total).ValuesAsync(ct))</c>. It saves bandwidth, not capacity: DynamoDB charges for whole items.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Over <c>Project</c>.</b> The result is the value and no entity, so a <c>required</c> member cannot be in the way. The attribute is projected and nothing else: the filter on the discriminator keeps
    /// items of other types away, and only a key condition that stands in for that filter brings the discriminator along.
    /// </para>
    /// <para>
    /// <b>Last clause.</b> After <c>Select</c> only the send is left, as after <c>Return</c>: <see cref="Filter{TCondition}(TCondition)"/>, <see cref="Limit"/>, <see cref="Descending()"/>, <see cref="Consistent"/>, <see cref="ReturnCapacity"/> and <see cref="MaxAttempts"/> come before this, a second <c>Select</c> and a <c>Project</c> after it do not compile, and a <c>Project</c> before it throws.
    /// </para>
    /// <inheritdoc cref="GetCommand{T}.Select{TValue, TConverter}(Attr{TValue, TConverter})" path="/remarks/para[position()>1]"/>
    /// </remarks>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be read: DynamoDB returns a shortened list, in which its position is not known.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public QuerySelectCommand<TValue, TConverter> Select<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public QuerySelectCommand<TValue?, NullableConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableHandleConverter<TValue, TConverter>> attribute)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public QuerySelectCommand<TValue?, NullableReferenceConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableReferenceHandleConverter<TValue, TConverter>> attribute)
        where TValue : class
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>
    /// Limits the items DynamoDB reads <em>per page</em>, before the filter. It does not limit the result: <see cref="ItemsAsync"/> reads on until the end, in
    /// pages of this size, and with a filter a page holds fewer items than this. A cap on the items of the result is <see cref="Take"/>. To read one page, use <see cref="PageAsync(ContinuationToken?, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryCommand{T}" path="/remarks/para[2]"/>
    /// </remarks>
    /// <param name="items">The most items per page.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> is below 1.</exception>
    public QueryCommand<T> Limit(int items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(items, 1);
        var state = Enter();
        state.Read.Limit = items;
        return new(state, state.Table);
    }

    /// <summary>
    /// Gives at most <paramref name="items"/> items in all, across pages, and asks each page for no more than are still to come, so the newest ten read ten:
    /// <c>orders.Query(pk).Descending().Take(10).ItemsAsync(ct)</c>. <c>Take(n).PageAsync(ct)</c> gives them as one page, whose <see cref="Page{T}.HasMore"/> tells a cut read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Over the <c>Take</c> of LINQ.</b> <c>.ItemsAsync(ct).Take(n)</c> stops as well, but each page reads up to 1 MB, so its last page reads, and costs, more than it gives.
    /// This sets the <c>Limit</c> of each request to the items still to give, and never above a <see cref="Limit"/> of the command, which stays the most per page.
    /// </para>
    /// <para>
    /// <b>Filters.</b> <see cref="Limit"/> counts the items DynamoDB reads, before the filter; <c>Take</c> counts the items given. The items of other entities, which the filter of the
    /// discriminator drops, and the items that <see cref="OnMappingFailure"/> skips, are read and not given, so a page gives fewer than it asked for, and the read goes on with a
    /// smaller page for the rest. A filter that passes few items so takes many small calls; there the <c>Take</c> of LINQ reads in fewer, larger pages.
    /// </para>
    /// <para>
    /// <b>The ends of the command.</b> <see cref="ItemsAsync"/>, a <c>ValuesAsync</c> after <c>Select</c>, and <see cref="PagesAsync"/> stop after <paramref name="items"/> items.
    /// <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> reads as many pages as it takes and gives them as one, as a page of a fan-out does: up to <paramref name="items"/> items,
    /// <see cref="Page{T}.Count"/>, <see cref="Page{T}.ScannedCount"/> and <see cref="Page{T}.ConsumedCapacity"/> summed, and <see cref="Page{T}.Next"/> after the last item given,
    /// so a client pages in steps of <paramref name="items"/>. <see cref="CountAsync"/> counts up to <paramref name="items"/>, and <see cref="ItemAsync"/> reads with a limit of
    /// <paramref name="items"/>.
    /// </para>
    /// <para>
    /// <b>Was it cut.</b> <see cref="Page{T}.HasMore"/> says that DynamoDB has more to read. A page that stops at its limit has a next key even when no item follows, so a read of
    /// exactly <paramref name="items"/> items says so too. To know, take one more and look: <c>Take(max + 1)</c>, then <c>page.Items.Count &gt; max</c>.
    /// </para>
    /// <para>A second call replaces the first.</para>
    /// </remarks>
    /// <param name="items">The most items to give, from 1.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> is below 1.</exception>
    public QueryCommand<T> Take(int items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(items, 1);
        var state = Enter();
        state.Read.Take = items;
        return new(state, state.Table);
    }

    /// <summary>Reads the sort key downwards, so that the newest of a time-ordered partition come first (<c>ScanIndexForward: false</c>). It costs the same.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> Descending() => Descending(true);

    /// <summary>
    /// Reads the sort key downwards if <paramref name="descending"/> is <see langword="true"/>, and upwards, as without this, if it is not, for a direction the caller decides:
    /// <c>.Descending(newestFirst)</c>. It costs the same either way.
    /// </summary>
    /// <param name="descending">Whether the newest of a time-ordered partition come first. A second call replaces the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    public QueryCommand<T> Descending(bool descending)
    {
        var state = Enter();
        state.Read.Descending = descending;
        return new(state, state.Table);
    }

    /// <summary>
    /// Asks for a strongly consistent read, which sees every write that succeeded before it and costs twice the capacity. A read is eventually consistent otherwise.
    /// </summary>
    /// <remarks>
    /// A global secondary index is always eventually consistent, so this throws on one that is known to be global, which is an index opened through its entity or one with another
    /// partition key than the table, and DynamoDB answers with a <see cref="ValidationException"/> for any other.
    /// </remarks>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The index is known to be global.</exception>
    public QueryCommand<T> Consistent()
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
    public QueryCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>
    /// Skips an item that cannot be read as <typeparamref name="T"/> and hands its <see cref="DynamoDbMappingException"/> to <paramref name="onFailure"/>, where the read would otherwise end with it:
    /// <c>.OnMappingFailure(failure =&gt; bad.Add(failure.Key))</c>, or <c>.OnMappingFailure(static _ =&gt; { })</c> to skip and count only. Ending the read stays the default, because an item that does not fit
    /// is a bug of the model or of a writer, which a read that drops items would hide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exception says which item it was and why: <see cref="DynamoDbMappingException.EntityType"/>, <see cref="DynamoDbMappingException.AttributeName"/>, the message, and
    /// <see cref="DynamoDbMappingException.Key"/>, the key attributes of the table as they are stored, such as <c>USER#alice</c>, also on an index. The key holds values, so nothing logs it: the callback decides where it goes,
    /// and the message holds none. After a <c>Select</c> the response holds no key, so <see cref="DynamoDbMappingException.Key"/> is null there.
    /// </para>
    /// <para>
    /// It runs once per skipped item, when the page that holds it has arrived and before its items are given out, outside the call: it is no part of a retry, and an exception it throws ends the read as it is.
    /// The pages of a parallel scan and of a fan-out arrive on several threads at once, so there it can run concurrently. Each item it returns from counts in the metric <c>dynasharp.client.operation.skipped_items</c>.
    /// </para>
    /// <para>
    /// What a skipped item does to the rest of the read: it is not in <see cref="Page{T}.Items"/>, and <see cref="Page{T}.Count"/> still counts it, as DynamoDB does, so <c>Count - Items.Count</c> is what a page skipped.
    /// <see cref="Limit"/> counts it, because DynamoDB read it, so a page can hold fewer items. <c>ItemsAsync(ct).Take(n)</c> counts what it gets, so it reads on past a skipped item, and
    /// <see cref="ItemAsync"/> gives the first item that can be read. <see cref="Page{T}.Next"/> continues after the page, so a skipped item is not read again. <see cref="CountAsync"/> reads no item and counts it.
    /// </para>
    /// <para>
    /// Only a <see cref="DynamoDbMappingException"/> is skipped: an item of another type in a read keyed by the discriminator, a value a converter cannot read, a missing <c>required</c> attribute,
    /// a stored key that does not fit its template. It covers every way of sending the read that gives items, also those of a <c>Select</c> after it. A second call replaces the first.
    /// </para>
    /// </remarks>
    /// <param name="onFailure">What to do with the failure of a skipped item.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public QueryCommand<T> OnMappingFailure(Action<DynamoDbMappingException> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        var state = Enter();
        state.Read.OnMappingFailure = onFailure;
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
    /// Reads the first item that matches, in the order of the query, and stops there. Without a filter it asks for one item; with one it goes on through pages that the filter left empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One item without a filter.</b> A read without a filter, a <see cref="Limit"/> and a <see cref="Take"/> sends <c>"Limit": 1</c>, which is all that <see cref="ToRequestJson"/> does not show:
    /// its first item is the one it gives, so <c>orders.Query(pk).Descending().ItemAsync(ct)</c> reads the newest and pays for one.
    /// </para>
    /// <para>
    /// <b>With a filter</b> it adds no limit, because a limit counts items before the filter, so a limit of 1 would look at one item per page. The first page comes back whole, up to 1 MB, and costs
    /// the capacity of all it read. A read of an entity filters on its discriminator, unless its key condition stands for that, so this holds for it too. A <see cref="Limit"/> or a
    /// <see cref="Take"/> of the chain stays as it is: <c>Limit(1)</c> on a read of an entity in a partition of several types counts the items of the other types too, and this goes on to the next page.
    /// </para>
    /// <para>An entity that is a struct reads as its default value when no item matches, so a miss looks like an item of default values there. <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> tells them apart.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read. It is looked at per page.</param>
    /// <returns>The item, or <see langword="null"/> if none matches. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item of the page cannot be read as <typeparamref name="T"/>, which counts for all items of that page, not only for the first.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<T?> ItemAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.FirstAsync(Prepare(), cancellationToken);

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
    /// <remarks>
    /// <inheritdoc cref="QueryCommand{T}" path="/remarks/para[2]"/>
    /// </remarks>
    /// <param name="after">The position to continue from, the <see cref="Page{T}.Next"/> of the page before, or <c>null</c> for the first page, as is a <c>default</c> token. It must come from the same query.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="FormatException"><paramref name="after"/> does not fit what <see cref="DynamoDbClientOptions.TokenSigner"/> says: a client that signs gets a plain token that came as text, or one whose signature does not hold for this read; a client that does not sign gets a signed one. The message quotes nothing of the token, and no request goes out.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another query; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(ContinuationToken? after = null, CancellationToken cancellationToken = default) =>
        PagedRead<QueryPageOperation<T>, T>.PageAsync(Prepare(), after.GetValueOrDefault(), cancellationToken);

    /// <summary>Reads the first page, as <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> does without a position, for a caller that has a cancellation token and no position yet.</summary>
    /// <param name="cancellationToken">Cancels the call. It has no default, because <c>PageAsync()</c> belongs to the overload with the position.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<T>> PageAsync(CancellationToken cancellationToken) => PageAsync(null, cancellationToken);

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
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, and is meant for tests and debugging.
    /// The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => Prepare().RenderJson();

    /// <summary>Starts a query of one partition of a table or index, its key condition and the filter of the discriminator written.</summary>
    /// <param name="read">The table or index that is read.</param>
    /// <param name="partitionKey">The partition key as it is stored.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the table or index has none.</exception>
    internal static QueryCommand<T> Start(TableModel read, AttributeValue partitionKey, SortKeyCondition sortKey) => new(read.BeginQuery(partitionKey, sortKey), read);

    private CommandState Selecting(string attribute)
    {
        Fluent.Selection.ThrowIfNoAttribute(attribute);
        var state = Enter();
        state.SelectAttribute(attribute, state.Table.DiscriminatorOnQuery);
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
