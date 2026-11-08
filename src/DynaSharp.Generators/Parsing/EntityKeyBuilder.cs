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

    private readonly List<KeyModel> _sparseKeys = [];

    // The index each key of an index belongs to, for the code fix of DYNA030.
    private readonly Dictionary<KeyModel, string> _indexOfKey = [];

    private readonly List<(KeyTemplate Template, Spot Spot, IReadOnlyCollection<string> Fallbacks)> _templates = [];

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
        ReportTruncatedEnd(hasSort ? sort : partition, hasSort ? SortSlot : PartitionSlot);

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
        var timeToLive = TimeToLiveAttribute();
        var computedInputs = ComputedInputs(version);
        KeepKeysFromOmitting();
        KeepKeysFromMissing();
        if (bag.HasErrors)
        {
            return null;
        }

        ReportEmptyDefaults();
        ReportRawBesideStrict();
        RestoreKeyOnlyProperties();
        ReportRawWithoutKeyOnly(partition, sort);
        FindFallbackKeys();
        var built = new EquatableArray<IndexModel>([.. indexes.Select(index => WithGates(index, indexes))]);
        return new EntityModel(discriminator.Value, discriminator.AttributeName, partition!, sort, built, version, computedInputs, timeToLive);
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

    private static string? TimeToLiveProblem(PropertyFacts property, PropertyFacts? found)
    {
        if (found is not null)
        {
            return $"'{found.Name}' is the time to live of the type already, and a table has time to live on one attribute";
        }

        if (!property.IsStored)
        {
            return "it is not stored, so no item holds an expiry";
        }

        return ConverterResolver.StoresEpochSeconds(property)
            ? null
            : "DynamoDB deletes by a number of epoch seconds and ignores an item that holds anything else. On a DateTimeOffset add [DynamoDbConverter(typeof(EpochSecondsConverter))]; an integer holds the seconds as it is";
    }

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
            property.OmitsEmptyByItsOwnAttribute,
            property.IsComputed,
            property.ComputedFrom is not null,
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
            .Where(static placeholder => placeholder.InputCanBeAbsent)
            .Select(static placeholder => placeholder.Property)
            .Distinct();
        return new EquatableArray<string>([.. names]);
    }

    // What ends the primary key decides whether two items are one. An index is not unique, and a partition key beside a sort key is not all of the key. A format of a minute or coarser says one item per
    // period, which is the design of a counter or a summary; s, ms and us stand for a moment, and there the cut is what loses an item.
    private void ReportTruncatedEnd(KeyModel? lastKey, KeySlot slot)
    {
        var last = (lastKey?.Template?.Segments ?? default).OfType<PlaceholderSegment>().LastOrDefault();
        if (last?.Encoding is TimestampEncoding { IsLossless: false, IsBucket: false })
        {
            bag.Report(
                Descriptors.TruncatedTimestampEndsTableKey,
                Spot.Argument(AttributeReader.Find(type, slot.Attribute)!, 0, typeSpot),
                KeyProblem.Rewrite(last.Property, [(last.Property, null)]),
                [last.Property]);
        }
    }

    private void ReportEmptyDefaults()
    {
        var inputs = _keys.SelectMany(static key => (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Select(static placeholder => placeholder.Property)).ToList();
        inputs.AddRange(_keys.Select(static key => key.PlainProperty).OfType<string>());
        foreach (var property in properties.Where(property => inputs.Contains(property.Name) && !property.OmitsEmpty && EmptyDefault.IsForgettable(property, type)))
        {
            bag.Report(Descriptors.EmptyDefaultOfKey, property.Spot, property.Name);
        }
    }

    // A format belongs to its template, so no template can see that another one refuses what it holds, and the write that passes one key throws at the next. Without a delimiter there is
    // nothing to refuse: such a template takes every value that raw takes.
    private void ReportRawBesideStrict()
    {
        var placeholders = _templates
            .SelectMany(static entry => entry.Template.Segments.OfType<PlaceholderSegment>().Select(placeholder => (entry.Template, entry.Spot, entry.Fallbacks, Placeholder: placeholder)))
            .ToList();
        foreach (var strict in placeholders.Where(static entry =>
            entry.Template.Delimiters.Length > 0 && entry.Placeholder.Encoding is StringEncoding { IsRaw: false } && !entry.Fallbacks.Contains(entry.Placeholder.Property)))
        {
            var raw = placeholders.Find(entry => entry.Placeholder.Encoding is StringEncoding { IsRaw: true } && entry.Placeholder.Property == strict.Placeholder.Property);
            if (raw.Template is not null)
            {
                bag.Report(Descriptors.RawBesideStrict, strict.Spot, strict.Placeholder.Property, raw.Template.Text, strict.Template.Text, strict.Template.Delimiters, string.Empty);
            }
        }
    }

    // A raw input is mostly an id that holds the delimiter, and only the key needs it. A Put stores every input it has an attribute for, and an Update writes none unless it asks, so
    // without [KeyOnly] some items hold the attribute and others do not. An adopted table whose items hold it is the case for keeping it, which is why this is information only.
    private void ReportRawWithoutKeyOnly(KeyModel? partition, KeyModel? sort)
    {
        var reported = new HashSet<string>();
        foreach (var key in new[] { partition, sort })
        {
            foreach (var placeholder in (key?.Template?.Segments ?? default).OfType<PlaceholderSegment>())
            {
                if (placeholder.Encoding is StringEncoding { IsRaw: true }
                    && _stored.TryGetValue(placeholder.Property, out var property)
                    && !property.IsComputed
                    && reported.Add(property.Name))
                {
                    bag.Report(Descriptors.RawWithoutKeyOnly, property.Spot, property.Name, key!.Template!.Text);
                }
            }
        }
    }

    private KeyModel? TableKey(KeySlot slot)
    {
        var onType = AttributeReader.Find(type, slot.Attribute);
        var onProperties = properties.Select(property => (Property: property, Attribute: AttributeReader.FindOnProperty(property.Symbol, slot.Attribute)))
            .Where(static pair => pair.Attribute is not null)
            .ToList();

        // The second declaration is the wrong one, whichever target it is on.
        foreach (var (_, attribute) in onType is null ? onProperties.Skip(1) : onProperties)
        {
            bag.Report(Descriptors.DuplicateKey, Spot.Of(attribute!, typeSpot), DocumentationCommentId.CreateDeclarationId(type), [slot.Label]);
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
        if (property.Converter is null && property.DefaultConverter is null && !ConverterResolver.IsKeyType(inner))
        {
            bag.Report(Descriptors.InvalidKeyType, spot, property.Name, property.Type.ToDisplayString());
        }

        // Keys.Of takes the value, and it is as public as the entity.
        if (Types.Effective(inner) < Types.Effective(type))
        {
            bag.Report(Descriptors.InaccessibleType, spot, Types.AccessibilityFix(Types.Effective(type), property.Name), [property.Type.ToDisplayString(), property.Name]);
        }

        if (isNullable && !role.IsSparse)
        {
            bag.Report(Descriptors.NullableInput, spot, property.Name, [property.Name]);
        }

        if (role.IsIndex && property is { IsComputed: true, ComputedFrom: null })
        {
            bag.Report(Descriptors.ComputedInIndex, spot, ComputedSources(property.Name), [property.Name]);
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
            var fix = FixOf(problem);
            if (problem.KeepsModel)
            {
                bag.ReportKeepingTheModel(problem.Descriptor, spot, fix, problem.Arguments);
            }
            else
            {
                bag.Report(problem.Descriptor, spot, fix, problem.Arguments);
            }
        }

        var key = Claim(new KeyModel(attributeName, builderName, binding.Template, null, SharesAttribute: false), label, spot);
        _keys.Add(key);
        _templates.Add((binding.Template, spot, binding.Fallbacks));
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
        // The diagnostic of an index states the whole rule, whichever part the name breaks.
        var isValid = ResourceNames.Problem(name, "index") is null;
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
            .Where(property => Types.Unwrap(_stored[property].Type).IsNullable || _stored[property].OmitsEmptyByItsOwnAttribute);
        var all = new EquatableArray<string>([.. inputs.Concat(plainInputs).Distinct()]);
        if (sparse && all.Count == 0)
        {
            bag.Report(Descriptors.SparseWithoutNullableInput, Spot.Of(attribute, typeSpot), name);
        }

        if (sparse)
        {
            _sparseKeys.AddRange(ownKeys.OfType<KeyModel>());
        }

        foreach (var key in ownKeys.OfType<KeyModel>())
        {
            _indexOfKey[key] = name;
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

    // DynamoDB deletes by a number of epoch seconds and ignores an item that holds anything else, which is what a DateTimeOffset without EpochSecondsConverter stores, so that is the mistake to catch
    // here. A table has one attribute of time to live, so a second property is one too many, whatever it stores.
    private string? TimeToLiveAttribute()
    {
        PropertyFacts? found = null;
        foreach (var property in properties.Where(static property => property.TimeToLive is not null))
        {
            var problem = TimeToLiveProblem(property, found);
            if (problem is null)
            {
                found = property;
            }
            else
            {
                bag.Report(Descriptors.InvalidTimeToLive, Spot.Of(property.TimeToLive!, property.Spot), property.Name, problem);
            }
        }

        return found?.AttributeName;
    }

    // After the keys and the version, because a list is valid only for a property some key uses, and every update moves the version without a handle.
    private EquatableArray<ComputedInput> ComputedInputs(string? version)
    {
        var inputs = new List<ComputedInput>();
        foreach (var property in properties.Where(static property => property.ComputedFrom is not null))
        {
            var names = AttributeReader.Texts(property.ComputedFrom!).ToList();
            if (ComputedFromProblem(property, names, version) is { } problem)
            {
                bag.Report(Descriptors.InvalidComputedFrom, Spot.Of(property.ComputedFrom!, property.Spot), property.Name, problem);
            }
            else
            {
                var attributes = names.Select(name => _stored[name].AttributeName);
                inputs.Add(new ComputedInput(property.Name, new EquatableArray<string>([.. names]), new EquatableArray<string>([.. attributes])));
            }
        }

        return new EquatableArray<ComputedInput>([.. inputs]);
    }

    // What the code fix of a problem of a template needs, where the binder cannot know it. An input of a public builder is as public as the entity.
    private string? FixOf(KeyProblem problem)
    {
        if (problem.Descriptor == Descriptors.ComputedInIndex)
        {
            return ComputedSources(problem.Arguments[0]);
        }

        return problem.Descriptor == Descriptors.InaccessibleType ? Types.AccessibilityFix(Types.Effective(type), problem.Arguments[1]) : problem.Fix;
    }

    // The fix of DYNA114: the computed property, then each property [ComputedFrom] can name, which the code fix narrows to what the getter reads.
    private string ComputedSources(string property)
    {
        var nameable = properties.Where(static candidate => candidate is { IsStored: true, IsComputed: false, Version: null }).Select(static candidate => candidate.Name);
        return string.Join(DiagnosticInfo.FixSeparator.ToString(), nameable.Prepend(property));
    }

    private string? ComputedFromProblem(PropertyFacts property, List<string> names, string? version)
    {
        if (!property.IsComputed)
        {
            return "it is not computed";
        }

        if (names.Count == 0)
        {
            return "it names no property";
        }

        foreach (var name in names)
        {
            var dependency = properties.Find(candidate => candidate.Name == name);
            if (dependency is { IsComputed: true })
            {
                return $"'{name}' is computed too";
            }

            if (dependency is not { IsStored: true })
            {
                return $"'{name}' is not a stored property of the type";
            }

            if (name == version)
            {
                return $"'{name}' is the version, which an update moves without a handle";
            }
        }

        if (names.Distinct().Count() != names.Count)
        {
            return $"'{names.GroupBy(static name => name).First(static group => group.Count() > 1).Key}' is named twice";
        }

        return FeedsKey(property) ? null : "no key template uses the property";
    }

    // A put writes the next version, so the key would move to another item, or an index would go stale.
    private void RejectVersionInKey(PropertyFacts property)
    {
        if (FeedsKey(property))
        {
            bag.Report(Descriptors.VersionInKey, Spot.Of(property.Version!, property.Spot), property.Name);
        }
    }

    // A key must not be empty, and one that leaves out an input is no key, so on a type the attribute passes over a property that feeds one, and on the property it is a mistake. A converter
    // decides by value, at run time, where nothing can report it, so its answer goes the way of the attribute on a type. The exception is a string with an attribute of its own that feeds
    // only keys of sparse indexes: an empty value is left out as null is, and so is the index, which the gates of the write test the same way.
    private void KeepKeysFromOmitting()
    {
        foreach (var property in properties.Where(FeedsKey))
        {
            property.Resolved = property.Resolved?.AlwaysWritten();
            if (property.OmitsEmptyByItsOwnAttribute && FeedsOnlySparseKeys(property))
            {
                continue;
            }

            var sparseFix = property.OmitsEmptyByItsOwnAttribute ? SparseFix(property) : null;
            property.OmitsEmpty = false;
            if (property.OmitEmpty is { } attribute && AttributeReader.IsEnabled(attribute) && property.CanBeEmpty)
            {
                bag.Report(
                    Descriptors.InvalidOmitEmpty,
                    Spot.Of(attribute, property.Spot),
                    sparseFix,
                    [property.Name, "it feeds a key, and only a stored string that feeds nothing but keys of sparse indexes may leave an empty value out"]);
            }
        }
    }

    private bool FeedsOnlySparseKeys(PropertyFacts property) => KeysFedBy(property).All(_sparseKeys.Contains);

    // What the code fix of DYNA030 needs where keys of indexes alone are why: the id of the type and the names of those indexes that are not sparse. Null where a key of the table is fed too.
    private string? SparseFix(PropertyFacts property)
    {
        var fed = KeysFedBy(property).ToList();
        return fed.All(_indexOfKey.ContainsKey)
            ? string.Join(" ", fed.Where(key => !_sparseKeys.Contains(key)).Select(key => _indexOfKey[key]).Distinct().Prepend(DocumentationCommentId.CreateDeclarationId(type)))
            : null;
    }

    private IEnumerable<KeyModel> KeysFedBy(PropertyFacts property) =>
        _keys.Where(key => key.PlainProperty == property.Name || (key.Template?.Segments ?? default).OfType<PlaceholderSegment>().Any(placeholder => placeholder.Property == property.Name));

    // A property that is a key has only the converter to go by. A read that took it for missing would give an entity with another key, which the next Put writes somewhere else.
    private void KeepKeysFromMissing()
    {
        foreach (var property in properties.Where(IsPlainKey))
        {
            property.Resolved = property.Resolved?.AlwaysRead();
        }
    }

    private bool IsPlainKey(PropertyFacts property) => _keys.Exists(key => key.PlainProperty == property.Name);

    private bool FeedsKey(PropertyFacts property) => IsPlainKey(property) || HoldersOf(property).Count > 0;

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
                bag.Report(Descriptors.InvalidKeyOnly, spot, property.Name, "no key holds it without loss, a timestamp needs the format ticks, which a placeholder without a format has");
                continue;
            }

            property.RestoredFromKey = lossless.Key.AttributeName;
        }
    }

    // An update stores no input of the table key unless it asks, so the item it creates has the key and not the property. A nullable input is left out: null is omitted on write, so
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
