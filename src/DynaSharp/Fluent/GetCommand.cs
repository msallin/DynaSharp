using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>GetItem</c> under construction. Chain the options and end with <see cref="ItemAsync"/> or <see cref="SendAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// A command is a ref struct over pooled state, which is what makes it free of allocation. The compiler therefore keeps it from being stored in
/// a field, captured in a lambda or kept across an <c>await</c>: compute values first, or put the chain into a method that returns the
/// command. Every call returns a new handle and uses up the one it was called on, so reusing a command throws
/// <see cref="InvalidOperationException"/>; to send the same request twice, build it twice.
/// </para>
/// </remarks>
/// <typeparam name="T">How the item is read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct GetCommand<T> : IRetryable<GetCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "GetItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal GetCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>
    /// Asks for a strongly consistent read, which sees every write that succeeded before it and costs twice the capacity. A read is eventually
    /// consistent otherwise, and can miss a very recent write.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public GetCommand<T> Consistent()
    {
        var state = Enter();
        state.ConsistentRead = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads only some attributes. It saves bandwidth, not capacity: DynamoDB charges for the whole item. Reading into an entity needs what
    /// the entity requires, so a projection is mostly for <see cref="Item"/>.
    /// </summary>
    /// <param name="paths">The names or document paths. Reserved words need no care, and a second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public GetCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        var state = Enter();
        state.Expressions.Project(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public GetCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Project(attributes);
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="GetResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public GetCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public GetCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>Sends the command. A missing item is not an error: <see cref="GetResult{T}.Found"/> is <see langword="false"/>.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<GetResult<T>> SendAsync(CancellationToken cancellationToken = default) =>
        Enter().SendAsync<GetOperation<T>, GetResult<T>>(cancellationToken);

    /// <summary>
    /// Sends the command and returns the item, or <see langword="null"/> if none has the key: <c>var order = await orders.Get(key).ItemAsync(ct);</c>. It is the same request as
    /// <see cref="SendAsync"/>, which stays for <see cref="GetResult{T}.Found"/> and the capacity. An entity that is a struct reads as its default value when there is no item, so only
    /// <see cref="GetResult{T}.Found"/> tells a miss from an item of default values.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The item. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<T?> ItemAsync(CancellationToken cancellationToken = default) =>
        Enter().SendAsync<GetValueOperation<T>, T?>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command, for <c>await table.Get(pk, sk)</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<GetResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
