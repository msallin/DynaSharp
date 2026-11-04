using DynaSharp.Fluent;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;
using DynaSharp.Testing.Fake;

namespace DynaSharp.Testing;

/// <summary>
/// An in-memory DynamoDB that speaks the wire protocol behind an <see cref="HttpMessageHandler"/>, for functional tests: a test sets up tables and items, runs the code under test
/// through a real client, and reads the state back. No network, no container, no queued answers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clients.</b> <see cref="CreateClient"/> gives a client over the fake, <see cref="CreateStreamsClient"/> one of DynamoDB Streams, and <c>StubClient.Create(fake.CreateHandler())</c> is the same: <see cref="CreateHandler"/> returns a
/// <see cref="StubHandler"/> whose responder is <see cref="Answer(RecordedRequest)"/>, so <see cref="StubHandler.Requests"/> records what the client sent, and a response queued with
/// <see cref="StubHandler.Enqueue"/> answers the next request instead of the fake, which never sees it: a queued throttle changes no state, as a throttle of DynamoDB does not. Every handler
/// of one fake shares its state. A test of a write whose response is lost wraps the fake in a responder of its own that answers, and then returns an error in place of the answer.
/// </para>
/// <para>
/// <b>What it answers.</b> <c>CreateTable</c>, <c>DescribeTable</c>, <c>UpdateTable</c>, <c>DeleteTable</c>, <c>ListTables</c>, <c>DescribeTimeToLive</c>, <c>UpdateTimeToLive</c>,
/// <c>DescribeLimits</c>, <c>GetItem</c>, <c>PutItem</c>, <c>UpdateItem</c>, <c>DeleteItem</c>, <c>Query</c>, <c>Scan</c>, <c>BatchGetItem</c>, <c>BatchWriteItem</c>, <c>TransactGetItems</c> and
/// <c>TransactWriteItems</c>, with condition, update, key condition, filter and projection expressions, <c>ReturnValues</c>, <c>ReturnConsumedCapacity</c> and a transaction token that holds for
/// 10 minutes on the clock, and of DynamoDB Streams <c>ListStreams</c>, <c>DescribeStream</c>, <c>GetShardIterator</c> and <c>GetRecords</c>: a table with a stream records every change
/// of an item in a shard, in the order of the writes, the records of a request together and none of a request that failed. A table and its indexes are <c>ACTIVE</c> in the answer that creates them, an index holds the items
/// of its table at once and follows every write at once, and every read sees the last write. Keys are checked against the schema with the messages of DynamoDB, as are the type of a key of an index,
/// empty keys, the sizes of keys and the item limit of 400 KB, and numbers keep their 38 digits and come back in canonical form, <c>1.50</c> as <c>1.5</c>.
/// </para>
/// <para>
/// <b>What it does not do.</b> An operation it does not answer, such as PartiQL, is HTTP 400 with <c>UnknownOperationException</c>. A member it does not implement inside an
/// operation it answers, such as tags or a legacy parameter, is HTTP 400 with the code <c>UnsupportedByFakeException</c>, which names the member: the fake never ignores a member. Nothing throttles, nothing is eventually consistent,
/// no transaction conflicts, a batch leaves nothing unprocessed, a stream has one shard until <see cref="SplitStreamShards"/> gives it children, and nothing expires by itself.
/// </para>
/// <para>
/// <b>Time.</b> The clock given to the constructor dates the tables and the stream records, expires shard iterators after 15 minutes and trims records after 24 hours, and
/// <see cref="ExpireItems"/> deletes what has expired by it. One fake per test, made with <see langword="new"/>: it holds no static state
/// and nothing to dispose. It is thread-safe, and each request runs under one lock from start to end.
/// </para>
/// <para>
/// <b>The direct API.</b> <see cref="CreateTable(CreateTableRequest)"/>, <see cref="Put(string, Item)"/>, <see cref="Get(string, Item)"/>, <see cref="Delete"/>, <see cref="Items(string)"/> and the
/// rest set up and read state without a client, and go through the same checks as a request: a key that does not match the schema throws the <see cref="ValidationException"/> a client would get.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var fake = new FakeDynamoDb();
/// fake.CreateTable&lt;Customer&gt;("Shop");
/// using var client = fake.CreateClient();
///
/// await service.RegisterAsync(client, "alice", ct);
///
/// var stored = fake.Get&lt;Customer&gt;("Shop", StubResponse.KeyOf(Customer.Keys.Of("alice")));
/// </code>
/// </example>
public sealed partial class FakeDynamoDb
{
    private readonly FakeDatabase _db;

    /// <summary>Initializes a new instance of the <see cref="FakeDynamoDb"/> class, without tables.</summary>
    /// <param name="timeProvider">The clock of creation times, of time to live, of the 10 minutes a client request token of a transaction holds and of the streams, <see cref="TimeProvider.System"/> if <see langword="null"/>.</param>
    public FakeDynamoDb(TimeProvider? timeProvider = null)
    {
        _db = new FakeDatabase(timeProvider ?? TimeProvider.System);
    }

    /// <summary>Gets the names of the tables, in ordinal order, as <c>ListTables</c> gives them.</summary>
    public IReadOnlyList<string> TableNames
    {
        get
        {
            lock (_db.Gate)
            {
                return [.. _db.Tables.Keys];
            }
        }
    }

    /// <summary>Creates a handler that answers every request from this fake, for <see cref="StubClient.Create"/> or for <see cref="DynamoDbClientOptions.HttpHandler"/>.</summary>
    /// <returns>A new <see cref="StubHandler"/> whose responder is <see cref="Answer(RecordedRequest)"/>. It holds nothing that needs disposing.</returns>
    public StubHandler CreateHandler() => new(Answer);

    /// <summary>Creates a client over a new handler of this fake, as <see cref="StubClient.Create"/> makes it: <c>us-east-1</c>, static fake credentials, one attempt per call.</summary>
    /// <param name="configure">Adjusts the options with a <c>with</c> expression, or <see langword="null"/> to keep them.</param>
    /// <returns>The client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> returns <see langword="null"/>.</exception>
    public DynamoDbClient CreateClient(Func<DynamoDbClientOptions, DynamoDbClientOptions>? configure = null) => StubClient.Create(CreateHandler(), configure);

    /// <summary>
    /// Creates a client of DynamoDB Streams over a new handler of this fake, as <see cref="StubClient.CreateStreams"/> makes it: it reads the streams of the tables of this fake, and
    /// <see cref="DynamoDbStreamsClient.ReadShardAsync"/> and <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> work over it as over DynamoDB.
    /// </summary>
    /// <remarks>
    /// Its <see cref="DynamoDbClientOptions.TimeProvider"/> is the clock of this fake, so that what the client waits for the stream, <see cref="ReadShardRequest.EmptyPageDelay"/> and
    /// the looks for the children of a shard, passes as the fake's iterators expire, its records age and its late children are listed: a test that moves the clock moves both.
    /// <paramref name="configure"/> can set another. <see cref="CreateClient"/> keeps the system clock, because its client waits only for itself, between the runs of
    /// <c>RetryOnConflictAsync</c>, which a test should not have to move a clock for.
    /// </remarks>
    /// <param name="configure">Adjusts the options with a <c>with</c> expression, or <see langword="null"/> to keep them.</param>
    /// <returns>The client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> returns <see langword="null"/>.</exception>
    public DynamoDbStreamsClient CreateStreamsClient(Func<DynamoDbClientOptions, DynamoDbClientOptions>? configure = null) =>
        StubClient.CreateStreams(CreateHandler(), options => configure is null ? options with { TimeProvider = _db.Clock } : configure(options with { TimeProvider = _db.Clock }));

    /// <summary>Answers a request a <see cref="StubHandler"/> received: the operation from <c>X-Amz-Target</c>, and the body as sent.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The answer, with <c>x-amzn-RequestId</c>. It fits any operation, because it is the answer to the operation of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public StubResponse Answer(RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FakeWire.Handle(_db, request.Headers.GetValueOrDefault(WireProtocol.TargetHeaderName), request.Body.Span).ToStubResponse();
    }

    /// <summary>Answers a request given as text, which is what a server in front of the fake or a raw client calls.</summary>
    /// <param name="operation">The operation: <c>GetItem</c>, or the whole target, <c>DynamoDB_20120810.GetItem</c>.</param>
    /// <param name="requestJson">The body.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public StubResponse Answer(string operation, string requestJson)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(requestJson);
        return FakeWire.Handle(_db, operation, System.Text.Encoding.UTF8.GetBytes(requestJson)).ToStubResponse();
    }

    /// <summary>Creates a table, as a <c>CreateTable</c> request does, with the same checks. It is <c>ACTIVE</c> at once.</summary>
    /// <param name="request">The request, as the request API takes it.</param>
    /// <returns>The description, as <c>DescribeTable</c> gives it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists.</exception>
    /// <exception cref="ValidationException">DynamoDB would reject the request.</exception>
    /// <exception cref="DynamoDbException">The request asks for what the fake does not implement, such as tags: <see cref="DynamoDbException.ErrorCode"/> is <c>UnsupportedByFakeException</c>.</exception>
    public TableDescription CreateTable(CreateTableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Direct(TableOperations.CreateTableName, request.TableName, () => Describe(TableOperations.Create(_db, request)));
    }

    /// <summary>
    /// Creates the table an entity lives in, as <c>client.Table&lt;T&gt;(tableName).CreateTable()</c> would: its keys with their types and every index it declares, on demand. A table of several
    /// entities takes <see cref="CreateTable(CreateTableRequest)"/> with the request of <c>CreateTable().Including&lt;TOther&gt;().ToRequest()</c>.
    /// </summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="tableName">The table.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists.</exception>
    public TableDescription CreateTable<T>(string tableName)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(tableName);
        const string Entity = "The entity";
        var schema = new TableSchema(Entity, T.Info.PartitionKeyName, T.Info.SortKeyName);
        schema.Add(Entity, T.Info);
        return CreateTable(schema.ToRequest(tableName, null, null));
    }

    /// <summary>
    /// Creates a table without a model, on demand, whose keys are strings: the table of <c>client.Table(name, partitionKey, sortKey)</c>, such as the table
    /// of a <c>LockTable</c> of <c>DynaSharp.Coordination</c>. A table with keys of other types, indexes or a stream takes <see cref="CreateTable(CreateTableRequest)"/>.
    /// </summary>
    /// <param name="tableName">The table.</param>
    /// <param name="partitionKey">The attribute name of the partition key, a string.</param>
    /// <param name="sortKey">The attribute name of the sort key, a string, or <see langword="null"/> for a table without one.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> or <paramref name="partitionKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceInUseException">A table of that name exists.</exception>
    /// <exception cref="ValidationException">DynamoDB would reject the request, such as for an empty name or a sort key named as the partition key.</exception>
    public TableDescription CreateTable(string tableName, string partitionKey, string? sortKey = null)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(partitionKey);
        return CreateTable(new CreateTableRequest
        {
            TableName = tableName,
            AttributeDefinitions = sortKey is null
                ? [new AttributeDefinition(partitionKey, ScalarAttributeType.String)]
                : [new AttributeDefinition(partitionKey, ScalarAttributeType.String), new AttributeDefinition(sortKey, ScalarAttributeType.String)],
            KeySchema = sortKey is null
                ? [new KeySchemaElement(partitionKey, KeyType.Hash)]
                : [new KeySchemaElement(partitionKey, KeyType.Hash), new KeySchemaElement(sortKey, KeyType.Range)],
            BillingMode = BillingMode.PayPerRequest,
        });
    }

    /// <summary>Describes a table, as <c>DescribeTable</c> does, with its item count and size as they are now.</summary>
    /// <param name="tableName">The table.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    public TableDescription DescribeTable(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        return Direct(TableOperations.DescribeTableName, tableName, () => Describe(_db.DescribeTable(tableName)));
    }

    /// <summary>Deletes a table with its items. The name is free at once.</summary>
    /// <param name="tableName">The table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The table is protected against deletion.</exception>
    public void DeleteTable(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        Direct(TableOperations.DeleteTableName, tableName, () => TableOperations.Delete(_db, tableName));
    }

    /// <summary>
    /// Drops every table with its items and streams, and forgets every client request token. The handlers and clients of this fake stay valid and see no tables. A stream enabled
    /// afterwards gets another ARN than the streams before, also on the same clock, so a shard iterator from before is <c>ResourceNotFoundException</c>.
    /// </summary>
    public void Reset()
    {
        lock (_db.Gate)
        {
            _db.Reset();
        }
    }

    /// <summary>Puts an item, as a <c>PutItem</c> without a condition does: it replaces the item with its key, after the checks of the keys, the keys of the indexes and the size, and the indexes follow.</summary>
    /// <param name="tableName">The table.</param>
    /// <param name="item">The item, which the fake copies.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">DynamoDB would reject the item: a key attribute is missing or of another type, a number is out of range, the item is larger than 400 KB.</exception>
    public void Put(string tableName, Item item)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(item);
        Direct(PutItemOperation.Name, tableName, () => _db.GetTable(tableName).Put(item));
    }

    /// <summary>Gets an item by its key, as a <c>GetItem</c> does.</summary>
    /// <param name="tableName">The table.</param>
    /// <param name="key">The key attributes, and nothing else.</param>
    /// <returns>A copy of the item, or <see langword="null"/> if there is none.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The key does not match the schema of the table.</exception>
    public Item? Get(string tableName, Item key)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(key);
        return Direct(GetItemOperation.Name, tableName, () =>
        {
            var table = _db.GetTable(tableName);
            return table.Get(table.KeySchema.ReadKey(key)) is { } item ? new Item(item) : null;
        });
    }

    /// <summary>Deletes an item by its key, as a <c>DeleteItem</c> without a condition does.</summary>
    /// <param name="tableName">The table.</param>
    /// <param name="key">The key attributes, and nothing else.</param>
    /// <returns><see langword="true"/> if there was an item.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The key does not match the schema of the table.</exception>
    public bool Delete(string tableName, Item key)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(key);
        return Direct(DeleteItemOperation.Name, tableName, () =>
        {
            var table = _db.GetTable(tableName);
            return table.Remove(table.KeySchema.ReadKey(key)) is not null;
        });
    }

    /// <summary>Gets every item of a table, in key order: partitions in the order of their keys, then sort keys in the order of DynamoDB. A test should not depend on the order of partitions, as on DynamoDB.</summary>
    /// <param name="tableName">The table.</param>
    /// <returns>Copies of the items, a snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    public IReadOnlyList<Item> Items(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        return Direct<IReadOnlyList<Item>>(ScanOperation.Name, tableName, () => [.. _db.GetTable(tableName).All().Select(static item => new Item(item))]);
    }

    /// <summary>
    /// Deletes every item that has expired: in each table with time to live <c>ENABLED</c>, an item whose attribute of time to live is a number of epoch seconds at or before the clock of the fake. An expiry more than five years before the clock is left, as DynamoDB leaves it.
    /// Nothing expires by itself; this is the background sweep of DynamoDB, run when the test says. A table with a stream records each delete as a <c>REMOVE</c> whose <c>userIdentity</c> is
    /// the service, <c>dynamodb.amazonaws.com</c> of type <c>Service</c>, as DynamoDB records it.
    /// </summary>
    /// <returns>The number of items deleted.</returns>
    public int ExpireItems()
    {
        lock (_db.Gate)
        {
            return TimeToLive.Expire(_db);
        }
    }

    /// <summary>
    /// Ends every open shard of the stream of a table and opens its children, as DynamoDB does on its own: one child when it rolls a shard over, which it does every few hours, two
    /// when it splits a shard with its partition. The children take the records of the shard from then on, each the items of its share of the partition keys, and name it in
    /// <c>ParentShardId</c>. The ended shard gets its <c>EndingSequenceNumber</c>, and a reader of it reaches its end.
    /// </summary>
    /// <remarks>
    /// DynamoDB can list a child in <c>DescribeStream</c> a moment after its parent ended. <paramref name="secondChildListedAfter"/> plays that for the second of two children: it takes
    /// records at once and is listed, also by a <c>ShardFilter</c> of <c>CHILD_SHARDS</c>, only once the clock of the fake has moved on by that much.
    /// </remarks>
    /// <param name="tableName">The table, whose stream is enabled.</param>
    /// <param name="children">How many children each open shard gets: 1 or 2, the most a shard of DynamoDB has.</param>
    /// <param name="secondChildListedAfter">How long after the split the second child is listed; zero, the default, lists it at once. It applies only to two children.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="children"/> is not 1 or 2, or <paramref name="secondChildListedAfter"/> is negative.</exception>
    /// <exception cref="ResourceNotFoundException">There is no such table.</exception>
    /// <exception cref="ValidationException">The table has no enabled stream.</exception>
    public void SplitStreamShards(string tableName, int children = 2, TimeSpan secondChildListedAfter = default)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentOutOfRangeException.ThrowIfLessThan(children, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(children, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(secondChildListedAfter, TimeSpan.Zero);
        Direct(TableOperations.UpdateTableName, tableName, () =>
        {
            var stream = _db.DescribeTable(tableName).LatestStream is { IsEnabled: true } enabled
                ? enabled
                : throw FakeError.Validation($"The table {tableName} has no enabled stream to split.");
            stream.Split(_db.Clock.GetUtcNow(), children, secondChildListedAfter, _db.NextShardNumber, _db.NextSequenceNumber);
            return stream;
        });
    }

    private static TableDescription Describe(FakeTable table)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var json = new System.Text.Json.Utf8JsonWriter(buffer, WireJson.WriterOptions))
        {
            json.WriteStartObject();
            ResponseJson.WriteTableDescription(json, "Table", table, TableStatus.Active);
            json.WriteEndObject();
        }

        var reader = new DynamoDbReader(buffer.WrittenSpan);
        reader.ReadStartItem();
        _ = reader.TryReadAttributeName();
        return TableJson.ReadTable(ref reader);
    }

    // Runs a direct call under the gate and throws for a failure what a client would throw for the answer of the wire.
    private T Direct<T>(string operation, string tableName, Func<T> call)
    {
        lock (_db.Gate)
        {
            try
            {
                var result = call();
                _db.CommitChanges();
                return result;
            }
            catch (FakeError error)
            {
                throw error.ToDynamoDbException(operation, tableName);
            }
            finally
            {
                _db.DiscardChanges();
            }
        }
    }
}
