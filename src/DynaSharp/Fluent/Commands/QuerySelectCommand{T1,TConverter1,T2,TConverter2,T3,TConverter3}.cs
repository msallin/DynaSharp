using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> of three attributes, as <c>And</c> leaves it: what is left is another <c>And</c>, up to four attributes, and the send. Each attribute follows the rules of
/// <see cref="QuerySelectCommand{TValue, TConverter}"/>, and the values come as a tuple in the order of the calls.
/// </summary>
/// <typeparam name="T1">The type of the first attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter1">The converter that reads the first attribute.</typeparam>
/// <typeparam name="T2">The type of the second attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter2">The converter that reads the second attribute.</typeparam>
/// <typeparam name="T3">The type of the third attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter3">The converter that reads the third attribute.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
[SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1414:Tuple types in signatures should have element names", Justification = "The elements are the attributes in the order of the calls, which a name would only repeat; a caller deconstructs them.")]
public readonly ref struct QuerySelectCommand<T1, TConverter1, T2, TConverter2, T3, TConverter3> : IReadCommand
    where TConverter1 : IDynamoDbConverter<T1>
    where TConverter2 : IDynamoDbConverter<T2>
    where TConverter3 : IDynamoDbConverter<T3>
{
    private const string Operation = "Query";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal QuerySelectCommand(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>Reads one more attribute in the same request, so that the read gives a tuple. See <see cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})"/>.</summary>
    /// <remarks><inheritdoc cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})" path="/remarks"/></remarks>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be read.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public QuerySelectCommand<T1, TConverter1, T2, TConverter2, T3, TConverter3, TNext, TNextConverter> And<TNext, TNextConverter>(Attr<TNext, TNextConverter> attribute)
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>Reads one more nullable attribute in the same request, which gives null for an item without it. See <see cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})"/>.</summary>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public QuerySelectCommand<T1, TConverter1, T2, TConverter2, T3, TConverter3, TNext?, NullableConverter<TNext, TNextConverter>> And<TNext, TNextConverter>(Attr<TNext, NullableHandleConverter<TNext, TNextConverter>> attribute)
        where TNext : struct
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>Reads one more nullable attribute in the same request, which gives null for an item without it. See <see cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})"/>.</summary>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public QuerySelectCommand<T1, TConverter1, T2, TConverter2, T3, TConverter3, TNext?, NullableReferenceConverter<TNext, TNextConverter>> And<TNext, TNextConverter>(Attr<TNext, NullableReferenceHandleConverter<TNext, TNextConverter>> attribute)
        where TNext : class
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>Reads the attributes of all items of all pages as they come, as <see cref="QuerySelectCommand{TValue, TConverter}.ValuesAsync"/> reads one.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per page.</param>
    /// <returns>The tuples, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<(T1, T2, T3)> ValuesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<(T1, T2, T3), SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3>>, (T1, T2, T3)>.Items(Enter(), cancellationToken);

    /// <summary>
    /// Reads the attributes of the first item that matches, in the order of the query, and stops there, as <see cref="QueryCommand{T}.ItemAsync"/> does, with what it says about <c>Limit</c>.
    /// <see cref="SelectResult{TValue}.Found"/> tells no match from a value.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read. It is looked at per page.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no attribute that its handle cannot do without, or a value that cannot be read as the type of its handle.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<SelectResult<(T1, T2, T3)>> ValueAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<(T1, T2, T3), SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3>>, (T1, T2, T3)>.FindAsync(Enter(), cancellationToken);

    /// <summary>Reads all pages as they come, as <see cref="QuerySelectCommand{TValue, TConverter}.PagesAsync"/> does, with the tuples in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The pages, which can be enumerated once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<Page<(T1, T2, T3)>> PagesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<(T1, T2, T3), SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3>>, (T1, T2, T3)>.Pages(Enter(), cancellationToken);

    /// <summary>Reads one page, as <see cref="QuerySelectCommand{TValue, TConverter}.PageAsync(ContinuationToken?, CancellationToken)"/> does, with the tuples in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="after">The position to continue from, the <see cref="Page{T}.Next"/> of the page before, or <c>null</c> for the first page, as is a <c>default</c> token. It must come from the same query.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="FormatException"><paramref name="after"/> does not fit what <see cref="DynamoDbClientOptions.TokenSigner"/> says. The message quotes nothing of the token, and no request goes out.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another query; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no attribute that its handle cannot do without, or a value that cannot be read as the type of its handle.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<(T1, T2, T3)>> PageAsync(ContinuationToken? after = null, CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<(T1, T2, T3), SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3>>, (T1, T2, T3)>.PageAsync(Enter(), after.GetValueOrDefault(), cancellationToken);

    /// <summary>Reads the first page, as <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> does without a position, for a caller that has a cancellation token and no position yet.</summary>
    /// <param name="cancellationToken">Cancels the call. It has no default, because <c>PageAsync()</c> belongs to the overload with the position.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no attribute that its handle cannot do without, or a value that cannot be read as the type of its handle.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<(T1, T2, T3)>> PageAsync(CancellationToken cancellationToken) => PageAsync(null, cancellationToken);

    /// <summary>
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, and is meant for tests and debugging. It shows what goes out, and the text contains the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
