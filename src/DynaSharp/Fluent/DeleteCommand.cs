using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>DeleteItem</c> under construction. Deleting what does not exist is no error unless <see cref="IfExists"/> asks for one. Chain the options and
/// end with <see cref="SendAsync"/> in one expression.
/// </summary>
/// <remarks>See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</remarks>
/// <typeparam name="T">How the returned item is read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct DeleteCommand<T> : IConditional<DeleteCommand<T>>, IRetryable<DeleteCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "DeleteItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal DeleteCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated condition.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(DeleteCommand<T> command) => new(command._state, command._version, Operation, command._table);

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what struct conditions do not cover. An attribute in a hole stands for its name and anything
    /// else is a value. The delete happens only if the condition holds, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Condition);
        return new(state, state.Table);
    }

    /// <summary>Adds a condition. The delete happens only if it holds, and a second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public DeleteCommand<T> If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        return new(state, state.Table);
    }

    /// <summary>
    /// Deletes only if an item has this key (<c>attribute_exists</c> on the partition key), so that a missing item fails. Version 0, of an entity or of <c>IfVersion(0)</c>, says that no item has the key, so with it the
    /// terminal throws <see cref="InvalidOperationException"/>, unless <c>IgnoreVersion()</c> took the version of the entity back.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> IfExists()
    {
        var state = Enter();
        WriteRules.IfExists(state);
        return new(state, state.Table);
    }

    /// <summary>Returns the deleted item in <see cref="WriteResult{T}.Item"/> (<c>ALL_OLD</c>). There is none if no item had the key.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> ReturnOld()
    {
        var state = Enter();
        state.ReturnValues = ReturnValue.AllOld;
        return new(state, state.Table);
    }

    /// <summary>
    /// Puts the existing item on <see cref="ConditionalCheckFailedException.Item"/> when a condition fails, which saves a read to find out why. It
    /// costs no extra capacity.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> ReturnOldOnFailure()
    {
        var state = Enter();
        state.ReturnOnFailure = ReturnValuesOnConditionCheckFailure.AllOld;
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="WriteResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>Asks for the size estimate of the item collection, which goes to <see cref="WriteResult{T}.ItemCollectionMetrics"/>. DynamoDB has it for tables with a local secondary index.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public DeleteCommand<T> ReturnMetrics()
    {
        var state = Enter();
        state.ReturnMetrics = ReturnItemCollectionMetrics.Size;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public DeleteCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>Sends the command. A retry repeats the delete, which is safe unless <see cref="ReturnOld"/> or a condition depends on the first one having failed.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, a delete by key of an entity with a <see cref="VersionAttribute"/> has neither <c>IfVersion</c> nor <c>IgnoreVersion</c>, or version 0 stands beside <see cref="IfExists"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">A condition did not hold, so nothing was deleted.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The returned item cannot be read as <typeparamref name="T"/>. The delete has happened.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<WriteResult<T>> SendAsync(CancellationToken cancellationToken = default) =>
        WriteRules.Settle(Enter()).SendAsync<DeleteOperation<T>, WriteResult<T>>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command, for <c>await table.Delete(pk, sk)</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<WriteResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => WriteRules.Settle(Enter()).RenderJson();

    /// <summary>Uses up this handle for a method outside the type, which adds to the state and makes the next handle.</summary>
    /// <returns>The state.</returns>
    internal CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
