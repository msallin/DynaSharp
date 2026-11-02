using System.Numerics;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// What only some tables can do. These are extension methods so that the rest does not compile: a table of an entity has no attribute-by-attribute
/// <c>Put()</c>, because it would write an item without its discriminator, and a table without a schema has no key inside an item.
/// </summary>
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
    /// <param name="value">The value; strings and booleans convert at no cost.</param>
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
    /// Starts a query of one partition by a typed key: <c>orders.Query(Order.Keys.Pk.Of(id), Order.Keys.Sk.BeginsWith())</c>. The key of another entity is a compile error, which is what this
    /// has over <see cref="Table{T}.Query(AttributeValue, SortKeyCondition)"/>. It is an extension method so that no call that binds to a member can change: this applies only when none does.
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
        return table.Query(partitionKey.Key, sortKey);
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
        ThrowIfOtherAttribute(partitionKey, table.PartitionKeyName, table.Name);
        return table.Query(partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of several partitions by typed keys, merged by sort key: <c>orders.Query(shards.Select(Order.Keys.Pk.Of))</c>. See <see cref="Table{T}.Query(IEnumerable{AttributeValue}, SortKeyCondition)"/>.</summary>
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
        return table.Query(KeysOf(partitionKeys, table.PartitionKeyName, table.Name), sortKey);
    }

    /// <summary>Starts a query of several partitions of a table without a schema, by the keys of any entity. See <see cref="Table{T}.Query(IEnumerable{AttributeValue}, SortKeyCondition)"/>.</summary>
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
        return table.Query(KeysOf(partitionKeys, table.PartitionKeyName, table.Name), sortKey);
    }

    /// <summary>
    /// Starts a query of one partition of an index by a typed key: <c>byStatus.Query(Order.Keys.ByStatusPk.Of(status))</c>. The key of another entity is a compile error. The partition key of the
    /// table is of the same type, so that one fails here, see <see cref="PartitionKey{TEntity}"/>, unless the index has that attribute as well. See <see cref="Index{T}.Query(AttributeValue, SortKeyCondition)"/>
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
        return index.Query(partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of one partition of an index without a schema, by the key of any entity, for a mixed read. See <see cref="Index{T}.Query(AttributeValue, SortKeyCondition)"/>.</summary>
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
        ThrowIfOtherAttribute(partitionKey, index.PartitionKeyName, index.Name);
        return index.Query(partitionKey.Key, sortKey);
    }

    /// <summary>Starts a query of several partitions of an index by typed keys, merged by the sort key of the index. See <see cref="Index{T}.Query(IEnumerable{AttributeValue}, SortKeyCondition)"/>.</summary>
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
        return index.Query(KeysOf(partitionKeys, index.PartitionKeyName, index.Name), sortKey);
    }

    /// <summary>Starts a query of several partitions of an index without a schema, by the keys of any entity. See <see cref="Index{T}.Query(IEnumerable{AttributeValue}, SortKeyCondition)"/>.</summary>
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
        return index.Query(KeysOf(partitionKeys, index.PartitionKeyName, index.Name), sortKey);
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
    /// Starts a delete of the item under the key of an entity. An entity with a <see cref="VersionAttribute"/> gives its version as well, so the delete happens only if the item still has the
    /// version the entity holds, as with <see cref="IfVersion{T}(DeleteCommand{T}, long)"/>. Version 0 stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition
    /// key: an entity at 0 deletes nothing, and fails on any item under its key, also on one without a version attribute, which <see cref="IgnoreVersion{T}(DeleteCommand{T})"/> deletes, and which
    /// <see cref="VersionMissing.Allow"/> deletes at 0, by looking at the version attribute instead, as it deletes one that holds 0. Otherwise sending throws
    /// <see cref="ConditionalCheckFailedException"/>. <see cref="IgnoreVersion{T}(DeleteCommand{T})"/> gives the check up, and <see cref="IfVersion{T}(DeleteCommand{T}, long)"/> checks another version
    /// in its place. The other attributes of the entity play no part. It costs nothing for an entity without a version.
    /// </summary>
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
    /// Updates only if the item has this version, and stores the next one, which <see cref="WriteResult{T}.Version"/> returns. A version of 0 stands for an item that does not exist, so it
    /// renders <c>attribute_not_exists</c> on the partition key and fails on any item under the key, also on one without a version attribute, which <see cref="IgnoreVersion{T}(UpdateCommand{T})"/> writes to. With
    /// <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and writes to an item without one or with 0 in it.
    /// A key does not carry a version, so an update of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(UpdateCommand{T})"/>. If the item has another version, sending throws <see cref="ConditionalCheckFailedException"/>, and nothing is read or
    /// retried by itself: <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> is how to ask for that, around code that reads the version again.
    /// </summary>
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
    /// Stores the inputs of the key of the table as attributes of the item, as a <c>Put</c> does, for an update that may create it: <c>SET #userId = if_not_exists(#userId, :v)</c> per input, after the actions of the update and the
    /// keys of its indexes and before the discriminator. Without it an item an update created holds the key alone: a read of the entity restores the inputs from the key, and <c>Select</c> of such an input and a filter on it find nothing.
    /// An item that exists keeps what it has, so this goes with <c>IfExists()</c>, <c>IfNotExists()</c> and a version alike. The value goes through the converter of the property and is the value, not the text of the key, and an
    /// action of the update on the attribute by a raw path takes precedence. A plain key and a key made of one property over its own attribute are their attribute, and a <see cref="Mapping.KeyOnlyAttribute"/> has none, so
    /// an entity with nothing else changes nothing here.
    /// </summary>
    /// <remarks>
    /// A template that cuts a value off, such as a timestamp in <c>{At:yyyy-MM-dd}</c>, gives it back only from a generated key, so a key given as strings throws for it when the update ends. It costs an
    /// <see cref="AttributeValue"/> per input, and an update without it pays nothing.
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

    /// <summary>Lets the update go out without the check and without a new version, which leaves the version as it is. A later writer that holds it is not told about this update.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static UpdateCommand<T> IgnoreVersion<T>(this UpdateCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        WriteRules.IgnoreVersion(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Writes the entity as it is, with no condition on its version and no new version, for imports and migrations that bring their versions along: the item ends up as a batch that
    /// ignores versions writes it, and <see cref="WriteResult{T}.Version"/> is the version of the entity. Conditions of your own stay, and it holds wherever it stands in the chain. On an entity
    /// without a <see cref="VersionAttribute"/> it changes nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is also how a single item without a version attribute is taken over; for a table that keeps such items, <see cref="VersionMissing.Allow"/> is the better answer. Such an item reads as version 0, and a put at 0
    /// fails with <see cref="ConditionalCheckFailedException"/>, because 0 says that no item has the key. <c>orders.Put(order with { Revision = 1 }).IgnoreVersion()</c> stores version 1, and the next write is an ordinary
    /// one. It has to be above 0: an item stored at 0 reads as 0 and fails that next write the same way.
    /// </para>
    /// <para>
    /// <see cref="Table{T}.Put(T)"/> has written the item with the next version by now, so this writes it a second time, and a change to the entity since then shows. That same first
    /// write is why an entity at the last version its property holds throws <see cref="OverflowException"/> at the put, before this can say anything.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity. A table without a schema has no version, so it does not compile.</typeparam>
    /// <param name="command">The command.</param>
    /// <returns>The command, which replaces this one.</returns>
    public static PutCommand<T> IgnoreVersion<T>(this PutCommand<T> command)
        where T : IDynamoDbEntity<T>
    {
        var state = command.Enter();
        WriteRules.IgnoreVersion(state);
        return new(state, state.Table);
    }

    /// <summary>
    /// Deletes only if the item has this version. Version 0 stands for an item that does not exist, so it renders <c>attribute_not_exists</c> on the partition key, and the delete fails on any item under the key. With
    /// <see cref="VersionMissing.Allow"/> it looks at the version attribute instead, and deletes an item without one or with 0 in it.
    /// A key does not carry a version, so a delete by a key of an entity with a <see cref="VersionAttribute"/> needs this or
    /// <see cref="IgnoreVersion{T}(DeleteCommand{T})"/>; a delete of an entity takes the version from it, and this checks another one in its place. If the item has another version, sending throws
    /// <see cref="ConditionalCheckFailedException"/>; <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> repeats code that reads the version again.
    /// </summary>
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
        WriteRules.IgnoreVersion(state);
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

    private static AttributeValue[] KeysOf<TEntity>(IEnumerable<PartitionKey<TEntity>> partitionKeys, string attributeName, string readName)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        ArgumentNullException.ThrowIfNull(partitionKeys);
        var keys = new List<AttributeValue>();
        foreach (var key in partitionKeys)
        {
            ThrowIfOtherAttribute(key, attributeName, readName);
            keys.Add(key.Key);
        }

        return [.. keys];
    }

    // The table and index of a key have one type, so this is the check the compiler cannot make, and a key of the wrong attribute would come back empty without a word from DynamoDB.
    private static void ThrowIfOtherAttribute<TEntity>(PartitionKey<TEntity> partitionKey, string attributeName, string readName)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        if (partitionKey.AttributeName is { } builtFor && builtFor != attributeName)
        {
            throw new ArgumentException(
                $"This key was built for the attribute {builtFor}, and a query of {readName} reads {attributeName}, so it would find nothing. {BuilderOf<TEntity>(attributeName)}",
                nameof(partitionKey));
        }
    }

    // Generated keys have the names of the model, so the name of a builder comes from the entity.
    private static string BuilderOf<TEntity>(string attributeName)
        where TEntity : IDynamoDbEntity<TEntity>
    {
        var info = TEntity.Info;
        var builder = attributeName == info.PartitionKeyName ? "Pk" : info.Indexes.FirstOrDefault(index => index.PartitionKeyName == attributeName)?.PartitionKeyBuilder;
        return builder is null
            ? "Pass the value of that attribute as an AttributeValue."
            : $"Use {typeof(TEntity)}.Keys.{builder}.Of(...).";
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
