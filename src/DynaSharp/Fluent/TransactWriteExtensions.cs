using System.ComponentModel;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// The verbs of a transaction: what starts the next action after <see cref="TransactWriteCommand"/> and after every step, what all steps share, and the send. They
/// are extension methods because they are the same on each of them.
/// </summary>
/// <remarks>
/// Every verb that starts an action has a form with <c>out</c> that hands out a <see cref="TransactMarker"/>, which finds that action in a
/// <see cref="TransactionCanceledException"/>. On a table without a sort key, pass <see langword="default"/> for it. A verb throws
/// <see cref="InvalidOperationException"/> for action 101, because DynamoDB takes 100.
/// </remarks>
public static class TransactWriteExtensions
{
    /// <summary>The operation of a transaction, for the message about a stale handle.</summary>
    internal const string Operation = "TransactWriteItems";

    /// <summary>Adds a put of a whole item, which replaces everything under its key. The item is written to the request here and now.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="item">The item, which must hold the key attributes.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="item"/> is <see langword="null"/>.</exception>
    public static PutStep Put<TChain, T>(this TChain chain, Table<T> table, T item)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => Put(chain, table, item, out _);

    /// <summary>Adds a put of a whole item, which replaces everything under its key, and hands out its marker. The item is written to the request here and now.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="item">The item, which must hold the key attributes.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="item"/> is <see langword="null"/>.</exception>
    public static PutStep Put<TChain, T>(this TChain chain, Table<T> table, T item, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(item);

        var state = chain.Enter();
        marker = new TransactMarker(Transaction.AddAction(state, table.Model, "{\"Put\":"u8));
        WriteRules.WriteItem(state, item);
        return new PutStep(state);
    }

    /// <summary>
    /// Adds a put that exists to make something unique. DynamoDB enforces uniqueness only for a primary key, so a value that must be unique, such as an email
    /// address, gets an item of its own, whose key is that value: <c>.Unique(markers, new EmailMarker(email, id), out var taken)</c>. It is a put with
    /// <c>attribute_not_exists</c> on the partition key, so the transaction is cancelled if the item exists, and <see cref="TransactionCanceledException.ReasonFor"/>
    /// with the marker says that this was the reason. <see cref="ReturnOldOnFailure"/> also says who holds the value.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the marker item.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="item">The marker item, whose key is the value that must be unique.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="item"/> is <see langword="null"/>.</exception>
    public static PutStep Unique<TChain, T>(this TChain chain, Table<T> table, T item, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => Put(chain, table, item, out marker).IfNotExists();

    /// <summary>
    /// Adds a put of a marker item that consists of a key and nothing else, for a value that must be unique and has nothing to say beyond that:
    /// <c>.Unique(markers, EmailMarker.Keys.Of(email), out var taken)</c>. It is a put with <c>attribute_not_exists</c> on the partition key, so the transaction is cancelled if the
    /// item exists, and <see cref="TransactionCanceledException.ReasonFor"/> with the marker says that this was the reason. The item has no discriminator, so a typed read of it fails;
    /// it is there to exist, and to be deleted by its key.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key of the marker item, which holds the value that must be unique.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static PutStep Unique<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(key);

        var state = chain.Enter();
        marker = new TransactMarker(Transaction.AddAction(state, table.Model, "{\"Put\":"u8));
        key.WriteKey(state.StartItem());
        return new PutStep(state).IfNotExists();
    }

    /// <summary>Adds an update by a key of attribute values. It creates the item if none has the key, unless <see cref="IfExists"/> says otherwise.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static UpdateStep Update<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey = default)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(WriteRules.ExpectUpdate(AddAction(chain, table, "{\"Update\":"u8, partitionKey, sortKey, out _)));

    /// <summary>Adds an update by a key of attribute values, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static UpdateStep Update<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(WriteRules.ExpectUpdate(AddAction(chain, table, "{\"Update\":"u8, partitionKey, sortKey, out marker)));

    /// <summary>Adds an update by a generated key. It creates the item if none has the key, unless <see cref="IfExists"/> says otherwise.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static UpdateStep Update<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(WriteRules.ExpectUpdate(AddAction(chain, table, "{\"Update\":"u8, key, out _), key));

    /// <summary>Adds an update by a generated key, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static UpdateStep Update<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(WriteRules.ExpectUpdate(AddAction(chain, table, "{\"Update\":"u8, key, out marker), key));

    /// <summary>Adds a delete by a key of attribute values. Deleting what does not exist is no failure unless <see cref="IfExists"/> asks for one.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static ConditionStep Delete<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey = default)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(WriteRules.ExpectVersion(AddAction(chain, table, "{\"Delete\":"u8, partitionKey, sortKey, out _)));

    /// <summary>Adds a delete by a key of attribute values, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static ConditionStep Delete<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(WriteRules.ExpectVersion(AddAction(chain, table, "{\"Delete\":"u8, partitionKey, sortKey, out marker)));

    /// <summary>Adds a delete by a generated key. Deleting what does not exist is no failure unless <see cref="IfExists"/> asks for one.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static ConditionStep Delete<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(WriteRules.ExpectVersion(AddAction(chain, table, "{\"Delete\":"u8, key, out _)));

    /// <summary>Adds a delete by a generated key, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static ConditionStep Delete<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(WriteRules.ExpectVersion(AddAction(chain, table, "{\"Delete\":"u8, key, out marker)));

    /// <summary>
    /// Adds a condition check by a key of attribute values: an item that takes part in the transaction without being written, which is how a write depends on
    /// another item. The step needs a condition, and it costs a write like any other action.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static ConditionStep Check<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey = default)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(AddAction(chain, table, "{\"ConditionCheck\":"u8, partitionKey, sortKey, out _));

    /// <summary>Adds a condition check by a key of attribute values, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static ConditionStep Check<TChain, T>(this TChain chain, Table<T> table, AttributeValue partitionKey, AttributeValue sortKey, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T> => new(AddAction(chain, table, "{\"ConditionCheck\":"u8, partitionKey, sortKey, out marker));

    /// <summary>Adds a condition check by a generated key: an item that takes part in the transaction without being written. The step needs a condition.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static ConditionStep Check<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(AddAction(chain, table, "{\"ConditionCheck\":"u8, key, out _));

    /// <summary>Adds a condition check by a generated key, and hands out its marker.</summary>
    /// <typeparam name="TChain">The type of the command or step before this.</typeparam>
    /// <typeparam name="T">The type of the table.</typeparam>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="chain">The command or step before this, which this replaces.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="marker">What finds this action in a cancelled transaction.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static ConditionStep Check<TChain, T, TKey>(this TChain chain, Table<T> table, TKey key, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T> => new(AddAction(chain, table, "{\"ConditionCheck\":"u8, key, out marker));

    /// <summary>
    /// Sets the idempotency token of the transaction, in place of the one each send generates. Use one that stays the same across your own retries, such as the
    /// id of the business operation: for 10 minutes DynamoDB answers a repeat with the outcome of the first and does not do it again.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step, which this replaces.</param>
    /// <param name="token">The token, of 1 to 36 characters, which DynamoDB checks.</param>
    /// <returns>The command or step.</returns>
    /// <exception cref="ArgumentException"><paramref name="token"/> is <see langword="null"/> or empty.</exception>
    public static TChain Token<TChain>(this TChain chain, string token)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var state = chain.Enter();
        state.Actions.Token = token;
        return TChain.Continue(state);
    }

    /// <summary>Asks for the consumed capacity, which goes to <see cref="TransactWriteResult.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step, which this replaces.</param>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command or step.</returns>
    public static TChain ReturnCapacity<TChain>(this TChain chain, ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        var state = chain.Enter();
        state.ReturnCapacity = detail;
        return TChain.Continue(state);
    }

    /// <summary>Asks for the size estimates of the item collections, which go to <see cref="TransactWriteResult.ItemCollectionMetrics"/>. DynamoDB has them for tables with a local secondary index.</summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step, which this replaces.</param>
    /// <returns>The command or step.</returns>
    public static TChain ReturnMetrics<TChain>(this TChain chain)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        var state = chain.Enter();
        state.ReturnMetrics = ReturnItemCollectionMetrics.Size;
        return TChain.Continue(state);
    }

    /// <summary>
    /// Makes this action depend on no item having its key (<c>attribute_not_exists</c> on the partition key of its table). Otherwise the whole transaction is
    /// cancelled. The table is the one of the action, so it takes no argument. An entity with a <see cref="Mapping.VersionAttribute"/> must then hold version 0: the next verb or the terminal throws
    /// <see cref="InvalidOperationException"/> for any other, unless <c>IgnoreVersion()</c> was called, because no item can lack its key and hold a version.
    /// </summary>
    /// <typeparam name="TStep">The type of the step.</typeparam>
    /// <param name="step">The step, which this replaces.</param>
    /// <returns>The step.</returns>
    public static TStep IfNotExists<TStep>(this TStep step)
        where TStep : ITransactWriteStep<TStep>, allows ref struct
    {
        var state = step.Enter();
        WriteRules.IfNotExists(state);
        return TStep.Continue(state);
    }

    /// <summary>Makes this action depend on an item having its key (<c>attribute_exists</c> on the partition key of its table). Otherwise the whole transaction is cancelled.</summary>
    /// <typeparam name="TStep">The type of the step.</typeparam>
    /// <param name="step">The step, which this replaces.</param>
    /// <returns>The step.</returns>
    public static TStep IfExists<TStep>(this TStep step)
        where TStep : ITransactWriteStep<TStep>, allows ref struct
    {
        var state = step.Enter();
        state.Expressions.AppendItemExists(state.Table.PartitionKeyName, exists: true);
        return TStep.Continue(state);
    }

    /// <summary>
    /// Puts the existing item on <see cref="TransactionCanceledException.ItemFor"/> when the condition of this action fails, which saves a read to find out why. It
    /// costs no extra capacity.
    /// </summary>
    /// <typeparam name="TStep">The type of the step.</typeparam>
    /// <param name="step">The step, which this replaces.</param>
    /// <returns>The step.</returns>
    public static TStep ReturnOldOnFailure<TStep>(this TStep step)
        where TStep : ITransactWriteStep<TStep>, allows ref struct
    {
        var state = step.Enter();
        state.ReturnOnFailure = ReturnValuesOnConditionCheckFailure.AllOld;
        return TStep.Continue(state);
    }

    /// <summary>
    /// Sends the transaction. All actions take effect, or none does. The client retries with the same <c>ClientRequestToken</c>, so a retry after a lost response
    /// does not apply anything twice.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it has no action, which DynamoDB would reject.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="TransactionCanceledException">DynamoDB cancelled the transaction, and nothing was written. The exception says why, per action.</exception>
    /// <exception cref="IdempotentParameterMismatchException">The token was used within the last 10 minutes for other actions.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it touches one item twice; the exception shows the expressions, per action.</exception>
    /// <exception cref="ResourceNotFoundException">A table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public static ValueTask<TransactWriteResult> SendAsync<TChain>(this TChain chain, CancellationToken cancellationToken = default)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        var state = chain.Enter();
        TransactWriteOperation.End(state);
        return state.SendActionsAsync<TransactWriteOperation, TransactWriteResult>(cancellationToken);
    }

    /// <summary>Lets <c>await</c> send the transaction. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step.</param>
    /// <returns>The awaiter of the sent transaction.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ValueTaskAwaiter<TransactWriteResult> GetAwaiter<TChain>(this TChain chain)
        where TChain : ITransactWriteChain<TChain>, allows ref struct => chain.SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this transaction would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is meant for tests
    /// and debugging. The text contains the items and a generated token that nothing ever sends.
    /// </summary>
    /// <typeparam name="TChain">The type of the command or step.</typeparam>
    /// <param name="chain">The command or step.</param>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered, or it has no action, which DynamoDB would reject.</exception>
    public static string ToRequestJson<TChain>(this TChain chain)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
    {
        var state = chain.Enter();
        TransactWriteOperation.End(state);
        return state.RenderJson();
    }

    private static CommandState AddAction<TChain, T>(TChain chain, Table<T> table, ReadOnlySpan<byte> kind, AttributeValue partitionKey, AttributeValue sortKey, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        model.CheckKey(partitionKey, sortKey);
        var state = chain.Enter();
        marker = new TransactMarker(Transaction.AddAction(state, model, kind));
        state.WriteKey(partitionKey, sortKey);
        return state;
    }

    private static CommandState AddAction<TChain, T, TKey>(TChain chain, Table<T> table, ReadOnlySpan<byte> kind, TKey key, out TransactMarker marker)
        where TChain : ITransactWriteChain<TChain>, allows ref struct
        where T : IDynamoDbDocument<T>
        where TKey : IKey<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(key);

        var state = chain.Enter();
        marker = new TransactMarker(Transaction.AddAction(state, table.Model, kind));
        key.WriteKey(state.StartKey());
        state.EndKey();
        return state;
    }
}
