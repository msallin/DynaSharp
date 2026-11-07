using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A delete or a condition check in a transaction: what can follow it are its condition, the verbs of the next action, and sending. A check without a
/// condition is rejected by DynamoDB. See <see cref="TransactWriteExtensions"/> for the rest.
/// </summary>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct ConditionStep : ITransactWriteStep<ConditionStep>
{
    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal ConditionStep(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(TransactWriteExtensions.Operation, _state, _version, _table);

    /// <summary>Converts a step for the compiler, which passes it to the handler of an interpolated condition.</summary>
    /// <param name="step">The step.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(ConditionStep step) => new(step._state, step._version, TransactWriteExtensions.Operation, step._table);

    /// <summary>
    /// Hands the transaction on as a <see cref="TransactWriteCommand"/>, which is what a variable holds across a loop: <c>tx = tx.Delete(table, key).IfExists();</c>. This uses the step up like any
    /// call on it, so the command is the one handle left, and a clause of the action can no longer follow. What the action still owes, such as a version, is checked by the next verb.
    /// </summary>
    /// <param name="step">The step, which this replaces.</param>
    /// <exception cref="InvalidOperationException">The step was already sent, extended or rendered.</exception>
    public static implicit operator TransactWriteCommand(ConditionStep step) => new(step.Enter());

    /// <inheritdoc/>
    static ConditionStep ITransactWriteChain<ConditionStep>.Continue(CommandState state) => new(state);

    /// <inheritdoc/>
    CommandState ITransactWriteChain<ConditionStep>.Enter() => Enter();

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what the conditions of handles do not cover. An interpolated <see cref="Attr"/> stands for an attribute name and any other interpolated value for
    /// a value. If it does not hold, the whole transaction is cancelled, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The step, which replaces this one.</returns>
    public ConditionStep If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
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
    public ConditionStep If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        return new(state);
    }

    /// <summary>
    /// Makes this action depend on the item having this version. If it has another, the whole transaction is cancelled, and the marker of this action says that this was the reason.
    /// </summary>
    /// <remarks>
    /// A key does not carry a version, so a delete of an entity with a <see cref="Mapping.VersionAttribute"/> needs this or <see cref="IgnoreVersion"/>, and the next action throws
    /// <see cref="InvalidOperationException"/> without it. A condition check needs neither, and this makes it a check on the version.
    /// <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> repeats a transaction together with the read of the version.
    /// </remarks>
    /// <param name="version">The version last read. A version of 0 stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key. With <see cref="Mapping.VersionMissing.Allow"/> it stands for what reads as 0, an item without a version attribute or with 0 in it, or none.</param>
    /// <returns>The step, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>.</exception>
    /// <exception cref="InvalidOperationException">The table of this action has no <see cref="Mapping.VersionAttribute"/>.</exception>
    public ConditionStep IfVersion(long version)
    {
        WriteRules.CheckVersion(version);
        var state = Enter();
        WriteRules.IfVersion(state, version);
        return new(state);
    }

    /// <summary>Lets this delete go out without the check.</summary>
    /// <returns>The step, which replaces this one.</returns>
    public ConditionStep IgnoreVersion()
    {
        var state = Enter();
        state.IgnoreVersion();
        return new(state);
    }

    /// <inheritdoc/>
    public ConditionStep MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state);
    }

    /// <inheritdoc cref="TransactWriteCommand.ToRequestJson"/>
    public string ToRequestJson() => TransactWriteExtensions.RenderJson(Enter());

    private CommandState Enter() => CommandState.Enter(_state, _version, TransactWriteExtensions.Operation, _table);
}
