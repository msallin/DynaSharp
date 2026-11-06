using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>PutItem</c> under construction, which replaces the whole item under its key. Chain the options and end with <see cref="SendAsync"/> in one
/// expression.
/// </summary>
/// <remarks>See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</remarks>
/// <typeparam name="T">How the item is written and read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct PutCommand<T> : IConditional<PutCommand<T>>, IRetryable<PutCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "PutItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal PutCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated condition.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(PutCommand<T> command) => new(command._state, command._version, Operation, command._table);

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what struct conditions do not cover:
    /// <c>.If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) &lt; {10}")</c>. An attribute in a hole stands for its name and anything
    /// else is a value. The put happens only if the condition holds, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Condition);
        WriteRules.NoteCondition(state);
        return new(state, state.Table);
    }

    /// <summary>Adds a condition. The put happens only if it holds, and a second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public PutCommand<T> If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        WriteRules.NoteCondition(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Puts only if no item has this key (<c>attribute_not_exists</c> on the partition key), which turns the replacing put into a create. An entity with a <see cref="VersionAttribute"/> must then
    /// hold version 0, whose condition is this one and renders once, also with <see cref="VersionMissing.Allow"/>, where this alone says that the key is free. No item can lack its key and hold a version, so the terminal throws
    /// <see cref="InvalidOperationException"/> for any other, unless <c>IgnoreVersion()</c> was called.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> IfNotExists()
    {
        var state = Enter();
        WriteRules.IfNotExists(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Puts only if an item has this key (<c>attribute_exists</c> on the partition key), which makes it a replacement that creates nothing. With <see cref="VersionMissing.Allow"/>, version 0 asks for an item that reads
    /// as 0 besides. Otherwise an entity with a <see cref="VersionAttribute"/> at version 0 says that no item
    /// has the key, so the terminal throws <see cref="InvalidOperationException"/> for it, unless <c>IgnoreVersion()</c> was called.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> IfExists()
    {
        var state = Enter();
        WriteRules.IfExists(state);
        return new(state, state.Table);
    }

    /// <summary>Returns the item this put replaced in <see cref="WriteResult{T}.Item"/> (<c>ALL_OLD</c>). There is none if no item had the key.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> ReturnOld()
    {
        var state = Enter();
        state.ReturnValues = ReturnValue.AllOld;
        return new(state, state.Table);
    }

    /// <summary>
    /// Puts the existing item on <see cref="ConditionalCheckFailedException.Item"/> when a condition fails, or on <see cref="WriteResult{T}.Item"/> with <see cref="TrySendAsync"/>, which saves a read
    /// to find out why. It costs no extra capacity.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> ReturnOldOnFailure()
    {
        var state = Enter();
        state.ReturnOnFailure = ReturnValuesOnConditionCheckFailure.AllOld;
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="WriteResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>Asks for the size estimate of the item collection, which goes to <see cref="WriteResult{T}.ItemCollectionMetrics"/>. DynamoDB has it for tables with a local secondary index.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public PutCommand<T> ReturnMetrics()
    {
        var state = Enter();
        state.ReturnMetrics = ReturnItemCollectionMetrics.Size;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public PutCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>
    /// Sends the command. A retry repeats the same put, which is safe unless <see cref="ReturnOld"/> or a condition depends on the first one having failed: after a lost response the retry
    /// finds the item the first one wrote, so <see cref="IfNotExists"/> fails, <see cref="ReturnOld"/> returns that item, and <see cref="IfExists"/> or an <c>If</c> judges it instead of what
    /// was there before. For an entity with a <see cref="VersionAttribute"/> the same goes: the retry finds the version it wrote and fails its own condition.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it asks for <see cref="IfNotExists"/> of an entity with a version above 0 or for <see cref="IfExists"/> of one at version 0 without <see cref="VersionMissing.Allow"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">A condition did not hold, so nothing was written.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The returned item cannot be read as <typeparamref name="T"/>. The write has happened.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<WriteResult<T>> SendAsync(CancellationToken cancellationToken = default) =>
        Enter().Settle().SendAsync<PutOperation<T>, WriteResult<T>>(cancellationToken);

    /// <summary>
    /// Sends the command as <see cref="SendAsync"/> does, and takes a failed condition as an answer instead of throwing <see cref="ConditionalCheckFailedException"/>: <see cref="WriteResult{T}.ConditionMet"/>
    /// is then <see langword="false"/>, nothing was written, and with <see cref="ReturnOldOnFailure"/> <see cref="WriteResult{T}.Item"/> is the item the condition saw. It is for a write whose failed
    /// condition is routine, such as an event that is old news, which then costs no exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request is the one <see cref="SendAsync"/> sends, and every other error throws as there. <see cref="ReturnOldOnFailure"/> stays a choice: without it a failed condition gives no item.
    /// <see cref="ReturnOld"/> gives nothing on a failed condition, because DynamoDB returns nothing then.
    /// </para>
    /// <para>
    /// A retry of the client after a lost response meets what its first attempt wrote, and a condition that rules out the write's own result then fails. That is the same answer a second delivery of the same
    /// change gets, and on the wire the two cannot be told apart, so <see cref="WriteResult{T}.ConditionMet"/> does not say which. Compare the item of <see cref="ReturnOldOnFailure"/> with what the put sent:
    /// the same version or time means that the change is stored, by this call or an earlier one.
    /// </para>
    /// <para>
    /// Inside <c>RetryOnConflictAsync</c> a failed condition of this method is no conflict, so the delegate does not run again. The span of a failed condition is no error, and carries
    /// <c>dynasharp.condition_met</c> = <see langword="false"/>.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it asks for <see cref="IfNotExists"/> of an entity with a version above 0 or for <see cref="IfExists"/> of one at version 0 without <see cref="VersionMissing.Allow"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The returned item, or the item a failed condition saw, cannot be read as <typeparamref name="T"/>. The write has happened only in the first case.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<WriteResult<T>> TrySendAsync(CancellationToken cancellationToken = default) =>
        Enter().Settle().TrySendAsync<PutOperation<T>, WriteResult<T>>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command, for <c>await table.Put(item)</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<WriteResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the item.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it asks for <see cref="IfNotExists"/> of an entity with a version above 0 or for <see cref="IfExists"/> of one at version 0 without <see cref="VersionMissing.Allow"/>.</exception>
    public string ToRequestJson() => Enter().Settle().RenderJson();

    /// <summary>Uses up this handle for a method outside the type, which adds to the state and makes the next handle.</summary>
    /// <returns>The state.</returns>
    internal CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
