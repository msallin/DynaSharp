using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// What a table of an entity owes the entity when it writes: its version and its keys, as the generated metadata says, in the form a command wants it. A table
/// of an entity with neither has none of this, so that a command pays one null check and no byte.
/// </summary>
internal sealed class EntityWrites
{
    private readonly HashSet<string> _keyInputs;

    private readonly HashSet<string> _tableKeyInputs;

    private readonly string _partitionKeyName;

    private readonly string? _sortKeyName;

    private EntityWrites(string entityName, DynamoDbEntityInfo info)
    {
        _partitionKeyName = info.PartitionKeyName;
        _sortKeyName = info.SortKeyName;
        EntityName = entityName;
        VersionAttributeName = info.VersionAttributeName;
        Templates = [.. info.KeyTemplates];
        Indexes = [.. info.Indexes];
        LiteralKeys = FindLiteralKeys();
        _keyInputs = [.. Templates.SelectMany(static template => template.Inputs).Select(static input => input.AttributeName).OfType<string>()];
        _tableKeyInputs = [.. Templates.Where(static template => template.IsTableKey).SelectMany(static template => template.Inputs).Select(static input => input.PropertyName)];
        HoldsKey = Templates.Any(static template => template.LosesValues);
    }

    /// <summary>Gets the name of the entity, for messages.</summary>
    public string EntityName { get; }

    /// <summary>Gets the attribute of the version, or <see langword="null"/> if the entity has none.</summary>
    public string? VersionAttributeName { get; }

    /// <summary>Gets the keys that are made of properties, and the keys of literals of an index, which have no properties and follow the key beside them.</summary>
    public DynamoDbKeyTemplate[] Templates { get; }

    /// <summary>
    /// Gets the literals of the indexes whose keys are literals or keys of the table. No property of such an index can bring an item in, so that every update owes them, see
    /// <see cref="WriteRules"/>. Empty for an entity without such an index, as most are.
    /// </summary>
    public LiteralKey[] LiteralKeys { get; }

    /// <summary>Gets the indexes of the entity.</summary>
    public DynamoDbIndexInfo[] Indexes { get; }

    /// <summary>Gets a value indicating whether an index wants more of an input of the table key than the text of the key gives back, so an update has to hold on to a generated key.</summary>
    public bool HoldsKey { get; }

    /// <summary>Reads the metadata of an entity.</summary>
    /// <param name="entityName">The name of the entity.</param>
    /// <param name="info">The metadata.</param>
    /// <returns>What a table has to do, or <see langword="null"/> if there is nothing.</returns>
    public static EntityWrites? Of(string entityName, DynamoDbEntityInfo info) =>
        info.VersionAttributeName is null && info.KeyTemplates.Count == 0 ? null : new EntityWrites(entityName, info);

    /// <summary>Finds out whether a path is the attribute of a property that feeds a key. A nested path never is.</summary>
    /// <param name="path">The path of a handle.</param>
    /// <returns><see langword="true"/> for a key input.</returns>
    public bool IsKeyInput(string path) => _keyInputs.Contains(path);

    /// <summary>Finds out whether a property feeds a key of the table, whose value an update knows from the key it was given and cannot change.</summary>
    /// <param name="input">The property.</param>
    /// <returns><see langword="true"/> for an input of the table key.</returns>
    public bool IsTableKeyInput(DynamoDbKeyInput input) => _tableKeyInputs.Contains(input.PropertyName);

    /// <summary>Finds out whether an attribute is a key of the table, which every item has and no update can write.</summary>
    /// <param name="attributeName">The attribute.</param>
    /// <returns><see langword="true"/> for a key of the table.</returns>
    public bool IsTableKey(string? attributeName) => attributeName == _partitionKeyName || attributeName == _sortKeyName;

    private static void AddIfLiteral(List<LiteralKey> keys, Dictionary<string, string> literals, string? attributeName)
    {
        if (attributeName is not null && literals.TryGetValue(attributeName, out var value))
        {
            keys.Add(new LiteralKey(attributeName, value));
        }
    }

    private LiteralKey[] FindLiteralKeys()
    {
        var literals = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var template in Templates)
        {
            if (template is { Inputs.Count: 0, Format: { } format })
            {
                literals[template.AttributeName] = format([]);
            }
        }

        var keys = new List<LiteralKey>();
        foreach (var index in Indexes)
        {
            // A key of the table is on every item, so it leaves the question to the keys beside it.
            if ((IsTableKey(index.PartitionKeyName) || literals.ContainsKey(index.PartitionKeyName))
                && (index.SortKeyName is null || IsTableKey(index.SortKeyName) || literals.ContainsKey(index.SortKeyName)))
            {
                AddIfLiteral(keys, literals, index.PartitionKeyName);
                AddIfLiteral(keys, literals, index.SortKeyName);
            }
        }

        return [.. keys];
    }

    /// <summary>A key attribute of an index and the one value it ever has.</summary>
    /// <param name="AttributeName">The attribute.</param>
    /// <param name="Value">The value.</param>
    public readonly record struct LiteralKey(string AttributeName, string Value);
}
