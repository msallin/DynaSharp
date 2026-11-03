using System.Text;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What every command on a table starts from: the client, the names, and the parts of the request that never change, encoded once so
/// that a command copies them. <see cref="Table{T}"/> is this with a type; an object of this class is what commands and misuse messages
/// hold, because they do not care about the type.
/// </summary>
internal sealed class TableModel
{
    /// <summary>Initializes a new instance of the <see cref="TableModel"/> class.</summary>
    /// <param name="core">The client.</param>
    /// <param name="name">The table name, or its ARN.</param>
    /// <param name="partitionKeyName">The attribute name of the partition key.</param>
    /// <param name="sortKeyName">The attribute name of the sort key, if the table has one.</param>
    /// <param name="discriminator">The value of the discriminator, if the table is typed and the type has one.</param>
    /// <param name="discriminatorAttributeName">The attribute of the discriminator, which a table of an entity always names, even if the entity has no discriminator.</param>
    /// <param name="indexName">The secondary index this model reads, if it is one.</param>
    /// <param name="table">The model of the table of an index.</param>
    /// <param name="isGlobalIndex">Whether the index is known to be global.</param>
    public TableModel(ClientCore core, string name, string partitionKeyName, string? sortKeyName, string? discriminator = null, string? discriminatorAttributeName = null, string? indexName = null, TableModel? table = null, bool isGlobalIndex = false)
    {
        Core = core;
        Name = name;
        PartitionKeyName = partitionKeyName;
        SortKeyName = sortKeyName;
        Discriminator = discriminator;
        DiscriminatorAttributeName = discriminatorAttributeName;
        IndexName = indexName;
        IsGlobalIndex = isGlobalIndex;
        ItemKeyNames = table?.ItemKeyNames ?? (sortKeyName is null ? [partitionKeyName] : [partitionKeyName, sortKeyName]);

        // The name of the attribute is what makes a table typed, and only a value is something to compare: a type without a discriminator of its own has its keys to read and no _type.
        RequiredNames = discriminatorAttributeName is null ? [] : NamesOfATypedRead(table, discriminator is null ? null : discriminatorAttributeName, partitionKeyName, sortKeyName);
        BodyStart = indexName is null
            ? [.. "{\"TableName\":\""u8, .. Encode(name), .. "\""u8]
            : [.. "{\"TableName\":\""u8, .. Encode(name), .. "\",\"IndexName\":\""u8, .. Encode(indexName), .. "\""u8];
        KeyStart = [.. ",\"Key\":{\""u8, .. Encode(partitionKeyName), .. "\":"u8];
        SortKeyStart = sortKeyName is null ? [] : [.. ",\""u8, .. Encode(sortKeyName), .. "\":"u8];
        RequestItemsStart = [.. "\""u8, .. Encode(name), .. "\":"u8];
        DiscriminatorUtf8 = discriminator is null ? [] : Encoding.UTF8.GetBytes(discriminator);
    }

    /// <summary>Gets the client.</summary>
    public ClientCore Core { get; }

    /// <summary>Gets the table name, or its ARN.</summary>
    public string Name { get; }

    /// <summary>Gets the attribute name of the partition key.</summary>
    public string PartitionKeyName { get; }

    /// <summary>Gets the attribute name of the sort key, if the table has one.</summary>
    public string? SortKeyName { get; }

    /// <summary>Gets the value of the discriminator, if the table is typed and the type has one.</summary>
    public string? Discriminator { get; }

    /// <summary>Gets the attribute of the discriminator.</summary>
    public string? DiscriminatorAttributeName { get; }

    /// <summary>Gets what a <c>GetItem</c> does about items of other types: it has no filter, so it looks at the item.</summary>
    public DiscriminatorCheck DiscriminatorOnGet => Discriminator is null ? DiscriminatorCheck.None : DiscriminatorCheck.CheckEachItem;

    /// <summary>
    /// Gets what a <c>Query</c> does about items of other types. DynamoDB refuses a filter on a key of what a query reads, so a query keyed by the discriminator goes without, as a <c>GetItem</c> does: its key
    /// condition stands where the filter would, and says which items come back. Every other query filters, as a <c>Scan</c> does.
    /// </summary>
    public DiscriminatorCheck DiscriminatorOnQuery => DiscriminatorIsKey ? DiscriminatorOnGet : DiscriminatorOnScan;

    /// <summary>Gets what a <c>Scan</c> does about items of other types: it filters, which DynamoDB takes on any attribute.</summary>
    public DiscriminatorCheck DiscriminatorOnScan => Discriminator is null ? DiscriminatorCheck.None : DiscriminatorCheck.Filter;

    /// <summary>Gets the secondary index this model reads, if it is one.</summary>
    public string? IndexName { get; }

    /// <summary>Gets a value indicating whether this model reads an index that is known to be global.</summary>
    public bool IsGlobalIndex { get; }

    /// <summary>Gets the attributes that identify an item: the keys of the table, also for an index, where two items can have one key.</summary>
    public string[] ItemKeyNames { get; }

    /// <summary>
    /// Gets a value indicating whether an update has an action on the discriminator, because it can create the item. That is every update of a type with one, unless the attribute is a key
    /// of the table, which every item has and DynamoDB does not let an update touch.
    /// </summary>
    public bool UpdateWritesDiscriminator => Discriminator is not null && !DiscriminatorIsKey;

    /// <summary>
    /// Gets what a write of the entity owes its version and its keys, or <see langword="null"/> if there is nothing, which a table without an entity never has. An index, which is not written, has the one of its
    /// table, so that a read of one attribute knows the version.
    /// </summary>
    public EntityWrites? Writes { get; init; }

    /// <summary>Gets what the entity of the table declares, or <see langword="null"/> for a table without an entity, which is what creating the table starts from.</summary>
    public DynamoDbEntityInfo? Entity { get; init; }

    /// <summary>Gets the name a read of an item goes by in a failure, the one generated code gives its type, so that a read of one attribute fails as a read of the entity does.</summary>
    public string ReadTypeName { get; init; } = nameof(Item);

    /// <summary>
    /// Gets the attributes a typed read needs in its projection: the discriminator, which the read checks, and the keys, which an entity is restored
    /// from. An <see cref="Item"/> needs none.
    /// </summary>
    public string[] RequiredNames { get; }

    /// <summary>Gets the start of every body: <c>{"TableName":"Shop"</c>, followed by the index for a read of one.</summary>
    public byte[] BodyStart { get; }

    /// <summary>Gets what comes before the value of the partition key: <c>,"Key":{"pk":</c>.</summary>
    public byte[] KeyStart { get; }

    /// <summary>Gets what comes before the value of the sort key: <c>,"sk":</c>.</summary>
    public byte[] SortKeyStart { get; }

    /// <summary>Gets what names the table in the <c>RequestItems</c> of a batch: <c>"Shop":</c>.</summary>
    public byte[] RequestItemsStart { get; }

    /// <summary>Gets the value of the discriminator as UTF-8, which a read of one attribute compares an item with.</summary>
    public byte[] DiscriminatorUtf8 { get; }

    private bool DiscriminatorIsKey => IsKey(DiscriminatorAttributeName);

    /// <summary>
    /// Makes what stands for all tables of a transaction, which is opened on the client and has none of its own yet. Its name only shows in the
    /// message about a stale command and in the debugger, and none of its keys is ever written.
    /// </summary>
    /// <param name="core">The client.</param>
    /// <returns>The model.</returns>
    public static TableModel ForTransactions(ClientCore core) => new(core, "several tables", "none", null);

    /// <summary>Finds out whether a key has nothing in it, which DynamoDB rejects in a message that does not say which call it was.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> for no value, an empty string and an empty binary value.</returns>
    public static bool IsEmptyKey(AttributeValue key) =>
        key.Type == AttributeType.None
        || (key.Type == AttributeType.String && key.AsString().Length == 0)
        || (key.Type == AttributeType.Binary && key.AsBinary().IsEmpty);

    /// <summary>Starts a command.</summary>
    /// <returns>The state of the command.</returns>
    public CommandState Begin()
    {
        var state = Core.Commands.Rent();
        state.Start(this);
        return state;
    }

    /// <summary>Starts a command on a key of attribute values. Everything that is wrong with the key is found first, at the call that passed it.</summary>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The sort key, or the default value if there is none.</param>
    /// <returns>The state of the command.</returns>
    /// <exception cref="ArgumentException">A key is empty, or there is a sort key and the table has none.</exception>
    public CommandState Begin(AttributeValue partitionKey, AttributeValue sortKey)
    {
        CheckKey(partitionKey, sortKey);
        var state = Begin();
        state.WriteKey(partitionKey, sortKey);
        return state;
    }

    /// <summary>Starts a command of several actions, whose request does not start with one table.</summary>
    /// <returns>The state of the command.</returns>
    public CommandState BeginActions()
    {
        var state = Core.Commands.Rent();
        state.Table = this;
        return state;
    }

    /// <summary>Finds out whether an attribute is a key of what this model reads, which DynamoDB refuses in the filter of a query.</summary>
    /// <param name="attributeName">The attribute.</param>
    /// <returns><see langword="true"/> for the partition key and the sort key.</returns>
    public bool IsKey(string? attributeName) => attributeName == PartitionKeyName || attributeName == SortKeyName;

    /// <summary>Finds what is wrong with a key at the call that passed it.</summary>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The sort key, or the default value if there is none.</param>
    /// <exception cref="ArgumentException">A key is empty, or there is a sort key and the table has none.</exception>
    public void CheckKey(AttributeValue partitionKey, AttributeValue sortKey)
    {
        ThrowIfEmpty(partitionKey, nameof(partitionKey));
        if (sortKey.Type != AttributeType.None)
        {
            ThrowIfEmpty(sortKey, nameof(sortKey));
            if (SortKeyName is null)
            {
                throw new ArgumentException($"The table {Name} was opened without a sort key, so a key of it has none.", nameof(sortKey));
            }
        }
    }

    /// <summary>Starts a <c>Query</c>: the key condition is written here, and the discriminator of a typed read joins the filter.</summary>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The condition on the sort key.</param>
    /// <returns>The state of the command.</returns>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the table or index has none.</exception>
    public CommandState BeginQuery(AttributeValue partitionKey, SortKeyCondition sortKey)
    {
        ThrowIfEmpty(partitionKey, nameof(partitionKey));
        return BeginKeyCondition(partitionKey, sortKey);
    }

    /// <summary>Starts a <c>Query</c> of the items under a key that writes itself: equal to each of its attributes, and the discriminator of a typed read in the filter.</summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="key">The key.</param>
    /// <returns>The state of the command.</returns>
    public CommandState BeginKeyQuery<TKey>(TKey key)
        where TKey : IKey
    {
        var state = Begin();
        state.WriteKeyCondition(key);
        AddDiscriminatorFilter(state, DiscriminatorOnQuery);
        return state;
    }

    /// <summary>Starts what the queries of a fan-out are copies of. It is <see cref="BeginQuery"/> with <c>:pk</c> for the partition, whose value each copy adds.</summary>
    /// <param name="sortKey">The condition on the sort key.</param>
    /// <returns>The state of the command.</returns>
    /// <exception cref="ArgumentException">There is a condition on a sort key and the table or index has none.</exception>
    public CommandState BeginFanOut(SortKeyCondition sortKey) => BeginKeyCondition(default, sortKey);

    /// <summary>Starts a <c>Scan</c>, with the discriminator of a typed read in its filter.</summary>
    /// <returns>The state of the command.</returns>
    public CommandState BeginScan()
    {
        var state = Begin();
        AddDiscriminatorFilter(state, DiscriminatorOnScan);
        return state;
    }

    /// <summary>Creates the model of a secondary index of this table.</summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="partitionKeyName">The attribute name of its partition key.</param>
    /// <param name="sortKeyName">The attribute name of its sort key, if it has one.</param>
    /// <param name="isGlobal">Whether the entity says that the index is global. A local index has the partition key of its table, so another partition key says the same.</param>
    /// <returns>The model.</returns>
    public TableModel ForIndex(string indexName, string partitionKeyName, string? sortKeyName, bool isGlobal = false) =>
        new(Core, Name, partitionKeyName, sortKeyName, Discriminator, DiscriminatorAttributeName, indexName, this, isGlobal || partitionKeyName != PartitionKeyName) { ReadTypeName = ReadTypeName, Writes = Writes };

    /// <summary>Stops a consistent read of a global index before it goes out, which DynamoDB would reject at the end of a round trip.</summary>
    /// <exception cref="InvalidOperationException">This model reads an index that is known to be global.</exception>
    public void ThrowIfNotConsistent()
    {
        if (IsGlobalIndex)
        {
            throw new InvalidOperationException($"{IndexName} is a global secondary index, which DynamoDB only reads eventually consistently, so it rejects Consistent(). Read the table or a local index for that.");
        }
    }

    /// <summary>Completes the projection of a typed read with what the read needs. It runs when the chain ends, because a projection can grow until then.</summary>
    /// <param name="state">The state of the read.</param>
    public void AddRequiredNames(CommandState state) => state.Expressions.CompleteProjection(RequiredNames);

    private static string[] NamesOfATypedRead(TableModel? table, string? discriminatorAttributeName, string partitionKeyName, string? sortKeyName)
    {
        var names = new List<string>(table?.RequiredNames ?? []);
        if (table is null && discriminatorAttributeName is not null)
        {
            names.Add(discriminatorAttributeName);
        }

        names.Add(partitionKeyName);
        if (sortKeyName is not null)
        {
            names.Add(sortKeyName);
        }

        return [.. names];
    }

    private static byte[] Encode(string text) => JsonEncodedText.Encode(text, WireJson.WriterOptions.Encoder).EncodedUtf8Bytes.ToArray();

    // Everything beyond an empty key is left to DynamoDB.
    private static void ThrowIfEmpty(AttributeValue key, string parameterName)
    {
        if (IsEmptyKey(key))
        {
            throw new ArgumentException("A key attribute cannot be empty.", parameterName);
        }
    }

    // #n0 = :v0 AND ..., or #n0 = :pk for a partition that is not known yet.
    private CommandState BeginKeyCondition(AttributeValue partitionKey, SortKeyCondition sortKey)
    {
        if (sortKey.IsSet && SortKeyName is null)
        {
            throw new ArgumentException($"The table or index {IndexName ?? Name} was opened without a sort key, so a query of it has no condition on one.", nameof(sortKey));
        }

        var state = Begin();
        var expressions = state.Expressions;
        var text = expressions[Clause.KeyCondition];
        expressions.AppendAttributeName(text, PartitionKeyName);
        text.Append(" = "u8);
        if (partitionKey.Type == AttributeType.None)
        {
            text.Append(":pk"u8);
        }
        else
        {
            expressions.AppendValue<AttributeValue, AttributeValueConverter>(text, partitionKey);
        }

        sortKey.Render(expressions, text, SortKeyName!);
        AddDiscriminatorFilter(state, DiscriminatorOnQuery);
        return state;
    }

    // So that a typed read of a table shared by several types sees its own items. It costs two placeholders, and the filter runs after the read, so capacity stays what it was.
    private void AddDiscriminatorFilter(CommandState state, DiscriminatorCheck check)
    {
        if (check == DiscriminatorCheck.Filter)
        {
            var expressions = state.Expressions;
            var text = expressions.StartPending();
            expressions.AppendAttributeName(text, DiscriminatorAttributeName!);
            text.Append(" = "u8);
            expressions.AppendValue<string, StringConverter>(text, Discriminator!);
            expressions.CommitPending(Clause.Filter);
        }
    }
}
