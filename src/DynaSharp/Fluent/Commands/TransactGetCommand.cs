using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>TransactGetItems</c> under construction: up to 100 gets, of any tables and types, that see one snapshot. Chain them and end with
/// <see cref="SendAsync"/> in one expression. Each get hands out a slot, which reads its item from the result.
/// </summary>
/// <remarks>
/// <para>
/// Every get is strongly consistent, and the items are read as of one point in time, which a batch does not give. That costs twice the capacity of a
/// strongly consistent read, and DynamoDB cancels the transaction with a <see cref="TransactionCanceledException"/> when it runs into a write in progress, so
/// a read that must not fail is better off as a batch. DynamoDB limits a transaction to 4 MB of items and rejects one that holds a key twice.
/// </para>
/// <para>Telemetry has one span for the request. See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</para>
/// </remarks>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct TransactGetCommand : IRetryable<TransactGetCommand>
{
    private const string Operation = "TransactGetItems";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal TransactGetCommand(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Adds a get by a key of attribute values.</summary>
    /// <typeparam name="T">How the item is read.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    /// <exception cref="InvalidOperationException">The transaction holds 100 gets already.</exception>
    public TransactGetCommand Get<T>(Table<T> table, AttributeValue partitionKey, AttributeValue sortKey, out TransactSlot<T> slot)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        model.CheckKey(partitionKey, sortKey);
        var state = Enter();
        slot = new TransactSlot<T>(Transaction.AddAction(state, model, "{\"Get\":"u8));
        state.WriteKey(partitionKey, sortKey);
        return new(state);
    }

    /// <summary>Adds a get by a partition key, for a table without a sort key. DynamoDB rejects a key without its sort key on a table that has one.</summary>
    /// <typeparam name="T">How the item is read.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    /// <exception cref="InvalidOperationException">The transaction holds 100 gets already.</exception>
    public TransactGetCommand Get<T>(Table<T> table, AttributeValue partitionKey, out TransactSlot<T> slot)
        where T : IDynamoDbDocument<T> => Get(table, partitionKey, default, out slot);

    /// <summary>Adds a get by a generated key.</summary>
    /// <typeparam name="T">How the item is read.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The transaction holds 100 gets already.</exception>
    public TransactGetCommand Get<T, TKey>(Table<T> table, TKey key, out TransactSlot<T> slot)
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(key);

        var state = Enter();
        slot = new TransactSlot<T>(Transaction.AddAction(state, table.Model, "{\"Get\":"u8));
        key.WriteKey(state.StartKey());
        state.EndKey();
        return new(state);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="TransactGetResult.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TransactGetCommand ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state);
    }

    /// <inheritdoc/>
    public TransactGetCommand MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state);
    }

    /// <summary>Sends the transaction. A key without an item is no error: its slot reads as not found. DynamoDB rejects a transaction without gets.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="TransactionCanceledException">DynamoDB cancelled the transaction, for example for a conflict with a write in progress. It may pass on a retry of your own.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it holds a key twice.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TransactGetResult> SendAsync(CancellationToken cancellationToken = default)
    {
        var state = Enter();
        Transaction.EndActions(state);
        return state.SendActionsAsync<TransactGetOperation, TransactGetResult>(cancellationToken);
    }

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<TransactGetResult> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is meant for tests
    /// and debugging. The text contains the keys.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson()
    {
        var state = Enter();
        Transaction.EndActions(state);
        return state.RenderJson();
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
