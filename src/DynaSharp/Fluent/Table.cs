using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// A DynamoDB table, and the entry to everything that reads and writes it: <c>Table&lt;Order&gt;</c> reads and writes the entity <c>Order</c>,
/// <c>Table&lt;Item&gt;</c> the same table without a schema.
/// </summary>
/// <example>
/// <code>
/// var orders = client.Table&lt;Order&gt;("Shop");
///
/// await orders.Put(order).IfNotExists().SendAsync(ct);
/// Order? found = await orders.Get(Order.Keys.Of("alice", 1)).ItemAsync(ct);
/// await foreach (var open in orders.Query(Order.Keys.Pk.Of("alice")).Filter(Order.Attrs.Status.Eq(OrderStatus.Placed)).ItemsAsync(ct))
/// {
///     Console.WriteLine(open.Number);
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Every method starts a command,</b> which is sent with <c>SendAsync</c> or one of its siblings such as <c>ItemAsync</c>: <c>await shop.Get("USER#1", "PROFILE").Consistent().SendAsync(ct);</c>.
/// Each verb is the DynamoDB operation of that name, with its semantics: <see cref="Put"/> replaces a whole item, <c>Update</c> creates an item
/// that does not exist unless told otherwise, and nothing is read before it is written.
/// </para>
/// <para>
/// <b>Hold it in a field.</b> A table holds what never changes; it is immutable and thread-safe. It makes no request of its own, so opening a table that does not exist succeeds, and
/// the first command fails. The table is sealed and has no interface: the test seam is the <see cref="HttpMessageHandler"/> of the client, which tests the serialization too.
/// </para>
/// </remarks>
/// <typeparam name="T">How items are written and read.</typeparam>
[DebuggerDisplay("{Name,nq}")]
public sealed partial class Table<T>
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

    /// <summary>
    /// Gets the client the table was opened from, which sends every command of the table. Its <see cref="DynamoDbClient.Options"/> say what the commands
    /// run with: the clock, the logger factory, the retries. A table seen through <see cref="Of{TOther}"/> and an <see cref="Index{T}"/> of it have the same client.
    /// </summary>
    /// <remarks>
    /// Code that takes a table and needs the client too, such as <c>DynaSharp.Coordination</c>, which sends transactions and reads the clock of the
    /// client, takes it from here instead of asking for both.
    /// </remarks>
    public DynamoDbClient Client => Model.Core.Client!;

    /// <summary>Gets what a command starts from.</summary>
    internal TableModel Model { get; }

    /// <summary>
    /// Views this table as another entity type, for a single table that holds several types: <c>shop.Of&lt;Order&gt;().Get(key)</c>.
    /// </summary>
    /// <remarks>Key names, indexes and the discriminator come from <typeparamref name="TOther"/>. The view is created once per type and shared, so it is fine to call this inline.</remarks>
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

    /// <summary>
    /// Starts a <c>GetItem</c> of one item by its key. End the chain with <see cref="GetCommand{T}.ItemAsync"/> or <see cref="GetCommand{T}.SendAsync"/>; a missing item is not an error, and
    /// the read is eventually consistent unless <see cref="GetCommand{T}.Consistent"/> says otherwise.
    /// </summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public GetCommand<T> Get(AttributeValue partitionKey, AttributeValue sortKey = default) =>
        new(Model.Begin(partitionKey, sortKey), Model);

    /// <summary>
    /// Starts a <c>GetItem</c> by a key the generator built, such as <c>customers.Get(Customer.Keys.Of("alice"))</c>. End the chain with <see cref="GetCommand{T}.ItemAsync"/> or
    /// <see cref="GetCommand{T}.SendAsync"/>; a missing item is not an error.
    /// </summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public GetCommand<T> Get<TKey>(TKey key)
        where TKey : IKey<T> => new(BeginWithKey(key), Model);

    /// <summary>
    /// Starts a put of a whole item. The item is written to the request here and now, so changing it afterwards changes nothing. A put replaces
    /// everything under the key; <see cref="PutCommand{T}.IfNotExists"/> makes it a create. An entity with a <see cref="VersionAttribute"/> adds the condition on its version and stores
    /// the next one, see there, unless <see cref="TableExtensions.IgnoreVersion{T}(PutCommand{T})"/> says otherwise. It takes the version from the entity, so a put has no <c>IfVersion</c>: put the entity as it was read.
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

    /// <summary>Starts an <c>UpdateItem</c> by a key the generator built, such as <c>orders.Update(Order.Keys.Of(customerId, number))</c>. See <see cref="Update(AttributeValue, AttributeValue)"/>.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public UpdateCommand<T> Update<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectUpdate(BeginWithKey(key), key), Model);

    /// <summary>Starts a delete of one item by its key. Deleting what does not exist is no error unless <see cref="DeleteCommand{T}.IfExists"/> asks for one.</summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public DeleteCommand<T> Delete(AttributeValue partitionKey, AttributeValue sortKey = default) =>
        new(WriteRules.ExpectVersion(Model.Begin(partitionKey, sortKey)), Model);

    /// <summary>Starts a delete of one item by a generated key. Deleting what does not exist is no error unless <see cref="DeleteCommand{T}.IfExists"/> asks for one.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public DeleteCommand<T> Delete<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectVersion(BeginWithKey(key)), Model);

    /// <summary>
    /// Starts a query of one partition: <c>await foreach (var order in orders.Query(pk, SortKeyCondition.BeginsWith("ORDER#")).ItemsAsync(ct))</c>. A query of a table of an entity
    /// reads that entity, even from a partition that holds several types, see <see cref="QueryCommand{T}"/>.
    /// </summary>
    /// <param name="partitionKey">The partition key. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the table has none.</exception>
    public QueryCommand<T> Query(AttributeValue partitionKey, SortKeyCondition sortKey = default) => new(Model.BeginQuery(partitionKey, sortKey), Model);

    /// <summary>
    /// Starts a <c>Query</c> for the one item of a key, which is a <c>Get</c> that can take a filter: <c>orders.Query(Order.Keys.Of(id, day)).Filter(open).ItemAsync(ct)</c>.
    /// </summary>
    /// <remarks>
    /// It queries the partition key, with <c>Eq</c> on the sort key if the key has one, so it finds at most one item. A read of an entity filters on its discriminator, so an item of another
    /// type under the key is no match, where a <c>Get</c> would fail on it. A partition key such as <c>Order.Keys.Pk.Of(id)</c> is no key of an item, and goes to the overloads that take a condition on the sort key.</remarks>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public QueryCommand<T> Query<TKey>(TKey key)
        where TKey : IKey<T>
    {
        Argument.ThrowIfNull(key);
        return new(Model.BeginKeyQuery(key), Model);
    }

    /// <summary>
    /// Starts a query of several partitions, merged by sort key, for write shards and time buckets: <c>orders.Query(shards, SortKeyCondition.Ge(from)).ItemsAsync(ct)</c>. It costs at least one
    /// read per partition, see <see cref="FanOutQueryCommand{T}"/>.
    /// </summary>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, which the message places by position, or there is a condition on a sort key and the table has none.</exception>
    public FanOutQueryCommand<T> Query(IEnumerable<AttributeValue> partitionKeys, SortKeyCondition sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

    /// <summary>Starts a query of several partitions by their string keys, which is <see cref="Query(IEnumerable{AttributeValue}, SortKeyCondition)"/> for write shards: <c>orders.Query(["SHARD#0", "SHARD#1"])</c>.</summary>
    /// <param name="partitionKeys">The partitions, read here and not kept. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    public FanOutQueryCommand<T> Query(scoped ReadOnlySpan<string> partitionKeys, SortKeyCondition sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

    /// <summary>
    /// Starts a query of several partitions by string keys that are computed, which is <see cref="Query(IEnumerable{AttributeValue}, SortKeyCondition)"/> for a list of shards:
    /// <c>orders.Query(Enumerable.Range(0, 8).Select(static shard => Order.Keys.Pk.Format(shard)))</c>.
    /// </summary>
    /// <remarks>This overload yields to every other, so a call binds to it only when no other applies. That takes C# 13: a compiler below it ignores the priority, and a <c>string[]</c> needs <c>AsSpan()</c> there.</remarks>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    [OverloadResolutionPriority(-1)]
    public FanOutQueryCommand<T> Query(IEnumerable<string> partitionKeys, SortKeyCondition sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Model);

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

    /// <summary>
    /// Waits for at most five minutes until the table and its global secondary indexes are <c>ACTIVE</c>. See <see cref="WaitUntilActiveAsync(TimeSpan, CancellationToken)"/>, which an index
    /// that <c>UpdateTable</c> added needs, because that takes longer.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    /// <exception cref="TimeoutException">A poll ran out of <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns an error other than <see cref="ResourceNotFoundException"/>; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> WaitUntilActiveAsync(CancellationToken cancellationToken = default) =>
        TableAdministration.WaitUntilActiveAsync(Model, TableAdministration.DefaultActiveTimeout, cancellationToken);

    /// <summary>
    /// Waits until the table and all its global secondary indexes, those its entity declares among them, are <c>ACTIVE</c>, polling <c>DescribeTable</c> at growing intervals from 100 ms to 5 s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the clock of the client.</b> The polls wait on <see cref="DynamoDbClientOptions.TimeProvider"/>, so a test with a fake clock does not wait. The wait is bounded: the last poll starts at the latest when <paramref name="timeout"/> is up, and then the call throws <see cref="TableNotActiveException"/>, which is a <see cref="TimeoutException"/>
    /// with the name of the table and its last status, whatever that status is. A poll
    /// itself takes as long as a call takes, up to <see cref="DynamoDbClientOptions.AttemptTimeout"/> per attempt, so the call can return that much later.
    /// With a timeout of zero it looks once. A <paramref name="cancellationToken"/> on the same timeout races it, and when the token wins the caller gets a bare <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/>, with no table and no
    /// index named, so give the wait less time than the token.
    /// </para>
    /// <para>
    /// <b>A missing table</b> counts as not active yet, because <c>DescribeTable</c> right after <c>CreateTable</c> can miss a new table. So a wrong name shows
    /// up as a timeout with no <see cref="TableNotActiveException.LastStatus"/>, whose <see cref="Exception.InnerException"/> is the <see cref="ResourceNotFoundException"/>. A status that never turns active, such as
    /// <c>DELETING</c>, runs into the timeout too.
    /// </para>
    /// <para>
    /// <b>An index that was added later.</b> A table reads <c>ACTIVE</c> while an index it was given by <c>UpdateTable</c> backfills, and the index misses items until it is done, so this waits for the indexes too, and
    /// <see cref="TableNotActiveException.PendingIndex"/> names the one that held it up. DynamoDB first provisions such an index, which takes minutes on any table, often more than five, and then backfills it, which takes as long as the table is large and can take hours.
    /// So that wait needs a longer timeout than the default of five minutes: half an hour for a small table, more for a large one.
    /// Local secondary indexes have no status: they exist from the creation of the table.
    /// </para>
    /// <para>
    /// <b>An index the entity declares</b> that was just added may not be listed yet, and what is not listed cannot be seen to be behind. So every global index that the entity declares has to be listed as well, and until then this keeps polling; a
    /// table that does not have such an index at all runs into the timeout, and the message of the <see cref="TableNotActiveException"/> says that it was not listed. A table opened without an entity declares nothing, so
    /// <c>client.Table(name, "pk", "sk")</c> waits for what the table lists, which is the wait before <c>UpdateTable</c> adds an index, and <see cref="Of{TOther}"/> gives a table that waits for what that entity declares.
    /// </para>
    /// </remarks>
    /// <param name="timeout">How long to wait, which for an index that <c>UpdateTable</c> added is half an hour or more, longer for a large table. <see cref="Timeout.InfiniteTimeSpan"/> is not a bound and is rejected.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The description of the active table.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative.</exception>
    /// <exception cref="TableNotActiveException">The table or an index of it was not active in time.</exception>
    /// <exception cref="TimeoutException">A poll ran out of <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt.</exception>
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

    /// <summary>
    /// Runs a read-modify-write and repeats it, up to 5 runs with a short wait between them, when it loses to another writer. It is
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>, whose remarks say what a run is and how to write one.
    /// </summary>
    /// <remarks>Code that holds a table needs no client for it. Nothing ties a run to this table: it can touch others, and a transaction.</remarks>
    /// <typeparam name="TResult">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>, and it must read what it depends on itself.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<TResult> RetryOnConflictAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken) =>
        ConflictRetry.StartAsync(Model.Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names. It is
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/>, whose remarks say which conflicts to name and how many runs to allow.
    /// </summary>
    /// <typeparam name="TResult">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">
    /// The conflicts that run it again: <see cref="ConflictKind.Any"/>, the default, <see cref="ConflictKind.ConditionalCheckFailed"/> or <see cref="ConflictKind.TransactionConflict"/>.
    /// </param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run. The default is 5.</param>
    /// <param name="cancellationToken">Passed to every run; it also ends the wait between two runs. Because <paramref name="maxAttempts"/> comes before it, pass it by name: <c>cancellationToken: ct</c>.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<TResult> RetryOnConflictAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> operation, ConflictKind retryOn = ConflictKind.Any, int maxAttempts = ConflictRetry.DefaultMaxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Model.Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>Runs a read-modify-write that returns nothing, and runs it again when it loses to another writer. See <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/>.</summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken) =>
        ConflictRetry.StartAsync(Model.Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that returns nothing up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, ConflictKind, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">
    /// The conflicts that run it again: <see cref="ConflictKind.Any"/>, the default, <see cref="ConflictKind.ConditionalCheckFailed"/> or <see cref="ConflictKind.TransactionConflict"/>.
    /// </param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run. The default is 5.</param>
    /// <param name="cancellationToken">Passed to every run; it also ends the wait between two runs. Because <paramref name="maxAttempts"/> comes before it, pass it by name: <c>cancellationToken: ct</c>.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, ConflictKind retryOn = ConflictKind.Any, int maxAttempts = ConflictRetry.DefaultMaxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Model.Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write that gets the number of its run, 1 for the first, and repeats it when it loses to another writer, as
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> does. See
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{int, CancellationToken, ValueTask{T}}, CancellationToken)"/> for an example.
    /// </summary>
    /// <remarks>Only the first run may take what was read before: the run after a conflict has lost to a write that this value does not hold, and taking it again fails the same way, which is what <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA218</see> reports for a version from before the call.</remarks>
    /// <typeparam name="TResult">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<TResult> RetryOnConflictAsync<TResult>(Func<int, CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken) =>
        ConflictRetry.StartAsync(Model.Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that gets the number of its run up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{int, CancellationToken, ValueTask{TResult}}, CancellationToken)"/> for the number, and
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, ConflictKind, int, CancellationToken)"/> for the rest.
    /// </summary>
    /// <typeparam name="TResult">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">
    /// The conflicts that run it again: <see cref="ConflictKind.Any"/>, the default, <see cref="ConflictKind.ConditionalCheckFailed"/> or <see cref="ConflictKind.TransactionConflict"/>.
    /// </param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run. The default is 5.</param>
    /// <param name="cancellationToken">Passed to every run; it also ends the wait between two runs. Because <paramref name="maxAttempts"/> comes before it, pass it by name: <c>cancellationToken: ct</c>.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<TResult> RetryOnConflictAsync<TResult>(Func<int, CancellationToken, ValueTask<TResult>> operation, ConflictKind retryOn = ConflictKind.Any, int maxAttempts = ConflictRetry.DefaultMaxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Model.Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>Runs a read-modify-write that returns nothing, with the number of the run. See <see cref="RetryOnConflictAsync{TResult}(Func{int, CancellationToken, ValueTask{TResult}}, CancellationToken)"/>.</summary>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<int, CancellationToken, ValueTask> operation, CancellationToken cancellationToken) =>
        ConflictRetry.StartAsync(Model.Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that returns nothing, with the number of the run, up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{int, CancellationToken, ValueTask{TResult}}, ConflictKind, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">
    /// The conflicts that run it again: <see cref="ConflictKind.Any"/>, the default, <see cref="ConflictKind.ConditionalCheckFailed"/> or <see cref="ConflictKind.TransactionConflict"/>.
    /// </param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run. The default is 5.</param>
    /// <param name="cancellationToken">Passed to every run; it also ends the wait between two runs. Because <paramref name="maxAttempts"/> comes before it, pass it by name: <c>cancellationToken: ct</c>.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<int, CancellationToken, ValueTask> operation, ConflictKind retryOn = ConflictKind.Any, int maxAttempts = ConflictRetry.DefaultMaxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Model.Core, operation, retryOn, maxAttempts, cancellationToken);

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
