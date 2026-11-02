using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.Fluent;

/// <summary>
/// The schema of a table as the union of what its entities declare, because one entity does not describe a single table. Key attribute names are what entities have to agree on: an overloaded
/// index has several templates over one attribute, and the table knows nothing of templates. Types that nobody states stay open, so a type an entity does state fills them in and never conflicts.
/// </summary>
internal sealed class TableSchema
{
    private readonly List<KeyAttribute> _attributes = [];

    private readonly List<DeclaredIndex> _indexes = [];

    private readonly string _owner;

    private readonly string _partitionKey;

    private readonly string? _sortKey;

    /// <summary>Initializes a new instance of the <see cref="TableSchema"/> class with the key of the table and nothing about its type.</summary>
    /// <param name="owner">Who says that this is the key, as a message names it.</param>
    /// <param name="partitionKey">The attribute name of the partition key.</param>
    /// <param name="sortKey">The attribute name of the sort key, if the table has one.</param>
    public TableSchema(string owner, string partitionKey, string? sortKey)
    {
        _owner = owner;
        _partitionKey = partitionKey;
        _sortKey = sortKey;
        Define(partitionKey, ScalarAttributeType.Unknown, owner);
        if (sortKey is not null)
        {
            Define(sortKey, ScalarAttributeType.Unknown, owner);
        }
    }

    /// <summary>Gets the global secondary indexes of the union, which a wait for the table waits for.</summary>
    public IReadOnlyList<string> GlobalIndexNames => TableAdministration.GlobalIndexNames(_indexes.Select(static declared => declared.Index));

    /// <summary>Adds what an entity declares.</summary>
    /// <param name="entity">The entity, as a message names it.</param>
    /// <param name="info">What it declares.</param>
    /// <exception cref="ArgumentException">It disagrees with what was added before.</exception>
    public void Add(string entity, DynamoDbEntityInfo info)
    {
        if (info.PartitionKeyName != _partitionKey || info.SortKeyName != _sortKey)
        {
            throw new ArgumentException($"{_owner} has the key {Describe(_partitionKey, _sortKey)}, and {entity} has {Describe(info.PartitionKeyName, info.SortKeyName)}, so they cannot share a table.");
        }

        Define(info.PartitionKeyName, info.PartitionKeyType, entity);
        if (info.SortKeyName is not null)
        {
            Define(info.SortKeyName, info.SortKeyType, entity);
        }

        foreach (var index in info.Indexes)
        {
            Define(index.PartitionKeyName, index.PartitionKeyType, entity);
            if (index.SortKeyName is not null)
            {
                Define(index.SortKeyName, index.SortKeyType, entity);
            }

            Declare(index, entity);
        }
    }

    /// <summary>Creates the request. Billing is on demand unless capacity is given, and a global index needs capacity of its own then, which a local index shares with the table.</summary>
    /// <param name="tableName">The table.</param>
    /// <param name="capacity">The capacity of the table and of each global index, if it has provisioned capacity.</param>
    /// <param name="stream">The view of the stream of the table, if it has one.</param>
    /// <returns>The request.</returns>
    public CreateTableRequest ToRequest(string tableName, ProvisionedThroughput? capacity, StreamViewType? stream)
    {
        var globals = new List<GlobalSecondaryIndex>();
        var locals = new List<LocalSecondaryIndex>();
        foreach (var (index, _) in _indexes)
        {
            if (index.IsLocal)
            {
                locals.Add(new LocalSecondaryIndex { IndexName = index.IndexName, KeySchema = KeySchema(index.PartitionKeyName, index.SortKeyName), Projection = new IndexProjection { ProjectionType = ProjectionType.All } });
            }
            else
            {
                globals.Add(new GlobalSecondaryIndex { IndexName = index.IndexName, KeySchema = KeySchema(index.PartitionKeyName, index.SortKeyName), Projection = new IndexProjection { ProjectionType = ProjectionType.All }, ProvisionedThroughput = capacity });
            }
        }

        return new CreateTableRequest
        {
            TableName = tableName,
            AttributeDefinitions = _attributes.ConvertAll(static attribute => new AttributeDefinition(attribute.Name, attribute.Type == ScalarAttributeType.Unknown ? ScalarAttributeType.String : attribute.Type)),
            KeySchema = KeySchema(_partitionKey, _sortKey),
            GlobalSecondaryIndexes = globals,
            LocalSecondaryIndexes = locals,
            BillingMode = capacity is null ? BillingMode.PayPerRequest : BillingMode.Provisioned,
            ProvisionedThroughput = capacity,
            StreamSpecification = stream is { } view ? new StreamSpecification { StreamEnabled = true, StreamViewType = view } : null,
        };
    }

    private static List<KeySchemaElement> KeySchema(string partitionKey, string? sortKey) =>
        sortKey is null ? [new KeySchemaElement(partitionKey, KeyType.Hash)] : [new KeySchemaElement(partitionKey, KeyType.Hash), new KeySchemaElement(sortKey, KeyType.Range)];

    private static string Describe(string partitionKey, string? sortKey) => sortKey is null ? $"({partitionKey})" : $"({partitionKey}, {sortKey})";

    private static string Describe(DynamoDbIndexInfo index) => $"{(index.IsLocal ? "local" : "global")} over {Describe(index.PartitionKeyName, index.SortKeyName)}";

    private void Define(string name, ScalarAttributeType type, string declaredBy)
    {
        var known = _attributes.FindIndex(attribute => attribute.Name == name);
        if (known < 0)
        {
            _attributes.Add(new KeyAttribute(name, type, declaredBy));
        }
        else if (_attributes[known].Type == ScalarAttributeType.Unknown)
        {
            _attributes[known] = new KeyAttribute(name, type, declaredBy);
        }
        else if (type != ScalarAttributeType.Unknown && type != _attributes[known].Type)
        {
            throw new ArgumentException($"{_attributes[known].DeclaredBy} stores the attribute {name} as {_attributes[known].Type}, and {declaredBy} stores it as {type}.");
        }
    }

    private void Declare(DynamoDbIndexInfo index, string declaredBy)
    {
        var known = _indexes.Find(candidate => candidate.Index.IndexName == index.IndexName);
        if (known is null)
        {
            _indexes.Add(new DeclaredIndex(index, declaredBy));
        }
        else if ((known.Index.IsLocal, known.Index.PartitionKeyName, known.Index.SortKeyName) != (index.IsLocal, index.PartitionKeyName, index.SortKeyName))
        {
            throw new ArgumentException($"{known.DeclaredBy} declares the index {index.IndexName} as {Describe(known.Index)}, and {declaredBy} as {Describe(index)}.");
        }
    }

    private readonly record struct KeyAttribute(string Name, ScalarAttributeType Type, string DeclaredBy);

    private sealed record DeclaredIndex(DynamoDbIndexInfo Index, string DeclaredBy);
}
