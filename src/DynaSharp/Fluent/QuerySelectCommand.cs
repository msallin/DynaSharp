using System.Diagnostics;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> of one attribute, as <see cref="QueryCommand{T}.Select{TValue, TConverter}(Attr{TValue, TConverter})"/> leaves it. Nothing can be added any more, which the type enforces, so <c>Filter</c>,
/// <c>Limit</c>, <c>Descending</c> and the other clauses come before <c>Select</c>: what is left are the terminals, which read as those of <see cref="QueryCommand{T}"/> do.
/// </summary>
/// <remarks>
/// An attribute that an item does not have follows the type of the handle: null for a nullable one, an empty set for a set, and otherwise a <see cref="DynamoDbMappingException"/>, which holds no key, because
/// only the attribute came back. Keys and all else stay out of the response, and the discriminator too, unless the key condition stands where its filter would.
/// </remarks>
/// <typeparam name="TValue">The type of the attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter">The converter that reads it.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct QuerySelectCommand<TValue, TConverter>
    where TConverter : IDynamoDbConverter<TValue>
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

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Reads the attribute of all items of all pages as they come, as <see cref="QueryCommand{T}.ItemsAsync"/> reads the items.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per page.</param>
    /// <returns>The values, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<TValue> ItemsAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<TValue, TConverter>, TValue>.Items(Enter(), cancellationToken);

    /// <summary>
    /// Reads the attribute of the first item that matches, in the order of the query, and stops there, as <see cref="QueryCommand{T}.ItemAsync"/> does, with what it says about <c>Limit</c>.
    /// <see cref="SelectResult{TValue}.Found"/> tells no match from a value, also for a handle that cannot be null.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read. It is looked at per page.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item of the page has no such attribute and the handle cannot be null, or its value cannot be read as <typeparamref name="TValue"/>, which counts for all items of that page, not only for the first.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<SelectResult<TValue>> ItemAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<TValue, TConverter>, TValue>.FindAsync(Enter(), cancellationToken);

    /// <summary>Reads all pages as they come, as <see cref="QueryCommand{T}.PagesAsync"/> does, with the values in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The pages, which can be enumerated once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<Page<TValue>> PagesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<TValue, TConverter>, TValue>.Pages(Enter(), cancellationToken);

    /// <summary>Reads one page, as <see cref="QueryCommand{T}.PageAsync"/> does, with the values in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="after">The position to continue from, or the default value for the first page. It must come from the same query.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another query; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no such attribute and the handle cannot be null, or its value cannot be read as <typeparamref name="TValue"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<TValue>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default) =>
        PagedRead<QuerySelectPageOperation<TValue, TConverter>, TValue>.PageAsync(Enter(), after, cancellationToken);

    /// <summary>
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is meant for tests and debugging. It shows what goes out, and the text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
