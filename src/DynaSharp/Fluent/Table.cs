using System.Collections.Concurrent;
using System.Diagnostics;
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
/// <b>Every method starts a command,</b> which is sent with <c>SendAsync</c> or one of its siblings such as <c>ItemAsync</c>: <c>await orders.Get(Order.Keys.Of("alice", 1)).Consistent().SendAsync(ct);</c>.
/// Each verb is the DynamoDB operation of that name, with its semantics: <see cref="Put"/> replaces a whole item, <c>Update</c> creates an item
/// that does not exist unless told otherwise, and nothing is read before it is written.
/// </para>
/// <para>
/// <b>A table of an entity takes the keys its generator built,</b> <c>Order.Keys.Of(...)</c> and <c>Order.Keys.Pk.Of(...)</c>, which format the values the way the item stores them. The values of a
/// key as they are stored, such as <c>shop.Get("USER#1", "PROFILE")</c>, go to a table without a schema, which <c>client.Table(name, partitionKey, sortKey)</c> opens, see <see cref="ItemTableExtensions"/>.
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
    /// the next one, see there, unless <see cref="EntityWriteExtensions.IgnoreVersion{T}(PutCommand{T})"/> says otherwise. It takes the version from the entity, so a put has no <c>IfVersion</c>: put the entity as it was read.
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
    /// Starts an <c>UpdateItem</c> by a key the generator built, such as <c>orders.Update(Order.Keys.Of(customerId, number))</c>. It creates the item if none has the key, unless
    /// <see cref="UpdateCommand{T}.IfExists"/> says otherwise, so an update of an entity with a discriminator gives a new item its discriminator, see <see cref="UpdateCommand{T}"/>.
    /// </summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public UpdateCommand<T> Update<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectUpdate(BeginWithKey(key), key), Model);

    /// <summary>Starts a delete of one item by a generated key. Deleting what does not exist is no error unless <see cref="DeleteCommand{T}.IfExists"/> asks for one.</summary>
    /// <typeparam name="TKey">The key. A key of another entity is a compile error.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The command.</returns>
    public DeleteCommand<T> Delete<TKey>(TKey key)
        where TKey : IKey<T> => new(WriteRules.ExpectVersion(BeginWithKey(key)), Model);

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

    /// <summary>Starts a scan of the whole table, which charges for every item in it, see <see cref="ScanCommand{T}"/>. A scan of a table of an entity gives that entity's items.</summary>
    /// <returns>The command.</returns>
    public ScanCommand<T> Scan() => new(Model.BeginScan(), Model);

    /// <summary>Reads the description of the table: status, key schema, indexes and approximate counts, which DynamoDB updates about every six hours.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description. A response without one gives the name of this table and <see cref="TableStatus.Unknown"/>.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist, or it was created a moment ago: this call is eventually consistent.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> DescribeTableAsync(CancellationToken cancellationToken = default) =>
        TableAdministration.DescribeAsync(Model, cancellationToken);

    /// <summary>
    /// Reads the description of the table, as <see cref="DescribeTableAsync(CancellationToken)"/> does, within <paramref name="timeout"/>, and throws a <see cref="TimeoutException"/> that names the table when the time
    /// runs out first: the bound of a health check, <c>await orders.DescribeTableAsync(TimeSpan.FromSeconds(5), ct)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the clock of the client.</b> The time runs on <see cref="DynamoDbClientOptions.TimeProvider"/>, and it bounds the call with its retries; the call in flight is cancelled. A token of the caller still
    /// cancels as it did, with an <see cref="OperationCanceledException"/> of that token, also when both happen at once, where a token on a timer of its own gives a bare <see cref="OperationCanceledException"/>.
    /// A <see cref="DynamoDbClientOptions.AttemptTimeout"/> of an attempt is its own and still throws its own <see cref="TimeoutException"/>.
    /// </para>
    /// </remarks>
    /// <param name="timeout">The time for the call, more than zero, or <see cref="Timeout.InfiniteTimeSpan"/> for no bound.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The description. A response without one gives the name of this table and <see cref="TableStatus.Unknown"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero, negative other than infinite, or above 4294967294 milliseconds.</exception>
    /// <exception cref="TimeoutException">The call did not end within <paramref name="timeout"/>, with a message that names the table and the <see cref="OperationCanceledException"/> of the call inside it; or an attempt ran out of <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist, or it was created a moment ago: this call is eventually consistent.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TableDescription> DescribeTableAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        TableAdministration.DescribeAsync(Model, timeout, cancellationToken);

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
    /// Deletes this table and <b>all the items in it</b> (<c>DeleteTable</c>), which cannot be undone except from a backup. To delete one item, see <see cref="Delete{TKey}(TKey)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table is gone only some time later, and this call does not wait for that: it returns the description of a table that is normally <see cref="TableStatus.Deleting"/>, and while it is, creating a table
    /// of that name fails with <see cref="ResourceInUseException"/>. It is gone when <see cref="DescribeTableAsync(CancellationToken)"/> throws <see cref="ResourceNotFoundException"/>.
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
    /// Runs a read-modify-write and repeats it, up to 5 runs with a short wait between them, when it loses to another writer: a <see cref="ConditionalCheckFailedException"/>, a
    /// <see cref="TransactionConflictException"/>, or a <see cref="TransactionCanceledException"/> with one of those reasons. The failure of the last run is rethrown.
    /// </summary>
    /// <example>
    /// <code>
    /// var saved = await carts.RetryOnConflictAsync(async token =>
    /// {
    ///     var cart = await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(token);
    ///     return await carts.Update(Cart.Keys.Of("bob")).Set(Cart.Attrs.Lines, lines).IfVersion(cart!.Revision).SendAsync(token);
    /// }, ct);
    /// </code>
    /// </example>
    /// <typeparam name="TResult">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>, and it must read what it depends on itself, see the remarks.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <remarks>
    /// <para>
    /// <b>No command retries a conflict by itself.</b> This is the loop to ask for it. Nothing ties a run to this table: it can read and write others, and send a transaction. The overload that takes a
    /// <see cref="ConflictKind"/> repeats one kind of conflict only, for a run whose failed condition is its answer, and takes another number of runs:
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, ConflictKind, int, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// <b>Read inside the delegate.</b> Each run starts from nothing, so <paramref name="operation"/> reads, decides and writes. A write built from what was read before this call
    /// carries the same old version in every run, so every run fails the same way and the retry changes nothing. Make it a strongly consistent read, because another read can miss the
    /// write this run lost to.
    /// </para>
    /// <para>
    /// <b>A condition can fail for a business reason too.</b> A failed condition does not say why, and it can just as well mean that the bucket is empty or the seat is taken. The delegate
    /// reads again, so it decides again: it sees what is there now and returns without writing. A rule the delegate cannot read, such as a check on an item that it does not load, it
    /// catches itself and turns into its result, because whatever leaves the delegate as one of the three exceptions counts as lost. A transaction runs again if any of its reasons is
    /// one of the two, whatever the other actions report, and the limit on the runs is what ends it when another reason does not pass. A run that reads nothing cannot decide again, so a failed condition there is
    /// the answer, and <see cref="ConflictKind.TransactionConflict"/> lets it surface at once.
    /// </para>
    /// <para>
    /// <b>Everything else surfaces at once:</b> any other exception of a run, also from the third run after two conflicts, and cancellation. After the last run its exception surfaces unchanged, so a
    /// <see cref="ConditionalCheckFailedException"/> of the last run says which item gave up in <see cref="ConditionalCheckFailedException.Key"/>, which holds the key of a write of that run as the request named it. Neither its
    /// message nor telemetry show the key, and a transaction has none: there <see cref="TransactionCanceledException.ItemFor"/> is the way to an item.
    /// </para>
    /// <para>
    /// <b>Keep follow-up writes outside the run.</b> A run is one read-modify-write, and what depends on its result, such as a second item or an entry of an audit log, goes after this call. Inside, it runs again
    /// with every run. A retry inside a run multiplies the attempts, 5 runs of 5 are 25, and an inner one that used up its runs throws what it last caught, which this call counts as a conflict of its own, so one
    /// lost race on the inner item starts the whole run over.
    /// </para>
    /// <para>
    /// <b>A wait.</b> Between two runs it waits a random time up to <c>min(1 s, 10 ms * 2^(run - 1))</c> on <see cref="DynamoDbClientOptions.TimeProvider"/>, so that writers that failed together
    /// do not meet again; a conflict usually clears within that. This has nothing to do with <see cref="DynamoDbClientOptions.MaxAttempts"/>: it takes nothing from the retry quota, and every
    /// call of a run has its own attempts, as it would outside.
    /// </para>
    /// <para>
    /// <b>A lost response can look like a conflict.</b> When a conditional write reaches DynamoDB and its response is lost, the next attempt of that call fails its condition on what the first
    /// attempt wrote, and the delegate then runs on top of its own write. A run that adds to what it read, a deposit or a line in a cart, adds twice. Give such a write
    /// <c>MaxAttempts(1)</c>: the lost response then surfaces as a failure of the call, and it is not retried here. A transaction does not have this problem, because the attempts of one send carry
    /// one <c>ClientRequestToken</c>.
    /// </para>
    /// <para>
    /// <b>In a transaction,</b> leave <c>Token(string)</c> out. A run that read something else sends another request, and a token says that it is the same one, which DynamoDB answers with
    /// <see cref="IdempotentParameterMismatchException"/>.
    /// </para>
    /// <para>
    /// <b>Telemetry.</b> The calls of a run have their spans and metrics as usual. On top of that <c>dynasharp.client.conflict.retries</c> counts each run that lost and is repeated, and
    /// <c>dynasharp.client.conflict.exhausted</c> each conflict passed on after the last run, both by the operation, the table and the error that the conflict names. A warning is
    /// logged when the runs are used up. Neither carries a key or a value.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item, see the remarks.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<TResult> RetryOnConflictAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken) =>
        ConflictRetry.StartAsync(Model.Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names:
    /// <c>RetryOnConflictAsync(operation, ConflictKind.TransactionConflict, cancellationToken: ct)</c>. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> for what a run is and how to write one.
    /// </summary>
    /// <example>
    /// A registration whose claims are a transaction: a taken email is the answer, and only a transaction in the way is asked again.
    /// <code>
    /// var registered = await customers.RetryOnConflictAsync(async token =>
    /// {
    ///     TransactMarker taken = default;
    ///     try
    ///     {
    ///         await client.TransactWrite().Unique(emails, new EmailClaim(customer.Email, customer.Id), out taken).Put(customers, customer).IfNotExists().SendAsync(token);
    ///         return true;
    ///     }
    ///     catch (TransactionCanceledException exception) when (exception.ReasonFor(taken) == TransactionFailure.ConditionalCheckFailed)
    ///     {
    ///         return false;
    ///     }
    /// }, ConflictKind.TransactionConflict, cancellationToken: ct);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <see cref="ConflictKind.TransactionConflict"/> is for a run whose failed condition is an answer that asking again does not change, such as a value that is taken, or a record that exists. It surfaces at once,
    /// with no wait and no second run, and so does anything else that is not named. A cancelled transaction runs again when any of its reasons is of a kind named here, whatever the others say, and the limit on the
    /// runs ends it when another reason does not pass.
    /// </para>
    /// <para>
    /// <see cref="ConflictKind.ConditionalCheckFailed"/> alone leaves a transaction in the way to the caller, which a run that never meets a transaction does not need either way.
    /// </para>
    /// <para>
    /// Every failed condition on a version means that another writer got through, so <c>n</c> writers on one item need at most <c>n</c> runs each, whatever the timing. That is the number to pass
    /// as <paramref name="maxAttempts"/> when more than five writers meet on one item. <see cref="TransactionFailure.TransactionConflict"/> has no such bound, because the transaction that was in the way may fail too.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Runs a read-modify-write that returns nothing, and runs it again when it loses to another writer. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> for what a run is, what makes it run again and how to write one.
    /// </summary>
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
    /// <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken)"/> does.
    /// </summary>
    /// <example>
    /// A value read before the call, which the first run takes and every later run reads again:
    /// <code>
    /// var cart = await carts.Get(Cart.Keys.Of("bob")).ItemAsync(ct);
    /// await carts.RetryOnConflictAsync(async (run, token) =>
    /// {
    ///     var current = run == 1 ? cart : await carts.Get(Cart.Keys.Of("bob")).Consistent().ItemAsync(token);
    ///     await carts.Put(current! with { Lines = lines }).SendAsync(token);
    /// }, ct);
    /// </code>
    /// </example>
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
    /// Runs a write with the number of the run up to <paramref name="maxAttempts"/> times, and again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{TResult}(Func{int, CancellationToken, ValueTask{TResult}}, CancellationToken)"/> for the number, and <see cref="RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, ConflictKind, int, CancellationToken)"/> for the rest.
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
