using System.Numerics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// The verbs of a table or index without a schema that take the values of a key as they are stored, and the attribute-by-attribute <c>Put()</c> with its <c>With</c>. These are extension
/// methods so that a table of an entity does not offer them: a key given as its values would skip its key template, and a put by attribute would write an item without its discriminator.
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
public static class ItemTableExtensions
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
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
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
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
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
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
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
        return QueryCommand<Item>.Start(table.Model, partitionKey, sortKey);
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
        return QueryCommand<Item>.Start(index.Model, partitionKey, sortKey);
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
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
    public static BatchGetCommand<Item> Get(this BatchGetCommand<Item> command, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        command.CheckedTable().CheckKey(partitionKey, sortKey);
        var state = command.Enter();
        var table = state.Table;
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
    /// <exception cref="ArgumentException">A key is empty, or the sort key is given and the table has none, or left out and the table has one.</exception>
    public static BatchWriteCommand<Item> Delete(this BatchWriteCommand<Item> command, AttributeValue partitionKey, AttributeValue sortKey = default)
    {
        command.CheckedTable().CheckKey(partitionKey, sortKey);
        var state = command.Enter();
        BatchWriteOperation.AddDelete(state, state.Table, partitionKey, sortKey);
        return new(state, state.Table);
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
}
