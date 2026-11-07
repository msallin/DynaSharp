using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// The verbs of a table or index of an entity that take what the entity builds: a partition key of <c>Keys</c> for a query, the entity itself for a get or a delete, and an index by
/// the name the entity declares. These are extension methods because they depend on the type of the table: a table without a schema takes such a key too, for a mixed read.
/// </summary>
/// <example>
/// <code>
/// await foreach (var order in orders.Query(Order.Keys.Pk.Of("alice")).ItemsAsync(ct))
/// {
///     Console.WriteLine(order.Total);
/// }
/// </code>
/// </example>
public static class EntityTableExtensions
{
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
        return QueryCommand<T>.Start(table.Model, partitionKey.Key, sortKey);
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
        return QueryCommand<Item>.Start(table.Model, partitionKey.Key, sortKey);
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
        return QueryCommand<T>.Start(index.Model, partitionKey.Key, sortKey);
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
        return QueryCommand<Item>.Start(index.Model, partitionKey.Key, sortKey);
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
    /// <b>The version check</b> is that of <see cref="EntityWriteExtensions.IfVersion{T}(DeleteCommand{T}, long)"/>: if the item has another version, sending throws <see cref="ConditionalCheckFailedException"/>.
    /// <see cref="EntityWriteExtensions.IgnoreVersion{T}(DeleteCommand{T})"/> gives the check up, and <see cref="EntityWriteExtensions.IfVersion{T}(DeleteCommand{T}, long)"/> checks another version in its place. The other attributes of
    /// the entity play no part, and an entity without a version gives only its key.
    /// </para>
    /// <para>
    /// <b>Version 0</b> stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key: an entity at 0 deletes nothing, and fails on any item under its key,
    /// also on one without a version attribute, which <see cref="EntityWriteExtensions.IgnoreVersion{T}(DeleteCommand{T})"/> deletes, and which <see cref="VersionMissing.Allow"/> deletes at 0, by looking at the
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

    /// <summary>Adds a delete of the item under the key of an entity to a batch, for an entity that was read: <c>batch.Delete(order)</c>. Deleting what does not exist is no error.</summary>
    /// <remarks>
    /// <b>Only the key.</b> A batch has no conditions, so the other attributes of the entity play no part, its version included: a batch that writes an entity with a
    /// <see cref="VersionAttribute"/> needs <see cref="BatchWriteCommand{T}.IgnoreVersion"/>, as its puts do, and deletes whatever version is stored.
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The batch.</param>
    /// <param name="entity">The entity to take the key from.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <see langword="null"/>.</exception>
    public static BatchWriteCommand<T> Delete<T>(this BatchWriteCommand<T> command, T entity)
        where T : IDynamoDbEntity<T>
    {
        Argument.ThrowIfNull(entity);
        var state = command.Enter();
        BatchWriteOperation.AddDelete(state, state.Table, new EntityKey<T>(entity));
        return new(state, state.Table);
    }

    /// <summary>
    /// Adds a delete of the item under the key of each entity, any number of them, which <see cref="BatchWriteCommand{T}.SendAsync"/> sends in requests of 25 writes: <c>batch.DeleteAll(orders)</c>
    /// for what a query read. Deleting what does not exist is no error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Enumerated here.</b> The keys are written into memory at the call, so the sequence is enumerated once, before anything is sent, and an entity that fails here leaves nothing written.
    /// </para>
    /// <inheritdoc cref="Delete{T}(BatchWriteCommand{T}, T)" path="/remarks"/>
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The batch.</param>
    /// <param name="entities">The entities to take the keys from.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entities"/> or one of them is <see langword="null"/>.</exception>
    public static BatchWriteCommand<T> DeleteAll<T>(this BatchWriteCommand<T> command, IEnumerable<T> entities)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(entities);
        var state = command.Enter();
        foreach (var entity in entities)
        {
            Argument.ThrowIfNull(entity, nameof(entities));
            BatchWriteOperation.AddDelete(state, state.Table, new EntityKey<T>(entity));
        }

        return new(state, state.Table);
    }

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
