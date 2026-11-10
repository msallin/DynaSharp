using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>BatchGetItem</c> under construction: many keys of its table, of any number, and gets of other tables and types that each hand out a slot. Chain the keys and
/// options and end with <see cref="SendAsync"/> in one expression.
/// </summary>
/// <example>
/// <code>
/// var batch = await orders.BatchGet()
///     .Get(Order.Keys.Of("bob", 1))
///     .Get(customers, Customer.Keys.Of("bob"), out var buyer)
///     .SendAsync(ct);
/// var customer = batch.Read(buyer).Item;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// DynamoDB takes 100 keys in a request and returns 16 MB of items in a response. The command cuts the keys into requests of 100, which run one after the
/// other, and asks again for the keys DynamoDB did not get to, so the result is complete. A batch is not one read: each key is read on its own, so
/// the items are not a snapshot, and a read that must be one is a <see cref="TransactGetCommand"/>. Items come back in no order.
/// </para>
/// <para>
/// Keys that were left unprocessed are asked for again after a backoff with jitter on <see cref="DynamoDbClientOptions.TimeProvider"/>. The batch gives
/// up after 8 requests in a row that get nothing done, or as many as <see cref="MaxStalledRounds"/> says, with <see cref="BatchGetIncompleteException{T}"/>. A
/// round that reads something, as with a response at its 16 MB, does not count against that. Any other failure after a request went through ends the batch the same way, with the failure
/// as its <see cref="Exception.InnerException"/>, and so does a timeout, a lost connection or a server error of the first request. Any other failure of the first request surfaces as it
/// is, and a cancellation stays an <see cref="OperationCanceledException"/>, which reports no progress.
/// </para>
/// <para>
/// DynamoDB rejects a request that holds one key twice with a <see cref="ValidationException"/>. The command leaves that to DynamoDB and does not look for
/// duplicates: a key that occurs twice in one request fails it, and one that occurs in two requests is read twice.
/// </para>
/// <para>
/// <b>Other tables.</b> <see cref="Get{TOther, TKey}(Table{TOther}, TKey, out BatchSlot{TOther})"/> adds a key of any table, also of another type on the same table, and its slot reads the item from
/// <see cref="BatchGetResult{T}.Read{TOther}(BatchSlot{TOther})"/>; it is not in <see cref="BatchGetResult{T}.Items"/>. Such gets share the requests of 100 keys, the second round for unprocessed keys and
/// <see cref="Consistent"/> with the keys of the table of the command. <c>Project</c> is about the table of the command only, so it rejects a get with a slot on that table, which reads another type.
/// </para>
/// <para>
/// Telemetry has one span per request, so a batch can make several, and they carry the table and the capacity of their own request. See
/// <see cref="GetCommand{T}"/> for what a command is and what it cannot do.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct BatchGetCommand<T> : IRetryable<BatchGetCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "BatchGetItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal BatchGetCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Adds a key built by the generator, such as <c>Order.Keys.Of(customerId, number)</c>.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public BatchGetCommand<T> Get<TKey>(TKey key)
        where TKey : IKey<T>
    {
        Argument.ThrowIfNull(key);
        var state = Enter();
        AddKey(state, key);
        return new(state, state.Table);
    }

    /// <summary>Adds generated keys, any number of them. They are written here and now, so the sequence is enumerated once, before anything is sent.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="keys">The keys.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> or one of them is <see langword="null"/>.</exception>
    public BatchGetCommand<T> GetAll<TKey>(IEnumerable<TKey> keys)
        where TKey : IKey<T>
    {
        ArgumentNullException.ThrowIfNull(keys);
        var state = Enter();
        foreach (var key in keys)
        {
            Argument.ThrowIfNull(key, nameof(keys));
            AddKey(state, key);
        }

        return new(state, state.Table);
    }

    /// <summary>Adds a get by a generated key of any table, whose item <paramref name="slot"/> reads from the result. The table can be another one or this one, for another type.</summary>
    /// <typeparam name="TOther">How the item is read.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The table is the one of the command and the command has a <c>Project</c>, which a read of another type cannot do with.</exception>
    public BatchGetCommand<T> Get<TOther, TKey>(Table<TOther> table, TKey key, out BatchSlot<TOther> slot)
        where TOther : IDynamoDbDocument<TOther>
        where TKey : IKey<TOther>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(key);
        ThrowIfProjected(table.Model);
        var state = Enter();
        var offset = StartSlot(state, table.Model);
        state.BodyJson.WriteStartObject();
        key.WriteKey(new DynamoDbWriter(state.BodyJson));
        state.EndKey();
        slot = new BatchSlot<TOther>(EndSlot(state, table.Model, offset));
        return new(state, state.Table);
    }

    /// <summary>Adds a get on a table without a schema, by the values of the key, whose item <paramref name="slot"/> reads from the result.</summary>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
    /// <exception cref="InvalidOperationException">The table is the one of the command and the command has a <c>Project</c>, which a read of another type cannot do with.</exception>
    public BatchGetCommand<T> Get(Table<Item> table, AttributeValue partitionKey, AttributeValue sortKey, out BatchSlot<Item> slot)
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        model.CheckKey(partitionKey, sortKey);
        ThrowIfProjected(model);
        var state = Enter();
        var offset = StartSlot(state, model);
        state.WriteKeyObject(model, partitionKey, sortKey);
        slot = new BatchSlot<Item>(EndSlot(state, model, offset));
        return new(state, state.Table);
    }

    /// <summary>Adds a get on a table without a schema and without a sort key, by the value of the partition key, whose item <paramref name="slot"/> reads from the result.</summary>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="slot">What reads the item from the result.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is empty, or the table has a sort key.</exception>
    /// <exception cref="InvalidOperationException">The table is the one of the command and the command has a <c>Project</c>, which a read of another type cannot do with.</exception>
    public BatchGetCommand<T> Get(Table<Item> table, AttributeValue partitionKey, out BatchSlot<Item> slot) => Get(table, partitionKey, default, out slot);

    /// <summary>Asks for strongly consistent reads, which see every write that succeeded before them and cost twice the capacity. Reads are eventually consistent otherwise.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public BatchGetCommand<T> Consistent()
    {
        var state = Enter();
        state.Read.ConsistentRead = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads only some attributes. It saves bandwidth, not capacity: DynamoDB charges for the whole items. A read of an entity needs its discriminator and its
    /// keys, so they join the projection on their own, and it needs whatever else the entity requires, so a projection is mostly for <see cref="Item"/>.
    /// </summary>
    /// <param name="paths">The names or document paths. Reserved words need no care, and a second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    /// <exception cref="InvalidOperationException">The command has a get with a slot on its own table, which reads another type and needs the whole item.</exception>
    public BatchGetCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        ThrowIfSlotOnTable();
        var state = Enter();
        state.Expressions.Projection.Add(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The command has a get with a slot on its own table, which reads another type and needs the whole item.</exception>
    public BatchGetCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        ThrowIfSlotOnTable();
        var state = Enter();
        state.Expressions.Projection.Add(attributes);
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="BatchGetResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public BatchGetCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>
    /// Sets the number of attempts of each request including the first, in place of <see cref="DynamoDbClientOptions.MaxAttempts"/>. It is about one request and says nothing about keys that
    /// come back unanswered, which <see cref="MaxStalledRounds"/> bounds.
    /// </summary>
    /// <param name="attempts">The number of attempts.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/> is below 1.</exception>
    public BatchGetCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>
    /// Sets how many requests in a row may get nothing done before the batch gives up with <see cref="BatchGetIncompleteException{T}"/>, in place of 8. A request that reads something starts the
    /// count again, so this bounds a batch that is stuck and not a long one. With <c>1</c> the batch gives up at the first request that reads nothing, without a wait, which suits a caller that
    /// has a backoff of its own.
    /// </summary>
    /// <param name="rounds">The number of requests.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rounds"/> is below 1.</exception>
    public BatchGetCommand<T> MaxStalledRounds(int rounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        var state = Enter();
        state.MaxStalledRounds = rounds;
        return new(state, state.Table);
    }

    /// <summary>Sends the batch, as one request per 100 keys. A batch without keys sends nothing and returns an empty result, and a key without an item is no error.</summary>
    /// <param name="cancellationToken">Cancels the batch, also while it waits between rounds.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="BatchGetIncompleteException{T}">
    /// DynamoDB left keys unanswered after every round, a table stayed throttled on the last attempt of a request, a request timed out, lost its connection or met a server error on its
    /// last attempt, or any other failure came after a request went through. The failure is then its <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the first request, for example because it holds a key twice; the exception shows the projection.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist, as the first request finds.</exception>
    /// <exception cref="DynamoDbMappingException">An item of the first response cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error on the first request that repeating cannot fix.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. This does not report progress: what was read before is dropped.</exception>
    public ValueTask<BatchGetResult<T>> SendAsync(CancellationToken cancellationToken = default)
    {
        var state = Prepare();
        state.Actions.Results = new List<T>(state.Actions.Count);
        return BatchDriver.SendAsync<BatchGetOperation<T>, BatchGetResult<T>>(state, cancellationToken);
    }

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<BatchGetResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body of the first request this command would send, as DynamoDB JSON, without sending it. It ends the command,
    /// and is meant for tests and debugging. The text contains the keys.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => BatchDriver.RenderJson<BatchGetOperation<T>, BatchGetResult<T>>(Prepare());

    internal CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    // The table of the command, for a check of an argument that must leave the command as it was when it throws.
    internal TableModel CheckedTable() => CommandState.Check(_state, _version, Operation, _table).Table;

    private static void AddKey<TKey>(CommandState state, TKey key)
        where TKey : IKey<T>
    {
        state.Actions.Add(state.Table, state.Body.WrittenCount);
        var json = state.BodyJson;
        json.WriteStartObject();
        key.WriteKey(new DynamoDbWriter(json));
        state.EndKey();
    }

    private static int StartSlot(CommandState state, TableModel table)
    {
        var offset = state.Body.WrittenCount;
        state.Actions.Add(table, offset);
        return offset;
    }

    private static int EndSlot(CommandState state, TableModel table, int offset)
    {
        var actions = state.Actions;
        actions.Slots ??= new BatchSlots();
        return actions.Slots.Add(table, state.Body.WrittenSpan[offset..], actions.Count - 1);
    }

    // A projection is written once, for the table of the command, so a read of another type on that table would get it too. Checked before the command is entered, so that
    // the call leaves the command as it was.
    private void ThrowIfProjected(TableModel table)
    {
        var state = CommandState.Check(_state, _version, Operation, _table);
        if (table.Name == state.Table.Name && state.Expressions[Clause.Projection].WrittenCount > 0)
        {
            throw new InvalidOperationException($"This batch reads {state.Table.Name} with a projection, which a get with a slot on that table would get too. Read the other type in a batch of its own, or leave out Project.");
        }
    }

    private void ThrowIfSlotOnTable()
    {
        var state = CommandState.Check(_state, _version, Operation, _table);
        if (state.Actions.Slots?.TableNamed(state.Table.Name) is not null)
        {
            throw new InvalidOperationException($"This batch has a get with a slot on {state.Table.Name}, which reads another type and needs the whole item. Read that type in a batch of its own, or leave out Project.");
        }
    }

    private CommandState Prepare()
    {
        var state = Enter();
        state.Table.AddRequiredNames(state);
        return state;
    }
}
