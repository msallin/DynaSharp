using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>BatchWriteItem</c> under construction: puts and deletes of any number, also of other types and other tables in the same batch, as in
/// <c>shop.BatchWrite().Put(item).Delete(pk, sk).Put(orders, order)</c>. It is not atomic, runs in requests of 25 writes, sends again what DynamoDB left unprocessed, and gives up with a
/// <see cref="BatchIncompleteException"/>. Chain the writes and end with <see cref="SendAsync"/> in one expression.
/// </summary>
/// <example>
/// <code>
/// var written = await products.BatchWrite().PutAll(catalog).SendAsync(ct);
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Not atomic.</b> Each write succeeds or fails on its own, and when the batch ends early the writes before that stay applied. A batch has no
/// conditions and no update either: what needs those is a <see cref="TransactWriteCommand"/>, and costs twice the capacity.
/// </para>
/// <para>
/// <b>Requests of 25 writes.</b> DynamoDB takes 25 writes and 16 MB in a request, and an item can have 400 KB. The command cuts the batch into requests that fit, which run one after
/// the other.
/// </para>
/// <para>
/// <b>Unprocessed writes go out again.</b> That happens after a backoff with jitter on <see cref="DynamoDbClientOptions.TimeProvider"/>. A retry repeats a put or a delete, which is safe: the
/// item ends up as it was written.
/// </para>
/// <para>
/// <b>It gives up</b> after 8 requests in a row that get nothing done, or as many as <see cref="MaxStalledRounds"/> says, with a <see cref="BatchIncompleteException"/> that holds
/// what was not written. A round that writes something does not count against that. Any other failure after a request went through ends the batch the same way, with the failure
/// as its <see cref="Exception.InnerException"/>; a failure of the first request surfaces as it is, and a cancellation stays an <see cref="OperationCanceledException"/>, which reports no progress.
/// </para>
/// <para>
/// <b>A key twice.</b> DynamoDB rejects a request that holds one key twice with a <see cref="ValidationException"/>, and the command does not look for duplicates: an item can
/// hold its key anywhere, and a write costs no read of it here. So a key that occurs twice in one request fails that request, with the requests before
/// it applied and reported, and one that occurs in two requests is written twice, in order.
/// </para>
/// <para>
/// Telemetry has one span per request, so a batch can make several, and they carry the tables and the capacity of their own request. See
/// <see cref="GetCommand{T}"/> for what a command is and what it cannot do.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the table the batch was started on.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct BatchWriteCommand<T> : IRetryable<BatchWriteCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "BatchWriteItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal BatchWriteCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Adds a put of a whole item, which replaces everything under its key. The item is written to the request here and now, so changing it afterwards changes nothing.</summary>
    /// <param name="item">The item, which must hold the key attributes.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public BatchWriteCommand<T> Put(T item)
    {
        Argument.ThrowIfNull(item);
        var state = Enter();
        BatchWriteOperation.AddPut(state, state.Table, item);
        return new(state, state.Table);
    }

    /// <summary>
    /// Adds a put of each item, any number of them. The whole batch is written into memory here and now, so the sequence is enumerated once, and nothing is sent until <c>SendAsync</c>: an item that
    /// fails here leaves nothing written, and it fails the whole batch.
    /// </summary>
    /// <param name="items">The items, which must hold the key attributes.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or one of them is <see langword="null"/>.</exception>
    /// <exception cref="KeyFormatException">A key template rejects a value of an item. It names the placeholder, not the item.</exception>
    public BatchWriteCommand<T> PutAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var state = Enter();
        foreach (var item in items)
        {
            Argument.ThrowIfNull(item, nameof(items));
            BatchWriteOperation.AddPut(state, state.Table, item);
        }

        return new(state, state.Table);
    }

    /// <summary>Adds a put to another table, or of another type on the same table.</summary>
    /// <typeparam name="TOther">The type of the item.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="item">The item, which must hold the key attributes.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="item"/> is <see langword="null"/>.</exception>
    public BatchWriteCommand<T> Put<TOther>(Table<TOther> table, TOther item)
        where TOther : IDynamoDbDocument<TOther>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(item);
        var state = Enter();
        BatchWriteOperation.AddPut(state, table.Model, item);
        return new(state, state.Table);
    }

    /// <summary>Adds a delete by a generated key. Deleting what does not exist is no error.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public BatchWriteCommand<T> Delete<TKey>(TKey key)
        where TKey : IKey<T>
    {
        Argument.ThrowIfNull(key);
        var state = Enter();
        BatchWriteOperation.AddDelete(state, state.Table, key);
        return new(state, state.Table);
    }

    /// <summary>
    /// Adds a delete of each generated key, any number of them. Deleting what does not exist is no error. The keys are written here and now, so the sequence is enumerated once, before
    /// anything is sent. It takes a sequence of a generated key, such as <c>ids.Select(Order.Keys.Of)</c>, and an <see cref="IEnumerable{T}"/> of <see cref="IKey{TEntity}"/>.
    /// </summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="keys">The keys.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> or one of them is <see langword="null"/>.</exception>
    public BatchWriteCommand<T> DeleteAll<TKey>(IEnumerable<TKey> keys)
        where TKey : IKey<T>
    {
        ArgumentNullException.ThrowIfNull(keys);
        var state = Enter();
        foreach (var key in keys)
        {
            Argument.ThrowIfNull(key, nameof(keys));
            BatchWriteOperation.AddDelete(state, state.Table, key);
        }

        return new(state, state.Table);
    }

    /// <summary>Adds a delete on another table, one without a schema, by the values of the key. A table of an entity takes a generated key instead.</summary>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public BatchWriteCommand<T> Delete(Table<Item> table, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var state = Enter();
        BatchWriteOperation.AddDelete(state, table.Model, partitionKey, sortKey);
        return new(state, state.Table);
    }

    /// <summary>Adds a delete by a generated key on another table.</summary>
    /// <typeparam name="TOther">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public BatchWriteCommand<T> Delete<TOther, TKey>(Table<TOther> table, TKey key)
        where TOther : IDynamoDbDocument<TOther>
        where TKey : IKey<TOther>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(key);
        var state = Enter();
        BatchWriteOperation.AddDelete(state, table.Model, key);
        return new(state, state.Table);
    }

    /// <summary>
    /// Lets the batch write entities with a <see cref="VersionAttribute"/> as they are: a batch cannot carry a condition, so without this <see cref="SendAsync"/> throws
    /// <see cref="InvalidOperationException"/> for such an entity.
    /// </summary>
    /// <remarks>
    /// The version of what the batch writes is not checked and does not advance. It holds for the whole batch, wherever in the chain it stands. Each item is stored with the version its
    /// entity holds, so seed at 1 or above: an item stored at 0 reads as 0, which the next checked write takes for no item, and fails, unless the entity has
    /// <c>[Version(Missing = VersionMissing.Allow)]</c>.
    /// </remarks>
    /// <returns>The command, which replaces this one.</returns>
    public BatchWriteCommand<T> IgnoreVersion()
    {
        var state = Enter();
        state.IgnoreVersion();
        return new(state, state.Table);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="BatchWriteResult.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public BatchWriteCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <summary>Asks for the size estimates of the item collections, which go to <see cref="BatchWriteResult.ItemCollectionMetrics"/>. DynamoDB has them for tables with a local secondary index.</summary>
    /// <returns>The command, which replaces this one.</returns>
    public BatchWriteCommand<T> ReturnMetrics()
    {
        var state = Enter();
        state.ReturnMetrics = ReturnItemCollectionMetrics.Size;
        return new(state, state.Table);
    }

    /// <summary>
    /// Sets the number of attempts of each request including the first, in place of <see cref="DynamoDbClientOptions.MaxAttempts"/>. It is about one request and says nothing about writes that
    /// come back unprocessed, which <see cref="MaxStalledRounds"/> bounds.
    /// </summary>
    /// <param name="attempts">The number of attempts.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/> is below 1.</exception>
    public BatchWriteCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>
    /// Sets how many requests in a row may get nothing done before the batch gives up with <see cref="BatchIncompleteException"/>, in place of 8. A request that writes something starts the
    /// count again, so this bounds a batch that is stuck and not a long one. With <c>1</c> the batch gives up at the first request that writes nothing, without a wait, which suits a caller that
    /// has a backoff of its own.
    /// </summary>
    /// <param name="rounds">The number of requests.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rounds"/> is below 1.</exception>
    public BatchWriteCommand<T> MaxStalledRounds(int rounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        var state = Enter();
        state.MaxStalledRounds = rounds;
        return new(state, state.Table);
    }

    /// <summary>Sends the batch, in as many requests as it takes. A batch without writes sends nothing and returns an empty result.</summary>
    /// <param name="cancellationToken">Cancels the batch, also while it waits between rounds. What was written before stays written.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it writes an entity with a <see cref="VersionAttribute"/> without <see cref="IgnoreVersion"/>.</exception>
    /// <exception cref="BatchIncompleteException">
    /// DynamoDB left writes unprocessed after every round, a table stayed throttled on the last attempt of a request, or any other failure came after a request went through, which is then its
    /// <see cref="Exception.InnerException"/>. The exception holds what was not written.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the first request, for example because it holds a key twice, or an item above 400 KB.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist, as the first request finds.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error on the first request, a server error only after the last attempt.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled. This does not report progress: what went through before stays applied, and nothing says how much that was.</exception>
    public ValueTask<BatchWriteResult> SendAsync(CancellationToken cancellationToken = default) =>
        BatchDriver.SendAsync<BatchWriteOperation, BatchWriteResult>(Enter().Settle(), cancellationToken);

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<BatchWriteResult> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body of the first request this command would send, as DynamoDB JSON, without sending it. It ends the command,
    /// and is meant for tests and debugging. The text contains the items.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => BatchDriver.RenderJson<BatchWriteOperation, BatchWriteResult>(Enter().Settle());

    internal CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
