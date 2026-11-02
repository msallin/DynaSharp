using System.Diagnostics;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A <c>TransactWriteItems</c> between its actions: up to 100 puts, updates, deletes and condition checks, on any tables, that take effect together or
/// not at all. The verbs are in <see cref="TransactWriteExtensions"/>, and each returns a step with the clauses of its own action and the verbs of the
/// next one.
/// </summary>
/// <remarks>
/// <para>
/// A step converts to this type, which uses the step up, and that is what makes a loop possible:
/// <c>TransactWriteCommand tx = client.TransactWrite(); foreach (var order in orders) { tx = tx.Put(table, order).IfNotExists(); } await tx.SendAsync(ct);</c>. A local of this type holds the
/// chain, as any step does, and is built up and sent without an <c>await</c> in between. This type has no clause of an action, so the conversion ends what can be said about the action
/// before it. Sending a transaction that got no action throws <see cref="InvalidOperationException"/>, so a loop that may add nothing skips the send.
/// </para>
/// <para>
/// DynamoDB cancels the whole transaction, with a <see cref="TransactionCanceledException"/>, when a condition of any action fails or an item takes part in
/// another transaction. A write in a transaction costs twice a standard write, for the prepare and the commit, and a transaction holds at most 4 MB,
/// must fit one region and cannot touch one item twice.
/// </para>
/// <para>
/// A transaction is idempotent for 10 minutes: DynamoDB recognizes the <c>ClientRequestToken</c>, and answers a repeat with the outcome of the first, without
/// doing it again. Each send generates one token and every retry of the client carries it, so a lost response is safe. To make a retry of your own
/// safe too, across restarts for example, pass <see cref="TransactWriteExtensions.Token"/>. Using a token again within the 10 minutes with other actions fails
/// with an <see cref="IdempotentParameterMismatchException"/>.
/// </para>
/// <para>Telemetry has one span for the request, with all tables in <c>aws.dynamodb.table_names</c>. See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</para>
/// </remarks>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct TransactWriteCommand : ITransactWriteChain<TransactWriteCommand>, IRetryable<TransactWriteCommand>
{
    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal TransactWriteCommand(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(TransactWriteExtensions.Operation, _state, _version, _table);

    /// <inheritdoc/>
    static TransactWriteCommand ITransactWriteChain<TransactWriteCommand>.Continue(CommandState state) => new(state);

    /// <inheritdoc/>
    CommandState ITransactWriteChain<TransactWriteCommand>.Enter() => Enter();

    /// <inheritdoc/>
    public TransactWriteCommand MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state);
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, TransactWriteExtensions.Operation, _table);
}
