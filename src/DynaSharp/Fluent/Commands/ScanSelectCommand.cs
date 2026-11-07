using System.Diagnostics;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>Scan</c> of one attribute, as <see cref="ScanCommand{T}.Select{TValue, TConverter}(Attr{TValue, TConverter})"/> leaves it. Nothing can be added any more, which the type enforces, so <c>Filter</c>,
/// <c>Limit</c>, <c>Segment</c> and the other clauses come before <c>Select</c>: what is left is to send it, with the same methods as <see cref="ScanCommand{T}"/>. It charges what any scan charges.
/// </summary>
/// <remarks>
/// An attribute that an item does not have follows the type of the handle: null for a nullable one, an empty set for a set, and otherwise a <see cref="DynamoDbMappingException"/>, which holds no key, because
/// only the attribute came back.
/// </remarks>
/// <typeparam name="TValue">The type of the attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter">The converter that reads it.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct ScanSelectCommand<TValue, TConverter> : IReadCommand
    where TConverter : IDynamoDbConverter<TValue>
{
    private const string Operation = "Scan";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal ScanSelectCommand(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Reads the attribute of all items of all pages as they come, as <see cref="ScanCommand{T}.ItemsAsync"/> reads the items.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result. It is looked at per page.</param>
    /// <returns>The values, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<TValue> ValuesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<ScanSelectPageOperation<TValue, TConverter>, TValue>.Items(Enter(), cancellationToken);

    /// <summary>Reads all pages as they come, as <see cref="ScanCommand{T}.PagesAsync"/> does, with the values in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The pages, which can be enumerated once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public IAsyncEnumerable<Page<TValue>> PagesAsync(CancellationToken cancellationToken = default) =>
        PagedRead<ScanSelectPageOperation<TValue, TConverter>, TValue>.Pages(Enter(), cancellationToken);

    /// <summary>Reads one page, as <see cref="ScanCommand{T}.PageAsync(ContinuationToken?, CancellationToken)"/> does, with the values in <see cref="Page{T}.Items"/>.</summary>
    /// <param name="after">The position to continue from, the <see cref="Page{T}.Next"/> of the page before, or <c>null</c> for the first page, as is a <c>default</c> token. It must come from the same scan, and the same segment.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="FormatException"><paramref name="after"/> does not fit what <see cref="DynamoDbClientOptions.TokenSigner"/> says: a client that signs gets a plain token that came as text, or one whose signature does not hold for this read; a client that does not sign gets a signed one. The message quotes nothing of the token, and no request goes out.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example a position of another scan; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no such attribute and the handle cannot be null, or its value cannot be read as <typeparamref name="TValue"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<TValue>> PageAsync(ContinuationToken? after = null, CancellationToken cancellationToken = default) =>
        PagedRead<ScanSelectPageOperation<TValue, TConverter>, TValue>.PageAsync(Enter(), after.GetValueOrDefault(), cancellationToken);

    /// <summary>Reads the first page, as <see cref="PageAsync(ContinuationToken?, CancellationToken)"/> does without a position, for a caller that has a cancellation token and no position yet.</summary>
    /// <param name="cancellationToken">Cancels the call. It has no default, because <c>PageAsync()</c> belongs to the overload with the position.</param>
    /// <returns>The page. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">An item has no such attribute and the handle cannot be null, or its value cannot be read as <typeparamref name="TValue"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<Page<TValue>> PageAsync(CancellationToken cancellationToken) => PageAsync(null, cancellationToken);

    /// <summary>
    /// Renders the body of the first page, as DynamoDB JSON, without sending it. It ends the command, and is meant for tests and debugging. It shows what goes out, and the text contains the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
