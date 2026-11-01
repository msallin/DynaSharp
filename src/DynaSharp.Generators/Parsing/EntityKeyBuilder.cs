using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>Builds the keys, indexes and metadata of an entity. Everything is reported, so all problems of a type show at once.</summary>
/// <param name="type">The entity.</param>
/// <param name="typeSpot">Where to report what has no place of its own.</param>
/// <param name="properties">The properties, with attribute names resolved.</param>
/// <param name="claims">The attribute names in use, by owner, seeded with the stored properties.</param>
/// <param name="reserved">The attribute of the discriminator, or null.</param>
/// <param name="enums">Where enums in name formats register their converter.</param>
/// <param name="bag">Where problems go.</param>
internal sealed class EntityKeyBuilder(
    INamedTypeSymbol type,
    Spot typeSpot,
    List<PropertyFacts> properties,
    Dictionary<string, AttributeClaim> claims,
    string? reserved,
    EnumRegistry enums,
    DiagnosticBag bag)
{
    private static readonly KeySlot PartitionSlot = new(WellKnownNames.PartitionKeyAttribute, "PartitionKey", "pk", "Pk", default);

    private static readonly KeySlot SortSlot = new(WellKnownNames.SortKeyAttribute, "SortKey", "sk", "Sk", new KeyRole(IsSortKey: true, IsIndex: false, IsSparse: false));

    private readonly Dictionary<string, PropertyFacts> _stored = properties.Where(static property => property.IsStored).ToDictionary(static property => property.Name);

    private readonly Dictionary<string, TemplateInput> _inputs = properties
        .Where(static property => !property.IsIgnored)
        .ToDictionary(static property => property.Name, property => ToInput(property, type, enums));

    private readonly HashSet<string> _indexNames = [];

    private readonly HashSet<string> _builderNames = [];

    private readonly HashSet<string> _constantNames = [];

    private readonly List<KeyModel> _keys = [];

    public EntityModel? Build(DiscriminatorInfo discriminator)
    {
        var partition = TableKey(PartitionSlot);
        var hasPartition = AttributeReader.Find(type, WellKnownNames.PartitionKeyAttribute) is not null || properties.Exists(static property => property.PartitionKey is not null);
        if (!hasPartition)
        {
            bag.Report(Descriptors.NoPartitionKey, typeSpot, type.Name);
        }

        var sort = TableKey(SortSlot);
        var hasSort = AttributeReader.Find(type, WellKnownNames.SortKeyAttribute) is not null || properties.Exists(static property => property.SortKey is not null);

        var indexes = new List<IndexModel>();
        foreach (var attribute in AttributeReader.FindAll(type, WellKnownNames.GlobalIndexKeyAttribute))
        {
            AddIfBuilt(indexes, GlobalIndex(attribute));
        }

        foreach (var attribute in AttributeReader.FindAll(type, WellKnownNames.LocalIndexKeyAttribute))
        {
            AddIfBuilt(indexes, LocalIndex(attribute, partition, hasSort));
        }

        var version = VersionProperty();
        if (bag.HasErrors)
        {
            return null;
        }

        RestoreKeyOnlyProperties();
        FindFallbackKeys();
        var built = new EquatableArray<IndexModel>([.. indexes.Select(index => WithGates(index, indexes))]);
        return new EntityModel(discriminator.Value, discriminator.AttributeName, partition!, sort, built, version);
    }

    private static IndexModel WithGates(IndexModel index, List<IndexModel> indexes) => index with
    {
        PartitionKey = index.IsLocal ? index.PartitionKey : Gated(index.PartitionKey, index, indexes),
        SortKey = index.SortKey is { } sortKey ? Gated(sortKey, index, indexes) : null,
    };

    private static KeyModel Gated(KeyModel key, IndexModel owner, List<IndexModel> indexes)
    {
        var sharers = indexes.Where(index => index != owner && Holds(index, key.AttributeName)).ToList();
        return key.SharesAttribute || sharers.Count == 0
            ? key
            : key with { Gates = new EquatableArray<EquatableArray<string>>([.. sharers.Prepend(owner).Select(static index => index.Sparse ? index.NullableInputs : default)]) };
    }

    private static bool Holds(IndexModel index, string attributeName) =>
        (!index.IsLocal && index.PartitionKey.AttributeName == attributeName) || index.SortKey?.AttributeName == attributeName;

    private static TemplateInput ToInput(PropertyFacts property, INamedTypeSymbol type, EnumRegistry enums)
    {
        var (inner, isNullable) = Types.Unwrap(property.Type);
        return new TemplateInput(
            property.Name,
            property.ParameterName,
            Names.Of(inner),
            inner.ToDisplayString(),
            KeyInputCatalog.Classify(inner, enums),
            isNullable,
            property.IsComputed,
            Types.Effective(inner) >= Types.Effective(type));
    }

    private static void AddIfBuilt(List<IndexModel> indexes, IndexModel? index)
    {
        if (index is not null)
        {
            indexes.Add(index);
        }
    }

    private static EquatableArray<string> NullableInputs(params KeyTemplate?[] templates)
    {
        var names = templates
            .SelectMany(static template => template?.Segments ?? default)
            .OfType<PlaceholderSegment>()
            .Where(static placeholder => placeholder.InputIsNullable)
            .Select(static placeholder => placeholder.Property)
            .Distinct();
        return new EquatableArray<string>([.. names]);
    }

    private KeyModel? TableKey(KeySlot slot)
    {
        var onType = AttributeReader.Find(type, slot.Attribute);
        var onProperties = properties.Select(property => (Property: property, Attribute: AttributeReader.Find(property.Symbol, slot.Attribute)))
            .Where(static pair => pair.Attribute is not null)
            .ToList();

        // The second declaration is the wrong one, whichever target it is on.
        foreach (var (_, attribute) in onType is null ? onProperties.Skip(1) : onProperties)
        {
            bag.Report(Descriptors.DuplicateKey, Spot.Of(attribute!, typeSpot), slot.Label);
        }

        if (onType is not null)
        {
            return TemplateOnType(onType, slot);
        }

        return onProperties.Count > 0 ? PlainOnProperty(onProperties[0].Property, onProperties[0].Attribute!, slot) : null;
    }

    private KeyModel? TemplateOnType(AttributeData attribute, KeySlot slot)
    {
        if (AttributeReader.Text(attribute, 0) is not { } template)
        {
            bag.Report(Descriptors.MisplacedKey, Spot.Of(attribute, typeSpot));
            return null;
        }

        var name = KeyAttributeName(attribute, WellKnownNames.AttributeNameArgument, slot.DefaultAttribute);
        return TemplateKey(name, slot.BuilderName, template, slot.Role, Spot.Argument(attribute, 0, typeSpot), slot.Label);
    }

    private KeyModel? PlainOnProperty(PropertyFacts property, AttributeData attribute, KeySlot slot)
    {
        var spot = Spot.Of(attribute, typeSpot);
        if (AttributeReader.Text(attribute, 0) is not null)
        {
            bag.Report(Descriptors.MisplacedKey, spot);
            return null;
        }

        if (property.IsIgnored)
        {
            bag.Report(Descriptors.IgnoredPlainKey, spot, property.Name);
            return null;
        }

        if (property.KeyOnly is not null)
        {
            bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "a plain key is stored as itself");
            return null;
        }

        return PropertyKey(property, slot.BuilderName, slot.Role, spot);
    }

    private KeyModel PropertyKey(PropertyFacts property, string builderName, KeyRole role, Spot spot)
    {
        var (inner, isNullable) = Types.Unwrap(property.Type);
        if (property.Converter is null && !ConverterResolver.IsKeyType(inner))
        {
            bag.Report(Descriptors.InvalidKeyType, spot, property.Name, property.Type.ToDisplayString());
        }

        // Keys.Of takes the value, and it is as public as the entity.
        if (Types.Effective(inner) < Types.Effective(type))
        {
            bag.Report(Descriptors.InaccessibleType, spot, property.Type.ToDisplayString(), property.Name);
        }

        if (isNullable && !role.IsSparse)
        {
            bag.Report(Descriptors.NullableInput, spot, property.Name);
        }

        if (role.IsIndex && property.IsComputed)
        {
            bag.Report(Descriptors.ComputedInIndex, spot, property.Name);
        }

        var key = new KeyModel(property.AttributeName, builderName, null, property.Name, SharesAttribute: false, WireType: ConverterResolver.WireTypeOf(property));
        _keys.Add(key);
        return key;
    }

    private KeyModel? TemplateKey(string attributeName, string builderName, string template, KeyRole role, Spot spot, string label)
    {
        var parsed = KeyTemplateParser.Parse(template);
        if (parsed.Error is { } error)
        {
            ReportTemplateError(template, error, spot);
            return null;
        }

        // One bare placeholder over the property's own attribute: nothing extra is written, and an N-typed key works.
        if (parsed.Nodes.Count == 1
            && parsed.Nodes[0] is PlaceholderNode { Format: null } bare
            && _stored.TryGetValue(bare.Property, out var property)
            && property.AttributeName == attributeName)
        {
            return PropertyKey(property, builderName, role, spot);
        }

        var binding = KeyTemplateBinder.Bind(template, parsed.Nodes, _inputs, role);
        foreach (var problem in binding.Problems)
        {
            bag.Report(problem.Descriptor, spot, problem.Arguments);
        }

        var key = Claim(new KeyModel(attributeName, builderName, binding.Template, null, SharesAttribute: false), label, spot);
        _keys.Add(key);
        return key;
    }

    private void ReportTemplateError(string template, TemplateError error, Spot spot)
    {
        if (error.IsLiteral)
        {
            bag.Report(Descriptors.LiteralNotAscii, spot);
        }
        else
        {
            bag.Report(Descriptors.TemplateSyntax, spot, template, error.Position.ToString(CultureInfo.InvariantCulture), error.Message);
        }
    }

    // Two keys may share an attribute when they write the same text, which is what an inverted index does.
    private KeyModel Claim(KeyModel key, string label, Spot spot)
    {
        var text = key.Template!.Text;
        if (!claims.TryGetValue(key.AttributeName, out var owner))
        {
            claims.Add(key.AttributeName, new AttributeClaim(label, text));
            return key;
        }

        if (owner.Template == text)
        {
            return key with { SharesAttribute = true };
        }

        bag.Report(Descriptors.DuplicateAttributeName, spot, key.AttributeName, owner.Label, label);
        return key;
    }

    private string KeyAttributeName(AttributeData attribute, string argument, string defaultName)
    {
        var name = AttributeReader.Named(attribute, argument) ?? defaultName;
        if (AttributeNames.Problem(name, reserved, AttributeNames.MaxKeyLength) is { } problem)
        {
            bag.Report(Descriptors.InvalidAttributeName, Spot.Argument(attribute, argument, typeSpot), name, problem);
        }

        return name;
    }

    private bool IsValidIndexName(string name, AttributeData attribute)
    {
        const int MinLength = 3;
        const int MaxLength = 255;
        var isValid = name.Length is >= MinLength and <= MaxLength
            && name.All(static character => character is '_' or '-' or '.' || (character < 128 && char.IsLetterOrDigit(character)));
        if (!isValid)
        {
            ReportIndex(name, "its name must be 3 to 255 of a-z, A-Z, 0-9, '_', '-' and '.'", attribute);
        }
        else if (!_indexNames.Add(name))
        {
            ReportIndex(name, "the name is used twice", attribute);
            isValid = false;
        }

        return isValid;
    }

    private void ReportIndex(string name, string reason, AttributeData attribute) =>
        bag.Report(Descriptors.InvalidIndex, Spot.Of(attribute, typeSpot), name, reason);

    private bool ClaimBuilder(string builderName, string indexName, AttributeData attribute)
    {
        if (_builderNames.Add(builderName))
        {
            return true;
        }

        ReportIndex(indexName, $"its key builder '{builderName}' collides with another index", attribute);
        return false;
    }

    // Builders end in Pk or Sk, so a global index without a sort key and a local one can pass them and still want one constant.
    private bool ClaimConstant(string indexName, AttributeData attribute)
    {
        var constant = Names.IndexConstant(indexName);
        if (_constantNames.Add(constant))
        {
            return true;
        }

        ReportIndex(indexName, $"its constant '{constant}' in Indexes collides with another index", attribute);
        return false;
    }

    private IndexModel? GlobalIndex(AttributeData attribute)
    {
        var name = AttributeReader.Text(attribute, 0) ?? string.Empty;
        if (!IsValidIndexName(name, attribute))
        {
            return null;
        }

        var builder = Names.Builder(name);
        if (AttributeReader.Named(attribute, "PartitionKey") is not { } partitionTemplate)
        {
            ReportIndex(name, "it has no partition key", attribute);
            return null;
        }

        var sortTemplate = AttributeReader.Named(attribute, "SortKey");
        if (!ClaimBuilder(builder + "Pk", name, attribute) || (sortTemplate is not null && !ClaimBuilder(builder + "Sk", name, attribute)) || !ClaimConstant(name, attribute))
        {
            return null;
        }

        var sparse = AttributeReader.Flag(attribute, "Sparse");
        var partition = TemplateKey(
            KeyAttributeName(attribute, "PartitionKeyAttributeName", Names.IndexAttribute(name, "pk")),
            builder + "Pk",
            partitionTemplate,
            new KeyRole(IsSortKey: false, IsIndex: true, sparse),
            Spot.Argument(attribute, "PartitionKey", typeSpot),
            name + ".PartitionKey");
        var sort = sortTemplate is not null
            ? TemplateKey(
                KeyAttributeName(attribute, "SortKeyAttributeName", Names.IndexAttribute(name, "sk")),
                builder + "Sk",
                sortTemplate,
                new KeyRole(IsSortKey: true, IsIndex: true, sparse),
                Spot.Argument(attribute, "SortKey", typeSpot),
                name + ".SortKey")
            : null;
        return partition is null ? null : Index(name, partition, sort, sparse, isLocal: false, attribute, [partition, sort]);
    }

    private IndexModel? LocalIndex(AttributeData attribute, KeyModel? partition, bool hasSort)
    {
        var name = AttributeReader.Text(attribute, 0) ?? string.Empty;
        if (!IsValidIndexName(name, attribute))
        {
            return null;
        }

        if (!hasSort)
        {
            ReportIndex(name, "a local index needs a table with a sort key", attribute);
            return null;
        }

        var builder = Names.Builder(name);
        if (!ClaimBuilder(builder + "Sk", name, attribute) || !ClaimConstant(name, attribute))
        {
            return null;
        }

        var sparse = AttributeReader.Flag(attribute, "Sparse");
        var sort = TemplateKey(
            KeyAttributeName(attribute, "SortKeyAttributeName", Names.IndexAttribute(name, "sk")),
            builder + "Sk",
            AttributeReader.Text(attribute, 1) ?? string.Empty,
            new KeyRole(IsSortKey: true, IsIndex: true, sparse),
            Spot.Argument(attribute, 1, typeSpot),
            name + ".SortKey");
        return partition is null || sort is null ? null : Index(name, partition, sort, sparse, isLocal: true, attribute, [sort]);
    }

    private IndexModel Index(string name, KeyModel partition, KeyModel? sort, bool sparse, bool isLocal, AttributeData attribute, KeyModel?[] ownKeys)
    {
        var inputs = NullableInputs([.. ownKeys.Select(static key => key?.Template)]);
        var plainInputs = ownKeys
            .Select(static key => key?.PlainProperty)
            .OfType<string>()
            .Where(property => Types.Unwrap(_stored[property].Type).IsNullable);
        var all = new EquatableArray<string>([.. inputs.Concat(plainInputs).Distinct()]);
        if (sparse && all.Count == 0)
        {
            bag.Report(Descriptors.SparseWithoutNullableInput, Spot.Of(attribute, typeSpot), name);
        }

        return new IndexModel(name, partition, sort, sparse, isLocal, all);
    }

    private string? VersionProperty()
    {
        string? version = null;
        foreach (var property in properties.Where(static property => property.Version is not null))
        {
            if (version is null && property.IsStored && !property.IsComputed && property.Converter is null && KeyInputCatalog.IsInteger(property.Type))
            {
                version = property.Name;
                RejectVersionInKey(property);
            }
            else
            {
                bag.Report(Descriptors.InvalidVersion, Spot.Of(property.Version!, property.Spot), property.Name);
            }
        }

        return version;
    }

    // A put writes the next version, so the key would move to another item, or an index would go stale.
    private void RejectVersionInKey(PropertyFacts property)
    {
        var inputs = _keys.SelectMany(static key => (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Select(static placeholder => placeholder.Property));
        if (_keys.Exists(key => key.PlainProperty == property.Name) || inputs.Contains(property.Name))
        {
            bag.Report(Descriptors.VersionInKey, Spot.Of(property.Version!, property.Spot), property.Name);
        }
    }

    private void RestoreKeyOnlyProperties()
    {
        foreach (var property in properties.Where(static property => property.KeyOnly is not null))
        {
            var spot = Spot.Of(property.KeyOnly!, property.Spot);
            if (property.IsComputed)
            {
                bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "it is computed");
                continue;
            }

            if (property.IsIgnored)
            {
                bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "it is also [DynamoDbIgnore]");
                continue;
            }

            var holders = HoldersOf(property);
            if (holders.Count == 0)
            {
                bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "no key template uses it");
                continue;
            }

            var lossless = holders.Find(static holder => holder.Placeholder.Encoding.IsLossless);
            if (lossless.Key is null)
            {
                bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "no key holds it without loss, a timestamp needs the format ticks");
                continue;
            }

            property.RestoredFromKey = lossless.Key.AttributeName;
        }
    }

    // An update stores no input of the table key, so the item it creates has the key and not the property. A nullable input is left out: null is omitted on write, so
    // its attribute missing means null, and only a sparse key can hold one.
    private void FindFallbackKeys()
    {
        foreach (var property in properties.Where(static property => property.IsStored && !property.IsComputed && !Types.Unwrap(property.Type).IsNullable))
        {
            property.FallbackKey = HoldersOf(property).Find(static holder => holder.Placeholder.Encoding.IsLossless).Key?.AttributeName;
        }
    }

    private List<(KeyModel Key, PlaceholderSegment Placeholder)> HoldersOf(PropertyFacts property) =>
    [
        .. _keys
            .SelectMany(static key => (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Select(placeholder => (key, placeholder)))
            .Where(holder => holder.placeholder.Property == property.Name),
    ];
}
