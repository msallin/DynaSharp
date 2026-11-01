using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// A DynamoDB table, and the entry to everything that reads and writes it. <c>Table&lt;Item&gt;</c> is the table without a schema and
/// <c>Table&lt;Order&gt;</c> the same table seen as a mapped entity. They are one engine: <typeparamref name="T"/> decides only how an item is
/// written and read.
/// </summary>
/// <remarks>
/// <para>
/// Every method starts a command, which is sent with <c>SendAsync</c>: <c>await shop.Get("USER#1", "PROFILE").Consistent().SendAsync(ct);</c>. Each
/// verb is the DynamoDB operation of that name, with its semantics: <see cref="Put"/> replaces a whole item, <c>Update</c> creates an item
/// that does not exist unless told otherwise, and nothing is read before it is written.
/// </para>
/// <para>
/// A table is a small object that holds what never changes, so hold it in a field; it is immutable and thread-safe. It makes no request of its
/// own, so opening a table that does not exist succeeds, and the first command fails. The table is sealed and has no interface: the test seam
/// is the <see cref="HttpMessageHandler"/> of the client, which tests the serialization too.
/// </para>
/// </remarks>
/// <typeparam name="T">How items are written and read.</typeparam>
[DebuggerDisplay("{Name,nq}")]
public sealed class Table<T>
    where T : IDynamoDbDocument<T>
{
    private ConcurrentDictionary<Type, object>? _views;

    internal Table(TableModel model)
    {
        Model = model;
    }

    /// <summary>Gets the name of the table, or its ARN.</summary>
    public string Name => Model.Name;

    /// <summary>Gets the attribute name of the partition key.</summary>
    public string PartitionKeyName => Model.PartitionKeyName;

    /// <summary>Gets the attribute name of the sort key, or <see langword="null"/> if the table has none.</summary>
    public string? SortKeyName => Model.SortKeyName;

    /// <summary>Gets what a command starts from.</summary>
    internal TableModel Model { get; }

    /// <summary>
    /// Gets the same table as seen by an entity type, as a single table holds several types. The key names come from the entity. The result is
    /// cached per type, so calling this inline costs a dictionary lookup and no allocation.
    /// </summary>
    /// <typeparam name="TOther">The entity. A nested document has no key, so it does not fit here, which the constraint enforces.</typeparam>
    /// <returns>The table.</returns>
    public Table<TOther> Of<TOther>()
        where TOther : IDynamoDbEntity<TOther>
    {
        var views = LazyInitializer.EnsureInitialized(ref _views, static () => new ConcurrentDictionary<Type, object>());
        if (views.TryGetValue(typeof(TOther), out var view))
        {
            return (Table<TOther>)view;
        }

        return (Table<TOther>)views.GetOrAdd(typeof(TOther), DynamoDbClient.CreateTable<TOther>(Model.Core, Name));
    }

    /// <summary>
    /// Gets a secondary index, naming its keys. An index is read through its own type, which has no writes. Hold it in a field.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="partitionKey">The attribute name of the partition key of the index.</param>
    /// <param name="sortKey">The attribute name of the sort key of the index, or <see langword="null"/> if it has none.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="partitionKey"/> is <see langword="null"/> or empty.</exception>
    public Index<T> Index(string name, string partitionKey, string? sortKey = null) => new(this, name, partitionKey, sortKey);

    /// <summary>Starts a get of one item by its key.</summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public GetCommand<T> Get(AttributeValue partitionKey, AttributeValue sortKey = default) =>
        new(Model.Begin(partitionKey, sortKey), Model);

    /// <summary>Starts a get of one item by a generated key, which costs no string: <c>customers.Get(Customer.Keys.Of("alice"))</c>.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public GetCommand<T> Get<TKey>(TKey key)
        where TKey : IKey<T> => new(BeginWithKey(key), Model);

    /// <summary>
    /// Starts a put of a whole item. The item is written to the request here and now, so changing it afterwards changes nothing. A put replaces
    /// everything under the key; <see cref="PutCommand{T}.IfNotExists"/> makes it a create. An entity with a <see cref="VersionAttribute"/> adds the condition on its version and stores
    /// the next one, see there, unless <see cref="TableExtensions.IgnoreVersion{T}(PutCommand{T})"/> says otherwise.
    /// </summary>
    /// <param name="item">The item, which must hold the key attributes.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public PutCommand<T> Put(T item)
    {
        Argument.ThrowIfNull(item);

        var state = Model.Begin();
        WriteRules.WriteItem(state, item);
        return new(state, Model);
    }

    /// <summary>
    /// Starts an update of one item by its key. It creates the item if none has the key, unless <see cref="UpdateCommand{T}.IfExists"/> says otherwise, so an update of an entity with a
    /// discriminator gives a new item its discriminator, see <see cref="UpdateCommand{T}"/>.
    /// </summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public UpdateCommand<T> Update(AttributeValue partitionKey, AttributeValue sortKey = default) =>
        new(WriteRules.ExpectUpdate(Model.Begin(partitionKey, sortKey)), Model);

    /// <summary>Starts an update of one item by a generated key, which is <see cref="Update(AttributeValue, AttributeValue)"/> at the cost of no string.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public UpdateCommand<T> Update<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectUpdate(BeginWithKey(key)), Model);

    /// <summary>Starts a delete of one item by its key. Deleting what does not exist is no error unless <see cref="DeleteCommand{T}.IfExists"/> asks for one.</summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public DeleteCommand<T> Delete(AttributeValue partitionKey, AttributeValue sortKey = default) =>
        new(WriteRules.ExpectVersion(Model.Begin(partitionKey, sortKey)), Model);

    /// <summary>Starts a delete of one item by a generated key.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public DeleteCommand<T> Delete<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectVersion(BeginWithKey(key)), Model);

    /// <summary>
    /// Starts a query of one partition: <c>await foreach (var order in orders.Query(pk, SortKey.BeginsWith("ORDER#")).ItemsAsync(ct))</c>. A query of a table of an entity
    /// reads that entity, even from a partition that holds several types, see <see cref="QueryCommand{T}"/>.
    /// </summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the table has none.</exception>
    public QueryCommand<T> Query(AttributeValue partitionKey, SortKey sortKey = default) => new(Model.BeginQuery(partitionKey, sortKey), Model);

    /// <summary>
    /// Starts a query of several partitions, merged by sort key, for write shards and time buckets: <c>orders.Query(shards, SortKey.Ge(from)).ItemsAsync(ct)</c>. It costs at least one
    /// read per partition, see <see cref="FanOutQueryCommand{T}"/>.
    /// </summary>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, which the message places by position, or there is a condition on a sort key and the table has none.</exception>
    public FanOutQueryCommand<T> Query(IEnumerable<AttributeValue> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

    /// <summary>Starts a query of several partitions by their string keys, which is <see cref="Query(IEnumerable{AttributeValue}, SortKey)"/> for write shards: <c>orders.Query(["SHARD#0", "SHARD#1"])</c>.</summary>
    /// <param name="partitionKeys">The partitions, read here and not kept. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    public FanOutQueryCommand<T> Query(scoped ReadOnlySpan<string> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

    /// <summary>
    /// Starts a query of several partitions by string keys that are computed, which is <see cref="Query(IEnumerable{AttributeValue}, SortKey)"/> for a list of shards:
    /// <c>orders.Query(Enumerable.Range(0, 8).Select(static shard => Order.Keys.Pk.Format(shard)))</c>.
    /// </summary>
    /// <remarks>This overload yields to every other, so a call binds to it only when no other applies. That takes C# 13: a compiler below it ignores the priority, and a <c>string[]</c> needs <c>AsSpan()</c> there.</remarks>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    [OverloadResolutionPriority(-1)]
    public FanOutQueryCommand<T> Query(IEnumerable<string> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

    /// <summary>Starts a scan of the whole table, which charges for every item in it, see <see cref="ScanCommand{T}"/>. A scan of a table of an entity gives that entity's items.</summary>
    /// <returns>The command.</returns>
    public ScanCommand<T> Scan() => new(Model.BeginScan(), Model);

    /// <summary>Reads the description of the table: status, key schema, indexes and approximate counts, which DynamoDB updates about every six hours.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description. A response without one gives the name of this table and <see cref="TableStatus.Unknown"/>.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist, or it was created a moment ago: this call is eventually consistent.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> DescribeAsync(CancellationToken cancellationToken = default) =>
        TableAdministration.DescribeAsync(Model, cancellationToken);

    /// <summary>Waits until the table is <c>ACTIVE</c>, for at most five minutes. See <see cref="WaitUntilActiveAsync(TimeSpan, CancellationToken)"/>.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TimeoutException">The table was not active in time.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns an error other than <see cref="ResourceNotFoundException"/>; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> WaitUntilActiveAsync(CancellationToken cancellationToken = default) =>
        TableAdministration.WaitUntilActiveAsync(Model, TableAdministration.DefaultActiveTimeout, cancellationToken);

    /// <summary>
    /// Waits until the table is <c>ACTIVE</c>, polling <c>DescribeTable</c> at once and then at growing intervals from 100 ms to 5 s, on the
    /// <see cref="DynamoDbClientOptions.TimeProvider"/> of the client, so a test with a fake clock does not wait.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wait is bounded: the last poll starts at the latest when <paramref name="timeout"/> is up, and then the call throws, whatever the status is. A poll
    /// itself takes as long as a call takes, up to <see cref="DynamoDbClientOptions.AttemptTimeout"/> per attempt, so the call can return that much later.
    /// With a timeout of zero it looks once.
    /// </para>
    /// <para>
    /// A table that does not exist counts as not active yet, because <c>DescribeTable</c> right after <c>CreateTable</c> can miss a new table. So a wrong name shows
    /// up as a timeout, whose <see cref="Exception.InnerException"/> is the <see cref="ResourceNotFoundException"/>. A status that never turns active, such as
    /// <c>DELETING</c>, runs into the timeout too. Global secondary indexes are not waited for: a table is active while an index still builds.
    /// </para>
    /// </remarks>
    /// <param name="timeout">How long to wait. <see cref="Timeout.InfiniteTimeSpan"/> is not a bound and is rejected.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative.</exception>
    /// <exception cref="TimeoutException">The table was not active in time.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns an error other than <see cref="ResourceNotFoundException"/>; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> WaitUntilActiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        TableAdministration.WaitUntilActiveAsync(Model, timeout, cancellationToken);

    /// <summary>
    /// Starts the creation of this table from what its entity declares: <c>await orders.CreateTable().Including&lt;Customer&gt;().SendAsync(ct);</c>. One entity does not describe a single table, whose indexes are
    /// the union over its entities, so <see cref="CreateTableCommand.Including{TOther}"/> names the others. A table without an entity starts from the key names it was opened with. See <see cref="CreateTableCommand"/>.
    /// </summary>
    /// <returns>The command, which sends nothing until <see cref="CreateTableCommand.SendAsync"/>.</returns>
    public CreateTableCommand CreateTable() => new(Model, typeof(T).ToString());

    /// <summary>
    /// Deletes this table and <b>all the items in it</b> (<c>DeleteTable</c>), which cannot be undone except from a backup. To delete one item, see <see cref="Delete(AttributeValue, AttributeValue)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table is gone only some time later, and this call does not wait for that: it returns the description of a table that is normally <see cref="TableStatus.Deleting"/>, and while it is, creating a table
    /// of that name fails with <see cref="ResourceInUseException"/>. It is gone when <see cref="DescribeAsync"/> throws <see cref="ResourceNotFoundException"/>.
    /// </para>
    /// <para>
    /// A table with deletion protection cannot be deleted. A repeat after a lost response can fail with <see cref="ResourceNotFoundException"/> or <see cref="ResourceInUseException"/>, because the table is
    /// already going away. The stream of the table stays readable for 24 hours.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the call. A deletion DynamoDB accepted stays accepted.</param>
    /// <returns>The description of the table as it goes away. A response without one gives the name of this table and <see cref="TableStatus.Unknown"/>.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ResourceInUseException">The table is being created or updated, or is already being deleted.</exception>
    /// <exception cref="LimitExceededException">The account creates or deletes tables too fast.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table has deletion protection.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> DeleteTableAsync(CancellationToken cancellationToken = default) => TableAdministration.DeleteAsync(Model, cancellationToken);

    /// <summary>
    /// Starts a batch get of many items of this table by their keys (<c>BatchGetItem</c>), of any number: the command cuts them into requests of 100 and
    /// asks again for what DynamoDB left unprocessed. Items come back in no order, and it is not a snapshot.
    /// </summary>
    /// <returns>The command.</returns>
    public BatchGetCommand<T> BatchGet() => new(Model.BeginActions(), Model);

    /// <summary>
    /// Starts a batch of puts and deletes (<c>BatchWriteItem</c>), of any number and on any tables: the command cuts them into requests that DynamoDB takes and
    /// sends again what it left unprocessed. A batch is not atomic, and has no conditions.
    /// </summary>
    /// <returns>The command.</returns>
    public BatchWriteCommand<T> BatchWrite() => new(Model.BeginActions(), Model);

    private CommandState BeginWithKey<TKey>(TKey key)
        where TKey : IKey<T>
    {
        Argument.ThrowIfNull(key);

        var state = Model.Begin();
        key.WriteKey(state.StartKey());
        state.EndKey();
        return state;
    }
}
