using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A put in a transaction: what can follow it are its condition, the verbs of the next action, and sending. Attributes of an update do not exist here, so
/// <c>.Add</c> after <c>.Put</c> does not compile. See <see cref="TransactWriteExtensions"/> for the rest.
/// </summary>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct PutStep : ITransactWriteStep<PutStep>
{
    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal PutStep(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(TransactWriteExtensions.Operation, _state, _version, _table);

    /// <summary>Converts a step for the compiler, which passes it to the handler of an interpolated condition.</summary>
    /// <param name="step">The step.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(PutStep step) => new(step._state, step._version, TransactWriteExtensions.Operation, step._table);

    /// <summary>
    /// Hands the transaction on as a <see cref="TransactWriteCommand"/>, which is what a variable holds across a loop: <c>tx = tx.Put(table, item).IfNotExists();</c>. This uses the step up like
    /// any call on it, so the command is the one handle left, and a clause of the action can no longer follow.
    /// </summary>
    /// <param name="step">The step, which this replaces.</param>
    /// <exception cref="InvalidOperationException">The step was already sent, extended or rendered.</exception>
    public static implicit operator TransactWriteCommand(PutStep step) => new(step.Enter());

    /// <inheritdoc/>
    static PutStep ITransactWriteChain<PutStep>.Continue(CommandState state) => new(state);

    /// <inheritdoc/>
    CommandState ITransactWriteChain<PutStep>.Enter() => Enter();

    /// <summary>
    /// Adds a condition in the grammar of DynamoDB, for what struct conditions do not cover. An attribute in a hole stands for its name and anything else is
    /// a value. If it does not hold, the whole transaction is cancelled, and a second condition means both must hold.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The step, which replaces this one.</returns>
    public PutStep If([InterpolatedStringHandlerArgument("")] ExpressionHandler condition)
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
    public PutStep If<TCondition>(TCondition condition)
        where TCondition : ICondition
    {
        var state = Enter();
        state.Expressions.AppendCondition(Clause.Condition, condition);
        return new(state);
    }

    /// <summary>
    /// Writes the entity of this put as it is, with no condition on its version and no new version, for imports and migrations that bring their versions along. Conditions of your own
    /// stay, and the other actions of the transaction are not touched. On a type without a <see cref="Mapping.VersionAttribute"/> it changes nothing. See <see cref="TableExtensions.IgnoreVersion{T}(PutCommand{T})"/>.
    /// </summary>
    /// <returns>The step, which replaces this one.</returns>
    public PutStep IgnoreVersion()
    {
        var state = Enter();
        state.IgnoreVersion();
        return new(state);
    }

    /// <inheritdoc/>
    public PutStep MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state);
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, TransactWriteExtensions.Operation, _table);
}
