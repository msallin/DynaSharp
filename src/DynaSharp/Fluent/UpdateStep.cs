using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An update in a transaction: what can follow it are its changes and its condition, in any order, the verbs of the next action, and sending. One attribute
/// can appear in one change of an update only. See <see cref="TransactWriteExtensions"/> for the rest.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Delete(string, AttributeValue)"/> here removes elements from a set, which is the <c>DELETE</c> clause of the update. The verb of the next
/// action takes a table, so the two do not mix.
/// </para>
/// <para>An update of an entity with a discriminator gives a new item its type, and an item that exists keeps the one it has, as <see cref="UpdateCommand{T}"/> says.</para>
/// </remarks>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct UpdateStep : ITransactWriteStep<UpdateStep>, IUpdatable<UpdateStep>
{
    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal UpdateStep(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(TransactWriteExtensions.Operation, _state, _version, _table);

    /// <summary>Converts a step for the compiler, which passes it to the handler of an interpolated expression.</summary>
    /// <param name="step">The step.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(UpdateStep step) => new(step._state, step._version, TransactWriteExtensions.Operation, step._table);

    /// <summary>
    /// Hands the transaction on as a <see cref="TransactWriteCommand"/>, which is what a variable holds across a loop: <c>tx = tx.Update(table, key).Add(attribute, 1);</c>. This uses the step up
    /// like any call on it, so the command is the one handle left, and a clause of the action can no longer follow. What the action still owes, such as a version, is checked by the next verb.
    /// </summary>
    /// <param name="step">The step, which this replaces.</param>
    /// <exception cref="InvalidOperationException">The step was already sent, extended or rendered.</exception>
    public static implicit operator TransactWriteCommand(UpdateStep step) => new(step.Enter());

    /// <inheritdoc/>
    static UpdateStep ITransactWriteChain<UpdateStep>.Continue(CommandState state) => new(state);

    /// <inheritdoc/>
    CommandState ITransactWriteChain<UpdateStep>.Enter() => Enter();

    /// <inheritdoc/>
    public UpdateStep Set(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.Set<AttributeValue, AttributeValueConverter>(path, value);
        return new(state);
    }

    /// <summary>Sets a number attribute, creating it if it does not exist (<c>SET</c>).</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path. The parent of a nested path must exist.</param>
    /// <param name="value">The number.</param>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep Set<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.Set<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.Set<TValue, TConverter>(state, attribute.Path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Set<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue? value)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue> => value is { } known ? Set(attribute, known) : Remove(attribute);

    /// <summary>
    /// Adds a <c>SET</c> action in the grammar of DynamoDB, for what the other forms do not cover, such as arithmetic and <c>list_append</c>:
    /// <c>.Set($"{Attr("total")} = {Attr("total")} + {5}")</c>. An attribute in a hole stands for its name and anything else is a value.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep Set([InterpolatedStringHandlerArgument("")] ExpressionHandler action)
    {
        var state = Enter();
        state.Expressions.CommitPendingSet();
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep SetIfNotExists(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.SetIfNotExists<AttributeValue, AttributeValueConverter>(path, value);
        return new(state);
    }

    /// <summary>Sets a number attribute unless it exists (<c>SET a = if_not_exists(a, value)</c>).</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number.</param>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep SetIfNotExists<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.SetIfNotExists<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep SetIfNotExists<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.NoteUnknownValue(state, attribute.Path);
        state.Expressions.SetIfNotExists<TValue, TConverter>(attribute.Path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Add(string path, AttributeValue value)
    {
        var state = Enter();
        state.Expressions.Add<AttributeValue, AttributeValueConverter>(path, value);
        return new(state);
    }

    /// <summary>Adds to a number (<c>ADD</c>). An attribute that does not exist starts from zero. The token of the transaction makes a retry safe here, unlike in a single update.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="path">The name or document path.</param>
    /// <param name="value">The number, which may be negative.</param>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep Add<TNumber>(string path, TNumber value)
        where TNumber : INumberBase<TNumber>
    {
        var state = Enter();
        state.Expressions.Add<TNumber, NumberConverter<TNumber>>(path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Add<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue>, IAddableConverter
    {
        var state = Enter();
        WriteRules.NoteUnknownValue(state, attribute.Path);
        state.Expressions.Add<TValue, TConverter>(attribute.Path, value);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Delete(string path, AttributeValue elements)
    {
        var state = Enter();
        state.Expressions.Delete<AttributeValue, AttributeValueConverter>(path, elements);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Delete<TValue, TConverter>(Attr<TValue, TConverter> attribute, TValue elements)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        state.Expressions.Delete<TValue, TConverter>(attribute.Path, elements);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Remove(string path)
    {
        var state = Enter();
        state.Expressions.Remove(path);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep Remove<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = Enter();
        WriteRules.Remove(state, attribute.Path);
        return new(state);
    }

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what struct conditions do not cover. An attribute in a hole stands for its name and anything else is
    /// a value. If it does not hold, the whole transaction is cancelled, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
    {
        var state = Enter();
        state.Expressions.CommitPending(Clause.Condition);
        return new(state);
    }

    /// <summary>Adds a condition. If it does not hold, the whole transaction is cancelled, and a second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The step, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is <see langword="null"/>.</exception>
    public UpdateStep If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        return new(state);
    }

    /// <summary>
    /// Makes this update depend on the version of the item, and stores the next version. Otherwise the whole transaction is cancelled, and the marker of this action says that this was
    /// the reason. A version of 0 stands for a new item. A key does not carry a version, so an update of an entity with a <see cref="Mapping.VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion"/>, and the next verb throws <see cref="InvalidOperationException"/> without it. <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>
    /// repeats a transaction together with the read of the version.
    /// </summary>
    /// <param name="version">The version last read.</param>
    /// <returns>The step, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>, which has no next one.</exception>
    /// <exception cref="InvalidOperationException">The table of this action has no <see cref="Mapping.VersionAttribute"/>.</exception>
    public UpdateStep IfVersion(long version)
    {
        WriteRules.CheckVersion(version);
        var state = Enter();
        WriteRules.AdvanceVersion(state, version);
        return new(state);
    }

    /// <summary>Lets this update go out without the check and without a new version.</summary>
    /// <returns>The step, which replaces this one.</returns>
    public UpdateStep IgnoreVersion()
    {
        var state = Enter();
        WriteRules.IgnoreVersion(state);
        return new(state);
    }

    /// <inheritdoc/>
    public UpdateStep MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state);
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, TransactWriteExtensions.Operation, _table);
}
