using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An <c>UpdateItem</c> under construction. It changes single attributes, and it creates the item if none has the key, unless <see cref="IfExists"/>
/// says otherwise. Chain the changes and options in any order and end with <see cref="SendAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// Every attribute name becomes a placeholder, so reserved words such as <c>status</c> need no care. One attribute can appear in one action of
/// an update only; DynamoDB rejects the request otherwise.
/// </para>
/// <para>
/// An update of an entity with a discriminator always ends its <c>SET</c> with <c>_type = if_not_exists(_type, value)</c>, because the update may be what creates the item, and a typed
/// read filters on it. A new item gets the type, and an item that exists keeps the one it has, so an update through the wrong table at a key held by another type does not change that type.
/// It costs a name and a value like any other action, and <c>UPDATED_NEW</c> returns it. A key of the table is left out, because no update can write one, and so is an attribute the update
/// has an action of its own on, such as <c>.SetIfNotExists("_type", value)</c>.
/// </para>
/// <para>
/// An index of the entity whose keys are all literals holds every item, and no property can bring an item in, so an update writes those keys the same way, before the discriminator. An
/// index with a key made of properties is written only by an update that sets those properties, with a key of literals beside them in the same <c>SET</c>, so that an update that does not
/// touch it adds nothing to it.
/// </para>
/// <para>See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</para>
/// </remarks>
/// <typeparam name="T">How the returned item is read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct UpdateCommand<T> : IConditional<UpdateCommand<T>>, IUpdatable<UpdateCommand<T>>, IRetryable<UpdateCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "UpdateItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal UpdateCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated expression.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(UpdateCommand<T> command) => new(command._state, command._version, Operation, command._table);

    /// <inheritdoc/>
    public UpdateCommand<T> Set(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.Set<AttributeValue, AttributeValueConverter>(path, value);
        return new(state, state.Table);
    }

    /// <summary>Sets a number attribute, creating it if it does not exist (<c>SET</c>).</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path. The parent of a nested path must exist.</param>
    /// <param name="value">The number.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> Set<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.Set<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.Set<TValue, TConverter>(state, attribute.Path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue> => value is { } known ? Set(attribute, known) : Remove(attribute);

    /// <summary>
    /// Adds a <c>SET</c> action in the grammar of DynamoDB, for what the other forms do not cover, such as arithmetic and <c>list_append</c>:
    /// <c>.Set($"{Attr("total")} = {Attr("total")} + {5}")</c>. An attribute in a hole stands for its name and anything else is a value.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> Set([InterpolatedStringHandlerArgument("")] ExpressionHandler action)
    {
        var state = Enter();
        state.Expressions.CommitPendingSet();
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> SetIfNotExists(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.SetIfNotExists<AttributeValue, AttributeValueConverter>(path, value);
        return new(state, state.Table);
    }

    /// <summary>Sets a number attribute unless it exists (<c>SET a = if_not_exists(a, value)</c>).</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> SetIfNotExists<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.SetIfNotExists<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> SetIfNotExists<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.NoteUnknownValue(state, attribute.Path);
        state.Expressions.SetIfNotExists<TValue, TConverter>(attribute.Path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Add(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.Add<AttributeValue, AttributeValueConverter>(path, value);
        return new(state, state.Table);
    }

    /// <summary>
    /// Adds to a number (<c>ADD</c>). An attribute that does not exist starts from zero. A retry after a lost response repeats the addition, so
    /// a counter that must be exact needs <see cref="MaxAttempts"/> of 1.
    /// </summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number, which may be negative.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> Add<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.Add<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Add<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>, IAddableConverter
    {
        var state = Enter();
        WriteRules.NoteUnknownValue(state, attribute.Path);
        state.Expressions.Add<TValue, TConverter>(attribute.Path, value);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Delete(string path, AttributeValue elements)
    {
        var state = Enter();
        state.Expressions.Delete<AttributeValue, AttributeValueConverter>(path, elements);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Delete<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue elements)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        state.Expressions.Delete<TValue, TConverter>(attribute.Path, elements);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Remove(string path)
    {
        var state = Enter();
        state.Expressions.Remove(path);
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> Remove<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.Remove(state, attribute.Path);
        return new(state, state.Table);
    }

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what struct conditions do not cover:
    /// <c>.If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) &lt; {10}")</c>. An attribute in a hole stands for its name and anything
    /// else is a value. The update happens only if the condition holds, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Condition);
        return new(state, state.Table);
    }

    /// <summary>Adds a condition. The update happens only if it holds, and a second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public UpdateCommand<T> If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        return new(state, state.Table);
    }

    /// <summary>Updates only if no item has this key (<c>attribute_not_exists</c> on the partition key), so that the update can only create.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> IfNotExists()
    {
        var state = Enter();
        state.Expressions.AppendItemExists(state.Table.PartitionKeyName, exists: false);
        return new(state, state.Table);
    }

    /// <summary>Updates only if an item has this key (<c>attribute_exists</c> on the partition key), which is how an update stops being an upsert.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> IfExists()
    {
        var state = Enter();
        state.Expressions.AppendItemExists(state.Table.PartitionKeyName, exists: true);
        return new(state, state.Table);
    }

    /// <summary>Returns the item as it was before the update in <see cref="WriteResult{T}.Attributes"/> (<c>ALL_OLD</c>). There is none if the update created it.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> ReturnOld()
    {
        var state = Enter();
        state.ReturnValues = ReturnValue.AllOld;
        return new(state, state.Table);
    }

    /// <summary>Returns the whole item as it is after the update in <see cref="WriteResult{T}.Attributes"/> (<c>ALL_NEW</c>).</summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> ReturnNew()
    {
        var state = Enter();
        state.ReturnValues = ReturnValue.AllNew;
        return new(state, state.Table);
    }

    /// <summary>
    /// Ends the chain with one attribute as the result, for example a counter after <c>Add</c> (<c>UPDATED_NEW</c>). The result is the value, not a
    /// <see cref="WriteResult{T}"/>, and only clauses that come before this are possible. The update must change the attribute, or DynamoDB does
    /// not return it, and then sending throws <see cref="DynamoDbMappingException"/>, with the update applied.
    /// </summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be returned: DynamoDB returns a shortened list, in which its position is not known.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The path of the attribute has a list index.</exception>
    public ReturnCommand<TValue, TConverter> Return<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue> => Returning<TValue, TConverter>(attribute.Path);

    /// <summary>
    /// Ends the chain with one attribute as the result, see <see cref="Return{TValue, TConverter}(Attr{TValue, TConverter})"/>, as an
    /// <see cref="AttributeValue"/>.
    /// </summary>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The path of the attribute has a list index.</exception>
    public ReturnCommand<AttributeValue, AttributeValueConverter> Return(Attr attribute) => Returning<AttributeValue, AttributeValueConverter>(attribute.Path);

    /// <summary>
    /// Puts the existing item on <see cref="ConditionalCheckFailedException.Item"/> when a condition fails, which saves a read to find out why. It
    /// costs no extra capacity.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> ReturnOldOnFailure()
    {
        var state = Enter();
        state.ReturnOnFailure = ReturnValuesOnConditionCheckFailure.AllOld;
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="WriteResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>Asks for the size estimate of the item collection, which goes to <see cref="WriteResult{T}.ItemCollectionMetrics"/>. DynamoDB has it for tables with a local secondary index.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public UpdateCommand<T> ReturnMetrics()
    {
        var state = Enter();
        state.ReturnMetrics = ReturnItemCollectionMetrics.Size;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public UpdateCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>Sends the command. A retry repeats the update, so one that is not idempotent, such as <c>Add</c>, is guarded by a condition or by <see cref="MaxAttempts"/>.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">
    /// The command was already sent or extended, an entity with a <see cref="VersionAttribute"/> has neither <c>IfVersion</c> nor <c>IgnoreVersion</c>, or a change by handle cannot
    /// leave a key consistent, see <see cref="IUpdatable{TSelf}.Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ConditionalCheckFailedException">A condition did not hold, so nothing was written.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The returned item cannot be read as <typeparamref name="T"/>. The write has happened.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<WriteResult<T>> SendAsync(CancellationToken cancellationToken = default) =>
        WriteRules.Settle(Enter()).SendAsync<UpdateOperation<T>, WriteResult<T>>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command, for <c>await table.Update(pk, sk).Set("a", 1)</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<WriteResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or extended.</exception>
    public string ToRequestJson() => WriteRules.Settle(Enter()).RenderJson();

    /// <summary>Uses up this handle for a method outside the type, which adds to the state and makes the next handle.</summary>
    /// <returns>The state.</returns>
    internal CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    private ReturnCommand<TValue, TConverter> Returning<TValue, TConverter>(string attribute)
        where TConverter : IDynamoDbConverter<TValue>
    {
        if (attribute.Contains('[', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The attribute path \"{attribute}\" has a list index, and a single list element cannot be returned.", nameof(attribute));
        }

        var state = Enter();
        state.ReturnValues = ReturnValue.UpdatedNew;
        state.ReturnPath = attribute;
        return new(state, state.Table);
    }
}
