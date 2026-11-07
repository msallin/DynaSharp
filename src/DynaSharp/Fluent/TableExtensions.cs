using System.Numerics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// What only some tables can do. These are extension methods so that the rest does not compile: a table of an entity takes no key given as its values, which would skip its
/// key template, and has no attribute-by-attribute <c>Put()</c>, which would write an item without its discriminator; a table without a schema has no key inside an item.
/// </summary>
/// <remarks>
/// <para>
/// <b>The values of a key go to <see cref="Item"/>.</b> <c>shop.Get("CUSTOMER#1", "PROFILE")</c> sends the values as they are written. On a table of an entity, <c>customers.Get("1")</c> would
/// miss the item the entity stores under <c>CUSTOMER#1</c>, so it does not compile there: such a table takes <c>Customer.Keys.Of("1")</c>, see
/// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA230</see>. A table without a schema has no template, so it takes the values, and
/// <see cref="Table{T}.Of{TOther}"/> turns it into a table of an entity.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var shop = client.Table("Shop", "pk", "sk");
/// Item? profile = await shop.Get("CUSTOMER#1", "PROFILE").ItemAsync(ct);
/// await foreach (var item in shop.Query("CUSTOMER#1", SortKeyCondition.BeginsWith("ORDER#")).ItemsAsync(ct))
/// {
///     Console.WriteLine(item["sk"]);
/// }
/// </code>
/// </example>
public static class TableExtensions
{
    /// <summary>
    /// Starts a put whose attributes follow one by one with <c>With</c>, straight to the request, without an <see cref="Item"/>:
    /// <c>shop.Put().With("pk", "USER#1").With("name", "Alice").SendAsync(ct)</c>.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <returns>The command.</returns>
    public static PutCommand<Item> Put(this Table<Item> table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        var state = model.Begin();
        state.StartItem();
        return new(state, model);
    }

    /// <summary>
    /// Adds an attribute to the item of a put. The name is the attribute name as it is, not a path. DynamoDB allows a name once in an item, and
    /// nothing checks that here.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<Item> With(this PutCommand<Item> command, string name, AttributeValue value) =>
        command.Attribute<AttributeValue, AttributeValueConverter>(name, value);

    /// <summary>Adds a number attribute to the item of a put.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The number.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<Item> With<TNumber>(this PutCommand<Item> command, string name, TNumber value)
        where TNumber : INumberBase<TNumber> => command.Attribute<TNumber, NumberConverter<TNumber>>(name, value);

    /// <summary>Adds an attribute to the item of a put through its converter. A handle of a property with <see cref="OmitEmptyAttribute"/> adds nothing for an empty string or collection, and neither does any value that <see cref="IDynamoDbConverter{T}.IsOmitted"/> of the converter counts as null.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="attribute">The attribute, whose path must be a plain name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<Item> With<TValue, TConverter>(this PutCommand<Item> command, Attr<TValue, TConverter> attribute, TValue value)
        where TConverter : IDynamoDbConverter<TValue> => command.Attribute<TValue, TConverter>(attribute.Path, value);

    /// <summary>Gets a secondary index that the entity declares, with its key names: <c>orders.Index(Order.Indexes.ByStatus)</c>. An index is read through its own type, which has no writes. Hold it in a field.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="name">The name of the index.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">The entity does not declare an index of this name.</exception>
    public static Index<T> Index<T>(this Table<T> table, string name)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        var index = IndexOf<T>(name);
        return new(table, name, index.PartitionKeyName, index.SortKeyName, !index.IsLocal);
    }

    /// <summary>
    /// Gets a secondary index of a table without a schema, with the key names an entity declares: <c>shop.Index&lt;Membership&gt;(Membership.Indexes.Gsi1)</c>. A mixed read goes through this
    /// table, so this is how it reads an index without naming its keys again. Items are read as <see cref="Item"/> until <c>Of&lt;...&gt;()</c> says otherwise, and the entity says nothing
    /// else. Hold it in a field.
    /// </summary>
    /// <typeparam name="TEntity">The entity that declares the index.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="name">The name of the index.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">The entity does not declare an index of this name.</exception>
    public static Index<Item> Index<TEntity>(this Table<Item> table, string name)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(table);
        var index = IndexOf<TEntity>(name);
        return new(table, name, index.PartitionKeyName, index.SortKeyName, !index.IsLocal);
    }

    /// <summary>
    /// Starts a <c>GetItem</c> of one item of a table without a schema by the values of its key: <c>shop.Get("CUSTOMER#1", "PROFILE")</c>. A missing item is not an error, and the read is
    /// eventually consistent unless <see cref="GetCommand{T}.Consistent"/> says otherwise.
    /// </summary>
    /// <remarks>
    /// <b>As written.</b> The values go out as they are. A table of an entity takes the key its generator built instead, <c>customers.Get(Customer.Keys.Of("1"))</c>, which formats them as
    /// the entity stores them.
    /// </remarks>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static GetCommand<Item> Get(this Table<Item> table, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        return new(model.Begin(partitionKey, sortKey), model);
    }

    /// <summary>
    /// Starts an <c>UpdateItem</c> of one item of a table without a schema by the values of its key. It creates the item if none has the key, unless
    /// <see cref="UpdateCommand{T}.IfExists"/> says otherwise.
    /// </summary>
    /// <inheritdoc cref="Get(Table{Item}, AttributeValue, AttributeValue)" path="/remarks"/>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static UpdateCommand<Item> Update(this Table<Item> table, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        return new(WriteRules.ExpectUpdate(model.Begin(partitionKey, sortKey)), model);
    }

    /// <summary>
    /// Starts a <c>DeleteItem</c> of one item of a table without a schema by the values of its key. Deleting what does not exist is no error unless
    /// <see cref="DeleteCommand{T}.IfExists"/> asks for one.
    /// </summary>
    /// <inheritdoc cref="Get(Table{Item}, AttributeValue, AttributeValue)" path="/remarks"/>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static DeleteCommand<Item> Delete(this Table<Item> table, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var model = table.Model;
        return new(WriteRules.ExpectVersion(model.Begin(partitionKey, sortKey)), model);
    }

    /// <summary>
    /// Starts a query of one partition of a table without a schema by the value of its partition key: <c>shop.Query("CUSTOMER#1", SortKeyCondition.BeginsWith("ORDER#"))</c>. Read it as
    /// several entities with <c>Of&lt;Customer, Order&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// <b>As written.</b> The value goes out as it is. A table of an entity takes the partition key its generator built instead, <c>orders.Query(Order.Keys.Pk.Of("1"))</c>, which formats it
    /// as the entity stores it.
    /// </remarks>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the table has none.</exception>
    public static QueryCommand<Item> Query(this Table<Item> table, AttributeValue partitionKey, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return QueryOf<Item>(table.Model, partitionKey, sortKey);
    }

    /// <summary>
    /// Starts a query of several partitions of a table without a schema, merged by sort key, for write shards and time buckets. It costs at least one read per partition, see
    /// <see cref="FanOutQueryCommand{T}"/>.
    /// </summary>
    /// <inheritdoc cref="Query(Table{Item}, AttributeValue, SortKeyCondition)" path="/remarks"/>
    /// <param name="table">The table.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, which the message places by position, or there is a condition on a sort key and the table has none.</exception>
    public static FanOutQueryCommand<Item> Query(this Table<Item> table, IEnumerable<AttributeValue> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, table.Model, table.Model);
    }

    /// <summary>Starts a query of several partitions of a table without a schema by their string keys: <c>shop.Query(["SHARD#0", "SHARD#1"])</c>. It costs at least one read per partition.</summary>
    /// <inheritdoc cref="Query(Table{Item}, AttributeValue, SortKeyCondition)" path="/remarks"/>
    /// <param name="table">The table.</param>
    /// <param name="partitionKeys">The partitions, read here and not kept. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    public static FanOutQueryCommand<Item> Query(this Table<Item> table, scoped ReadOnlySpan<string> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, table.Model, table.Model);
    }

    /// <summary>
    /// Starts a query of several partitions of a table without a schema by string keys that are computed: <c>shop.Query(Enumerable.Range(0, 8).Select(static shard => $"SHARD#{shard}"))</c>.
    /// It costs at least one read per partition.
    /// </summary>
    /// <remarks>
    /// <b>Last choice.</b> This overload yields to every other, so a call binds to it only when no other applies. That takes C# 13: a compiler below it ignores the priority, and a
    /// <c>string[]</c> needs <c>AsSpan()</c> there.
    /// </remarks>
    /// <param name="table">The table.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    [OverloadResolutionPriority(-1)]
    public static FanOutQueryCommand<Item> Query(this Table<Item> table, IEnumerable<string> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, table.Model, table.Model);
    }

    /// <summary>
    /// Starts a query of one partition of an index of a table without a schema by the value of its partition key: <c>shop.Index("GSI1", "gsi1pk", "gsi1sk").Query("STATUS#open")</c>. See
    /// <see cref="DynaSharp.Index{T}"/> for what is different about reading an index.
    /// </summary>
    /// <remarks>
    /// <b>As written.</b> The value goes out as it is. An index of an entity takes the partition key its generator built instead, <c>byStatus.Query(Order.Keys.ByStatusPk.Of(status))</c>.
    /// </remarks>
    /// <param name="index">The index.</param>
    /// <param name="partitionKey">The partition key of the index as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The condition on the sort key of the index, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the index has none.</exception>
    public static QueryCommand<Item> Query(this Index<Item> index, AttributeValue partitionKey, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        return QueryOf<Item>(index.Model, partitionKey, sortKey);
    }

    /// <summary>
    /// Starts a query of several partitions of an index of a table without a schema, merged by the sort key of the index: the read of a sharded index. It costs at least one read per
    /// partition.
    /// </summary>
    /// <inheritdoc cref="Query(Index{Item}, AttributeValue, SortKeyCondition)" path="/remarks"/>
    /// <param name="index">The index.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    public static FanOutQueryCommand<Item> Query(this Index<Item> index, IEnumerable<AttributeValue> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, index.Model, index.Table.Model);
    }

    /// <summary>Starts a query of several partitions of an index of a table without a schema by their string keys, such as <c>["SHARD#0", "SHARD#1"]</c>. It costs at least one read per partition.</summary>
    /// <inheritdoc cref="Query(Index{Item}, AttributeValue, SortKeyCondition)" path="/remarks"/>
    /// <param name="index">The index.</param>
    /// <param name="partitionKeys">The partitions, read here and not kept. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    public static FanOutQueryCommand<Item> Query(this Index<Item> index, scoped ReadOnlySpan<string> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, index.Model, index.Table.Model);
    }

    /// <summary>Starts a query of several partitions of an index of a table without a schema by string keys that are computed. It costs at least one read per partition.</summary>
    /// <inheritdoc cref="Query(Table{Item}, IEnumerable{string}, SortKeyCondition)" path="/remarks"/>
    /// <param name="index">The index.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    [OverloadResolutionPriority(-1)]
    public static FanOutQueryCommand<Item> Query(this Index<Item> index, IEnumerable<string> partitionKeys, SortKeyCondition sortKey = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        return FanOutQueryCommand<Item>.Start(partitionKeys, sortKey, index.Model, index.Table.Model);
    }

    /// <summary>Adds a key to a batch get of a table without a schema, by its values.</summary>
    /// <inheritdoc cref="Get(Table{Item}, AttributeValue, AttributeValue)" path="/remarks"/>
    /// <param name="command">The command.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static BatchGetCommand<Item> Get(this BatchGetCommand<Item> command, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        var state = command.Enter();
        var table = state.Table;
        table.CheckKey(partitionKey, sortKey);
        state.Actions.Add(table, state.Body.WrittenCount);
        state.WriteKeyObject(table, partitionKey, sortKey);
        return new(state, table);
    }

    /// <summary>Adds a delete to a batch of a table without a schema, by the values of the key. Deleting what does not exist is no error.</summary>
    /// <inheritdoc cref="Get(Table{Item}, AttributeValue, AttributeValue)" path="/remarks"/>
    /// <param name="command">The command.</param>
    /// <param name="partitionKey">The partition key as it is stored. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The sort key as it is stored, which a table with a sort key needs.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A key is empty, or the table has no sort key.</exception>
    public static BatchWriteCommand<Item> Delete(this BatchWriteCommand<Item> command, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        var state = command.Enter();
        BatchWriteOperation.AddDelete(state, state.Table, partitionKey, sortKey);
        return new(state, state.Table);
    }

    /// <summary>
    /// Starts a query of one partition by a typed key: <c>orders.Query(Order.Keys.Pk.Of(id), Order.Keys.Sk.BeginsWith())</c>. The key of another entity is a compile error, and the key is
    /// formatted as the entity stores it.
    /// </summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is the default value, it was built for another attribute, or there is a condition on a sort key and the table has none.</exception>
    public static QueryCommand<T> Query<T>(this Table<T> table, PartitionKey<T> partitionKey, SortKeyCondition sortKey = default)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        ThrowIfOtherAttribute(partitionKey, table.PartitionKeyName, table.Name);
        return QueryOf<T>(table.Model, partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of one partition of a table without a schema, by the key of any entity, for a mixed read: <c>shop.Query(Order.Keys.Pk.Of(id)).Of&lt;Customer, Order&gt;()</c>.</summary>
    /// <typeparam name="TEntity">The entity the key belongs to.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The condition on the sort key, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is the default value, it was built for another attribute, or there is a condition on a sort key and the table has none.</exception>
    public static QueryCommand<Item> Query<TEntity>(this Table<Item> table, PartitionKey<TEntity> partitionKey, SortKeyCondition sortKey = default)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(table);
        ThrowIfOtherAttribute(partitionKey, table.PartitionKeyName, table.Name, schemaless: true);
        return QueryOf<Item>(table.Model, partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of several partitions by typed keys, merged by sort key: <c>orders.Query(shards.Select(Order.Keys.Pk.Of))</c>. It costs at least one read per partition, see <see cref="FanOutQueryCommand{T}"/>.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is the default value, was built for another attribute or occurs twice, which the message places by position, or there is a condition on a sort key and the table has none.</exception>
    public static FanOutQueryCommand<T> Query<T>(this Table<T> table, IEnumerable<PartitionKey<T>> partitionKeys, SortKeyCondition sortKey = default)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        return FanOutQueryCommand<T>.Start(KeysOf(partitionKeys, table.PartitionKeyName, table.Name), sortKey, table.Model, table.Model);
    }

    /// <summary>Starts a query of several partitions of a table without a schema, by the keys of any entity. It costs at least one read per partition, see <see cref="FanOutQueryCommand{T}"/>.</summary>
    /// <typeparam name="TEntity">The entity the keys belong to.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is the default value, was built for another attribute or occurs twice, which the message places by position, or there is a condition on a sort key and the table has none.</exception>
    public static FanOutQueryCommand<Item> Query<TEntity>(this Table<Item> table, IEnumerable<PartitionKey<TEntity>> partitionKeys, SortKeyCondition sortKey = default)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(table);
        return FanOutQueryCommand<Item>.Start(KeysOf(partitionKeys, table.PartitionKeyName, table.Name, schemaless: true), sortKey, table.Model, table.Model);
    }

    /// <summary>
    /// Starts a query of one partition of an index by a typed key: <c>byStatus.Query(Order.Keys.ByStatusPk.Of(status))</c>. The key of another entity is a compile error. The partition key of the
    /// table is of the same type, so that one fails here, see <see cref="PartitionKey{TEntity}"/>, unless the index has that attribute as well. See <see cref="DynaSharp.Index{T}"/>
    /// for what is different about reading an index.
    /// </summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="partitionKey">The partition key of the index.</param>
    /// <param name="sortKey">The condition on the sort key of the index, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is the default value, it was built for another attribute, or there is a condition on a sort key and the index has none.</exception>
    public static QueryCommand<T> Query<T>(this Index<T> index, PartitionKey<T> partitionKey, SortKeyCondition sortKey = default)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(index);
        ThrowIfOtherAttribute(partitionKey, index.PartitionKeyName, index.Name);
        return QueryOf<T>(index.Model, partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of one partition of an index without a schema, by the key of any entity, for a mixed read. See <see cref="DynaSharp.Index{T}"/> for what is different about reading an index.</summary>
    /// <typeparam name="TEntity">The entity the key belongs to.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="partitionKey">The partition key of the index.</param>
    /// <param name="sortKey">The condition on the sort key of the index, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is the default value, it was built for another attribute, or there is a condition on a sort key and the index has none.</exception>
    public static QueryCommand<Item> Query<TEntity>(this Index<Item> index, PartitionKey<TEntity> partitionKey, SortKeyCondition sortKey = default)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(index);
        ThrowIfOtherAttribute(partitionKey, index.PartitionKeyName, index.Name, schemaless: true);
        return QueryOf<Item>(index.Model, partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of several partitions of an index by typed keys, merged by the sort key of the index. It costs at least one read per partition, see <see cref="FanOutQueryCommand{T}"/>.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is the default value, was built for another attribute or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    public static FanOutQueryCommand<T> Query<T>(this Index<T> index, IEnumerable<PartitionKey<T>> partitionKeys, SortKeyCondition sortKey = default)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(index);
        return FanOutQueryCommand<T>.Start(KeysOf(partitionKeys, index.PartitionKeyName, index.Name), sortKey, index.Model, index.Table.Model);
    }

    /// <summary>Starts a query of several partitions of an index without a schema, by the keys of any entity. It costs at least one read per partition, see <see cref="FanOutQueryCommand{T}"/>.</summary>
    /// <typeparam name="TEntity">The entity the keys belong to.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is the default value, was built for another attribute or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    public static FanOutQueryCommand<Item> Query<TEntity>(this Index<Item> index, IEnumerable<PartitionKey<TEntity>> partitionKeys, SortKeyCondition sortKey = default)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(index);
        return FanOutQueryCommand<Item>.Start(KeysOf(partitionKeys, index.PartitionKeyName, index.Name, schemaless: true), sortKey, index.Model, index.Table.Model);
    }

    /// <summary>
    /// Starts a query of an index keyed by the discriminator of the entity, whose partition holds every item of it: <c>orders.Index("ByType", "_type", "sk").QueryByDiscriminator()</c>.
    /// No generated builder makes that key, because the discriminator is no property.
    /// </summary>
    /// <remarks>
    /// <b>Keyed by the discriminator.</b> The partition key of the index has to be the attribute of the discriminator, else this throws <see cref="ArgumentException"/>, as a key of another
    /// attribute does. DynamoDB refuses a filter on a key, so the key condition decides which items are read, see <see cref="DynaSharp.Index{T}"/>.
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="sortKey">The condition on the sort key of the index, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no discriminator.</exception>
    /// <exception cref="ArgumentException">The partition key of the index is another attribute, or there is a condition on a sort key and the index has none.</exception>
    public static QueryCommand<T> QueryByDiscriminator<T>(this Index<T> index, SortKeyCondition sortKey = default)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(index);
        var info = T.Info;
        if (info.Discriminator is not { } discriminator)
        {
            throw new InvalidOperationException($"{typeof(T)} has no discriminator, so no index is keyed by it.");
        }

        return index.Query(new PartitionKey<T>(discriminator, info.DiscriminatorAttributeName), sortKey);
    }

    /// <summary>Starts a get of the item under the key of an entity, which is how a reload reads.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="entity">The entity to take the key from.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    public static GetCommand<T> Get<T>(this Table<T> table, T entity)
        where T : IDynamoDbEntity<T>
    {
        var state = BeginWithKeyOf(table, entity);
        return new(state, state.Table);
    }

    /// <summary>
    /// Starts a <c>DeleteItem</c> of the item under the key of an entity. An entity with a <see cref="VersionAttribute"/> gives its version as well, so the delete happens only if the item
    /// still has that version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version check</b> is that of <see cref="IfVersion{T}(DeleteCommand{T}, long)"/>: if the item has another version, sending throws <see cref="ConditionalCheckFailedException"/>.
    /// <see cref="IgnoreVersion{T}(DeleteCommand{T})"/> gives the check up, and <see cref="IfVersion{T}(DeleteCommand{T}, long)"/> checks another version in its place. The other attributes of
    /// the entity play no part, and an entity without a version gives only its key.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key: an entity at 0 deletes nothing, and fails on any item under its key,
    /// also on one without a version attribute, which <see cref="IgnoreVersion{T}(DeleteCommand{T})"/> deletes, and which <see cref="VersionMissing.Allow"/> deletes at 0, by looking at the
    /// version attribute instead, as it deletes one that holds 0.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="entity">The entity to take the key and the version from.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The version of the entity does not fit a <see cref="long"/>.</exception>
    public static DeleteCommand<T> Delete<T>(this Table<T> table, T entity)
        where T : IDynamoDbEntity<T>
    {
        var state = WriteRules.ExpectVersionOf(BeginWithKeyOf(table, entity), entity);
        return new(state, state.Table);
    }

    /// <summary>
    /// Updates only if the item has this version, and stores the next one, which <see cref="WriteResult{T}.Version"/> returns. If the item has another version, sending throws
    /// <see cref="ConditionalCheckFailedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required for a versioned entity.</b> A key does not carry a version, so an update of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(UpdateCommand{T})"/>. Nothing is read or retried by itself:
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> is how to ask for that, around code that reads the version again.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key and fails on any item under the key, also on one without a version
    /// attribute, which <see cref="IgnoreVersion{T}(UpdateCommand{T})"/> writes to. With <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and writes to an item
    /// without one or with 0 in it.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="version">The version last read.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>, which has no next one.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no <see cref="VersionAttribute"/>.</exception>
    public static UpdateCommand<T> IfVersion<T>(this UpdateCommand<T> command, long version)
        where T : IDynamoDbEntity<T>
    {
        WriteRules.CheckVersion(version);
        var state = command.Enter();
        WriteRules.AdvanceVersion(state, version);
        return new(state, state.Table);
    }

    /// <summary>
    /// Stores the inputs of the key of the table as attributes of the item, as a <c>Put</c> does, for an update that may create the item: without it, an item an update created holds the
    /// key alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is written.</b> <c>SET #userId = if_not_exists(#userId, :v)</c> per input, after the actions of the update and the keys of its indexes and before the discriminator. An item that
    /// exists keeps what it has, so this goes with <c>IfExists()</c>, <c>IfNotExists()</c> and a version alike. The value goes through the converter of the property and is the value, not the
    /// text of the key, and an action of the update on the attribute by a path given as a string takes precedence.
    /// </para>
    /// <para>
    /// <b>Why.</b> A read of the entity restores the inputs from the key, but <c>Select</c> of such an input and a filter on it find nothing in an item that holds the key alone. A plain key
    /// and a key made of one property over its own attribute are their attribute, and a <see cref="Mapping.KeyOnlyAttribute"/> has none, so an entity with nothing else changes nothing here.
    /// </para>
    /// <para>
    /// <b>A key given as strings.</b> A template that cuts a value off, such as a timestamp in <c>{At:yyyy-MM-dd}</c>, gives it back only from a key the generator built, so a key given as
    /// strings throws for it when the update ends.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no key inputs, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static UpdateCommand<T> StoreKeyInputs<T>(this UpdateCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        WriteRules.StoreKeyInputs(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Lets the update go out without the check of the version, and still stores the next version, so that a copy read before it fails its next checked write. <see cref="WriteResult{T}.Version"/> is
    /// <see langword="null"/>, because the update does not know the version it advanced from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The version still advances.</b> Every write of a versioned entity stores a new version, also one without the check: an update adds 1 to the stored version,
    /// <c>SET #v = if_not_exists(#v, :zero) + :one</c>, so an item without one, or a new item, gets version 1, and a put or a batch stores the version of its entity plus one, as a checked put does. A writer
    /// that holds a copy read before this write then fails with <see cref="ConditionalCheckFailedException"/>, or in a transaction with <see cref="TransactionCanceledException"/>, instead of overwriting it.
    /// What this write itself overwrites is not checked: it wins over whatever another writer stored since, so it suits a change that does not depend on what was read, such as an <c>Add</c> to a counter.
    /// </para>
    /// <para>
    /// <b>Setting the version yourself.</b> An update that sets, adds to or removes the version attribute by its own action keeps that action, and the version is not advanced besides it.
    /// <c>IfVersion(n)</c> after this checks <c>n</c> and stores <c>n + 1</c>; before it, this throws.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="InvalidOperationException">The update already has <c>IfVersion</c>, whose condition cannot be taken back.</exception>
    public static UpdateCommand<T> IgnoreVersion<T>(this UpdateCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion(isUpdate: true);
        return new(state, state.Table);
    }

    /// <summary>
    /// Writes the entity without the check of its version, for imports, migrations and an item written without a version, and still stores the next version, as a checked put does:
    /// <see cref="WriteResult{T}.Version"/> is the version of the entity plus one. Conditions of your own stay, it holds wherever it stands in the chain, and on an entity without a <see cref="VersionAttribute"/> it changes nothing.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TableExtensions.IgnoreVersion{T}(UpdateCommand{T})" path="/remarks/para[1]"/>
    /// <para>
    /// <b>Taking over an item without a version.</b> Such an item reads as version 0, and a checked put at 0 fails with <see cref="ConditionalCheckFailedException"/>, because 0 says that no item has the key.
    /// <c>orders.Put(order).IgnoreVersion()</c> of the entity as it was read stores version 1, and the next write is an ordinary one. For a table that keeps such items, <see cref="VersionMissing.Allow"/> is the better answer.
    /// </para>
    /// <para>
    /// <b>The last version.</b> An entity at the last version its property holds throws <see cref="OverflowException"/> at <see cref="Table{T}.Put(T)"/>, because it has no next one.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<T> IgnoreVersion<T>(this PutCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion();
        return new(state, state.Table);
    }

    /// <summary>
    /// Deletes only if the item has this version. If the item has another version, sending throws <see cref="ConditionalCheckFailedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required for a versioned entity.</b> A key does not carry a version, so a delete by a key of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(DeleteCommand{T})"/>; a delete of an entity takes the version from it, and this checks another one in its place.
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> repeats code that reads the version again.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key, and the delete fails on any item under the key. With
    /// <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and deletes an item without one or with 0 in it.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="version">The version last read.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is negative, or <see cref="long.MaxValue"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no <see cref="VersionAttribute"/>.</exception>
    public static DeleteCommand<T> IfVersion<T>(this DeleteCommand<T> command, long version)
        where T : IDynamoDbEntity<T>
    {
        WriteRules.CheckVersion(version);
        var state = command.Enter();
        WriteRules.IfVersion(state, version);
        return new(state, state.Table);
    }

    /// <summary>Lets the delete go out without the check.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static DeleteCommand<T> IgnoreVersion<T>(this DeleteCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        state.IgnoreVersion();
        return new(state, state.Table);
    }

    private static QueryCommand<T> QueryOf<T>(TableModel read, AttributeValue partitionKey, SortKeyCondition sortKey)
        where T : IDynamoDbDocument<T> => new(read.BeginQuery(partitionKey, sortKey), read);

    private static DynamoDbIndexInfo IndexOf<TEntity>(string name)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        foreach (var index in TEntity.Info.Indexes)
        {
            if (index.IndexName == name)
            {
                return index;
            }
        }

        throw new ArgumentException($"{typeof(TEntity)} declares no index \"{name}\".", nameof(name));
    }

    private static AttributeValue[] KeysOf<TEntity>(IEnumerable<PartitionKey<TEntity>> partitionKeys, string attributeName, string readName, bool schemaless = false)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(partitionKeys);
        var keys = new List<AttributeValue>();
        foreach (var key in partitionKeys)
        {
            ThrowIfOtherAttribute(key, attributeName, readName, schemaless);
            keys.Add(key.Key);
        }

        return [.. keys];
    }

    // The table and index of a key have one type, so this is the check the compiler cannot make, and a key of the wrong attribute would come back empty without a word from DynamoDB.
    private static void ThrowIfOtherAttribute<TEntity>(PartitionKey<TEntity> partitionKey, string attributeName, string readName, bool schemaless = false)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        if (partitionKey.AttributeName is { } builtFor && builtFor != attributeName)
        {
            throw new ArgumentException(
                $"This key was built for the attribute {builtFor}, and a query of {readName} reads {attributeName}, so it would find nothing. {BuilderOf<TEntity>(attributeName, schemaless)}",
                nameof(partitionKey));
        }
    }

    // Generated keys have the names of the model, so the name of a builder comes from the entity. An attribute no builder makes is the discriminator, which has a key of its own, or
    // one the entity does not declare: a table without a schema takes its value, and an entity needs the index declared.
    private static string BuilderOf<TEntity>(string attributeName, bool schemaless)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        var info = TEntity.Info;
        var builder = attributeName == info.PartitionKeyName ? "Pk" : info.Indexes.FirstOrDefault(index => index.PartitionKeyName == attributeName)?.PartitionKeyBuilder;
        if (builder is not null)
        {
            return $"Use {typeof(TEntity)}.Keys.{builder}.Of(...).";
        }

        if (attributeName == info.DiscriminatorAttributeName && info.Discriminator is not null)
        {
            return "Use QueryByDiscriminator() of the index.";
        }

        return schemaless ? "Pass the value of that attribute as an AttributeValue." : $"Declare the index on {typeof(TEntity)}, which generates the builder of its key.";
    }

    private static PutCommand<Item> Attribute<TValue, TConverter>(this PutCommand<Item> command, string name, TValue value)
        where TConverter : IDynamoDbConverter<TValue>
    {
        var state = command.Enter();
        if (!TConverter.IsOmitted(value))
        {
            var writer = new DynamoDbWriter(state.BodyJson);
            writer.WriteAttributeName(name);
            TConverter.Write(writer, value);
        }

        return new(state, state.Table);
    }

    private static CommandState BeginWithKeyOf<T>(Table<T> table, T entity)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        Argument.ThrowIfNull(entity);

        var state = table.Model.Begin();
        T.WriteKey(state.StartKey(), entity);
        state.EndKey();
        return state;
    }
}
