using DynaSharp.Mapping;
using DynaSharp.Operations;
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

    private (string Name, string DeclaredBy)? _timeToLive;

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

    /// <summary>Gets the attribute of time to live that an entity declares, or <see langword="null"/> if none does.</summary>
    public string? TimeToLiveAttributeName => _timeToLive?.Name;

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

            Declare(index, entity, info.Discriminator is null ? null : info.DiscriminatorAttributeName);
        }

        if (info.TimeToLiveAttributeName is { } timeToLive)
        {
            DeclareTimeToLive(timeToLive, entity);
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
        foreach (var declared in _indexes)
        {
            var index = declared.Index;
            var projection = ProjectionOf(declared);
            if (index.IsLocal)
            {
                locals.Add(new LocalSecondaryIndex { IndexName = index.IndexName, KeySchema = KeySchema(index.PartitionKeyName, index.SortKeyName), Projection = projection });
            }
            else
            {
                globals.Add(new GlobalSecondaryIndex { IndexName = index.IndexName, KeySchema = KeySchema(index.PartitionKeyName, index.SortKeyName), Projection = projection, ProvisionedThroughput = capacity });
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

    /// <summary>
    /// Compares a table that exists with the union, as <see cref="CreateTableCommand.IfNotExists"/> says: a key of other names or types throws, and an index that is missing, has other keys or another
    /// projection is drift. Only what both sides
    /// know is compared: a type of <see cref="ScalarAttributeType.Unknown"/> here, or an attribute the description does not define, matches any, and a description without a key schema, which DynamoDB always sends, is not
    /// compared at all. What the table has beyond the union is none of its business.
    /// </summary>
    /// <param name="tableName">The table, as the messages name it.</param>
    /// <param name="found">Its description.</param>
    /// <returns>The drift of the indexes, in the order they were declared.</returns>
    /// <exception cref="InvalidOperationException">The key of the table has other attribute names, a sort key where the union has none or none where it has one, or another type.</exception>
    public List<TableDrift> Check(string tableName, TableDescription found)
    {
        List<TableDrift> drift = [];
        if (found.KeySchema.Count == 0)
        {
            return drift;
        }

        var types = found.AttributeDefinitions;
        var (partitionKey, sortKey) = (KeyOf(found.KeySchema, KeyType.Hash), KeyOf(found.KeySchema, KeyType.Range));
        if (!Matches(_partitionKey, _sortKey, partitionKey, sortKey, types))
        {
            throw new InvalidOperationException(
                $"The table {tableName} exists with the {DescribeKey(partitionKey, TypeIn(types, partitionKey), sortKey, TypeIn(types, sortKey))}, and this command expects the {DescribeKey(_partitionKey, _sortKey)}. "
                + "Every read and write of these entities would fail with a ValidationException, and DynamoDB does not change the key of a table: give them a table of their own, or create a new one and move the items.");
        }

        foreach (var entry in _indexes)
        {
            var index = entry.Index;
            var (isLocal, keySchema, projection) = FindIndex(found, index.IndexName);
            var declared = $"{(index.IsLocal ? "local" : "global")} index {index.IndexName} over the {DescribeKey(index.PartitionKeyName, index.SortKeyName)}";
            if (keySchema is null)
            {
                var missing = index.IsLocal
                    ? $"The table {tableName} lacks the {declared}. A local index exists only if the table was created with it, so queries of it fail until the items move to a new table that has it."
                    : $"The table {tableName} lacks the {declared}. Queries of it fail until UpdateTable adds it.";
                drift.Add(new TableDrift(TableDriftKind.IndexMissing, index.IndexName, missing));
                continue;
            }

            var (indexPartitionKey, indexSortKey) = (KeyOf(keySchema, KeyType.Hash), KeyOf(keySchema, KeyType.Range));
            if (isLocal != index.IsLocal || !Matches(index.PartitionKeyName, index.SortKeyName, indexPartitionKey, indexSortKey, types))
            {
                var has = DescribeKey(indexPartitionKey, TypeIn(types, indexPartitionKey), indexSortKey, TypeIn(types, indexSortKey));
                var differs = $"The table {tableName} has the index {index.IndexName} as a {(isLocal ? "local" : "global")} index over the {has}, and the entities declare a {declared}. Queries of it read other attributes than they expect, or fail.";
                drift.Add(new TableDrift(TableDriftKind.IndexDiffers, index.IndexName, differs));
            }
            else if (projection is { ProjectionType: not ProjectionType.Unknown } && ProjectionDrift(tableName, entry, projection) is { } differs)
            {
                drift.Add(new TableDrift(TableDriftKind.IndexDiffers, index.IndexName, differs));
            }
        }

        return drift;
    }

    private static string? KeyOf(IReadOnlyList<KeySchemaElement> keySchema, KeyType keyType)
    {
        foreach (var element in keySchema)
        {
            if (element.KeyType == keyType)
            {
                return element.AttributeName;
            }
        }

        return null;
    }

    private static ScalarAttributeType TypeIn(IReadOnlyList<AttributeDefinition> definitions, string? name)
    {
        foreach (var definition in definitions)
        {
            if (definition.AttributeName == name)
            {
                return definition.AttributeType;
            }
        }

        return ScalarAttributeType.Unknown;
    }

    private static (bool IsLocal, IReadOnlyList<KeySchemaElement>? KeySchema, IndexProjection? Projection) FindIndex(TableDescription found, string name)
    {
        foreach (var index in found.GlobalSecondaryIndexes)
        {
            if (index.IndexName == name)
            {
                return (false, index.KeySchema, index.Projection);
            }
        }

        foreach (var index in found.LocalSecondaryIndexes)
        {
            if (index.IndexName == name)
            {
                return (true, index.KeySchema, index.Projection);
            }
        }

        return (false, null, null);
    }

    private static bool Matches(ScalarAttributeType expected, ScalarAttributeType found) => expected == ScalarAttributeType.Unknown || found == ScalarAttributeType.Unknown || expected == found;

    private static string DescribeKey(string? partitionKey, ScalarAttributeType partitionKeyType, string? sortKey, ScalarAttributeType sortKeyType) =>
        sortKey is null
            ? $"partition key {partitionKey}{DescribeType(partitionKeyType)} and no sort key"
            : $"partition key {partitionKey}{DescribeType(partitionKeyType)} and sort key {sortKey}{DescribeType(sortKeyType)}";

    private static string DescribeType(ScalarAttributeType type) => type == ScalarAttributeType.Unknown ? string.Empty : $" ({type})";

    private static List<KeySchemaElement> KeySchema(string partitionKey, string? sortKey) =>
        sortKey is null ? [new KeySchemaElement(partitionKey, KeyType.Hash)] : [new KeySchemaElement(partitionKey, KeyType.Hash), new KeySchemaElement(sortKey, KeyType.Range)];

    private static string Describe(string partitionKey, string? sortKey) => sortKey is null ? $"({partitionKey})" : $"({partitionKey}, {sortKey})";

    private static string Describe(DynamoDbIndexInfo index) => $"{(index.IsLocal ? "local" : "global")} over {Describe(index.PartitionKeyName, index.SortKeyName)}";

    private static bool Covers(IndexProjection projection, string name) =>
        projection.ProjectionType == ProjectionType.All || (projection.ProjectionType == ProjectionType.Include && projection.NonKeyAttributes?.Contains(name) == true);

    private static string Describe(IndexProjection projection) =>
        projection.ProjectionType == ProjectionType.Include ? $"INCLUDE ({string.Join(", ", projection.NonKeyAttributes ?? [])})" : WireNames.ProjectionTypes.GetNameIfSet(projection.ProjectionType)!;

    private bool Matches(string partitionKey, string? sortKey, string? foundPartitionKey, string? foundSortKey, IReadOnlyList<AttributeDefinition> types) =>
        partitionKey == foundPartitionKey && sortKey == foundSortKey
        && Matches(TypeOf(partitionKey), TypeIn(types, foundPartitionKey))
        && (sortKey is null || Matches(TypeOf(sortKey), TypeIn(types, foundSortKey)));

    private string DescribeKey(string partitionKey, string? sortKey) => DescribeKey(partitionKey, TypeOf(partitionKey), sortKey, sortKey is null ? ScalarAttributeType.Unknown : TypeOf(sortKey));

    // A projection that lacks the discriminator comes first: a typed read finds nothing at all. Then one that lacks other attributes, which a typed read needs if they are required, then one that copies more
    // than the entities ask for, which costs on every write. The keys are in every projection, so neither side counts them.
    private string? ProjectionDrift(string tableName, DeclaredIndex declared, IndexProjection found)
    {
        var index = declared.Index;
        var expected = ProjectionOf(declared);
        var has = $"The table {tableName} has the index {index.IndexName} with a projection of {Describe(found)}";
        var recreate = index.IsLocal ? "A local index keeps the projection it was created with: only a new table with the index changes it" : "An index keeps the projection it was created with: delete it and create it again";
        foreach (var discriminator in declared.Discriminators)
        {
            if (!IsKey(discriminator, index) && !Covers(found, discriminator))
            {
                return $"{has}, which lacks {discriminator}, the discriminator of the entities. "
                    + $"A typed read of it filters on {discriminator}, so it finds nothing or fails. {recreate} with {discriminator} in the projection, or with ALL.";
            }
        }

        var lacks = Lacking(found, expected, index);
        if (lacks is not null)
        {
            return $"{has}, and the entities declare {Describe(expected)}, so it lacks {lacks}. A typed read of it fails for a required property it lacks. {recreate} with {Describe(expected)}.";
        }

        if (Lacking(expected, found, index) is { } extra)
        {
            return $"{has}, and the entities declare {Describe(expected)}, so every write copies {extra} into it as well, at the cost of write capacity and storage. {recreate} with {Describe(expected)}.";
        }

        return null;
    }

    // The non-key attributes, among those that `wanted` copies, that `found` does not, as a message names them; every attribute when `wanted` is ALL and `found` is not.
    private string? Lacking(IndexProjection found, IndexProjection wanted, DynamoDbIndexInfo index)
    {
        if (found.ProjectionType == ProjectionType.All)
        {
            return null;
        }

        if (wanted.ProjectionType == ProjectionType.All)
        {
            return "the other attributes";
        }

        var missing = new List<string>();
        foreach (var name in wanted.NonKeyAttributes ?? [])
        {
            if (!IsKey(name, index) && !Covers(found, name))
            {
                missing.Add(name);
            }
        }

        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    // What the union asks of an index: every attribute if an entity declares it without a list, else the attributes the lists name and the discriminators, without the keys, which every projection has.
    private IndexProjection ProjectionOf(DeclaredIndex declared)
    {
        if (declared.Include is not { } include)
        {
            return new IndexProjection { ProjectionType = ProjectionType.All };
        }

        var nonKey = new List<string>();
        foreach (var name in include.Concat(declared.Discriminators))
        {
            if (!IsKey(name, declared.Index) && !nonKey.Contains(name))
            {
                nonKey.Add(name);
            }
        }

        return nonKey.Count == 0
            ? new IndexProjection { ProjectionType = ProjectionType.KeysOnly }
            : new IndexProjection { ProjectionType = ProjectionType.Include, NonKeyAttributes = nonKey };
    }

    private bool IsKey(string name, DynamoDbIndexInfo index) => name == _partitionKey || name == _sortKey || name == index.PartitionKeyName || name == index.SortKeyName;

    // Every key attribute of the union is defined, by the constructor or by Add.
    private ScalarAttributeType TypeOf(string name) => _attributes.Find(attribute => attribute.Name == name).Type;

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

    // An entity that declares the index without a list reads all of it, so the index copies everything; lists of several entities add up, since each reads its own attributes.
    private void Declare(DynamoDbIndexInfo index, string declaredBy, string? discriminator)
    {
        var known = _indexes.Find(candidate => candidate.Index.IndexName == index.IndexName);
        if (known is null)
        {
            _indexes.Add(new DeclaredIndex(index, declaredBy, discriminator is null ? [] : [discriminator], index.Include is null ? null : [.. index.Include]));
            return;
        }

        if ((known.Index.IsLocal, known.Index.PartitionKeyName, known.Index.SortKeyName) != (index.IsLocal, index.PartitionKeyName, index.SortKeyName))
        {
            throw new ArgumentException($"{known.DeclaredBy} declares the index {index.IndexName} as {Describe(known.Index)}, and {declaredBy} as {Describe(index)}.");
        }

        if (discriminator is not null && !known.Discriminators.Contains(discriminator))
        {
            known.Discriminators.Add(discriminator);
        }

        if (index.Include is null)
        {
            known.Include = null;
        }
        else
        {
            known.Include?.AddRange(index.Include);
        }
    }

    // DynamoDB keeps one attribute of time to live per table, and turning it on for another one means turning it off first.
    private void DeclareTimeToLive(string name, string declaredBy)
    {
        if (_timeToLive is null)
        {
            _timeToLive = (name, declaredBy);
        }
        else if (_timeToLive.Value.Name != name)
        {
            throw new ArgumentException($"{_timeToLive.Value.DeclaredBy} expires its items by the attribute {_timeToLive.Value.Name}, and {declaredBy} by {name}, and a table has time to live on one attribute.");
        }
    }

    private readonly record struct KeyAttribute(string Name, ScalarAttributeType Type, string DeclaredBy);

    // The discriminator attributes of the entities that declare the index and have a discriminator, which their typed reads of it filter on, and the attributes their lists name, or null when one of them
    // reads all of it.
    private sealed class DeclaredIndex(DynamoDbIndexInfo index, string declaredBy, List<string> discriminators, List<string>? include)
    {
        public DynamoDbIndexInfo Index { get; } = index;

        public string DeclaredBy { get; } = declaredBy;

        public List<string> Discriminators { get; } = discriminators;

        public List<string>? Include { get; set; } = include;
    }
}
