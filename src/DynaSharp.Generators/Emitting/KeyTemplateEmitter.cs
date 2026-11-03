namespace DynaSharp.Generators;

/// <summary>
/// Writes the metadata of an update: every key attribute that is made of properties, once, with a delegate that formats it. A key of the table has none, because an
/// update must not touch it, and neither has a plain key, whose attribute is the property. A plain key of the table is left out, DynamoDB rejects its update by itself and an entity with
/// nothing here costs an update nothing, unless an index key is made of it. A key without inputs cannot go stale, and it is here because an update has to write it: with the other key of its
/// index, or on every update when the other key of the index is like it or a key of the table. A key of the table with an input that an index key repeats or that has an attribute of its own also says how to read that
/// input from the key, which is how an update that was given the key writes the index key without a read, and stores the input when it is asked to.
/// </summary>
internal static class KeyTemplateEmitter
{
    private const string Values = "__values";

    private const string Key = "__key";

    private const string Parts = "__parts";

    private const string Value = "__value";

    public static List<string> Lines(TypeModel type, EntityModel entity)
    {
        var tableKeys = TableKeys(entity);
        var indexKeys = IndexKeys(entity);
        var readers = PlaceholdersOf(indexKeys);
        var stored = StoredInputs(type, tableKeys);
        var lost = LostInputs(tableKeys, readers, stored);
        var lines = new List<string>();
        foreach (var key in tableKeys)
        {
            var parse = Parse(type, key, readers, stored);
            if (Placeholders(key).Any() || parse is not null)
            {
                lines.Add(Line(type, entity, key, isTableKey: true, stored, format: null, parse, Placeholders(key).Any(placeholder => lost.Contains(placeholder.Property)), copyOf: null));
            }
        }

        lines.AddRange(indexKeys.Select(key => Line(type, entity, key, isTableKey: false, stored, Format(key), parse: null, losesValues: false, CopySource(type, key))));
        return lines;
    }

    /// <summary>Finds what a generated key has to give an update, because the text of the key does not.</summary>
    /// <param name="type">The type of the entity.</param>
    /// <param name="entity">The entity.</param>
    /// <returns>The properties an index key repeats or an update can store, or nothing if the text of the key gives each of them back.</returns>
    public static List<string> HeldInputs(TypeModel type, EntityModel entity)
    {
        var tableKeys = TableKeys(entity);
        var readers = PlaceholdersOf(IndexKeys(entity));
        var stored = StoredInputs(type, tableKeys);
        return LostInputs(tableKeys, readers, stored).Count == 0
            ? []
            : [.. tableKeys.SelectMany(Inputs).Where(input => stored.Contains(input) || readers.Exists(reader => reader.Property == input)).Distinct()];
    }

    /// <summary>
    /// Finds the attribute an update can copy into a key of an index, <c>SET key = attribute</c>, because the key is its stored text byte for byte: a template of one bare placeholder over a property with an
    /// attribute of its own, whose converter writes the string the encoding writes. A literal, a second placeholder or a format that pads, cuts or turns a number into text makes another text.
    /// </summary>
    /// <param name="type">The type of the entity.</param>
    /// <param name="key">The key of an index.</param>
    /// <returns>The attribute, or <see langword="null"/> for a key that only its inputs can build.</returns>
    public static string? CopySource(TypeModel type, KeyModel key)
    {
        if (key.Template is not { Segments: { Count: 1 } segments } || segments[0] is not PlaceholderSegment placeholder)
        {
            return null;
        }

        var property = type.Properties.FirstOrDefault(candidate => candidate.Name == placeholder.Property && candidate.Converter is not null);
        return property is not null && placeholder.Encoding.HasTextOf(property.Converter!.WriteConverter) ? property.AttributeName : null;
    }

    private static List<KeyModel> TableKeys(EntityModel entity) => [.. new[] { entity.PartitionKey, entity.SortKey }.OfType<KeyModel>()];

    // A local index has the partition key of the table, and an index key in an attribute of the table or of an index before it is that key.
    private static List<KeyModel> IndexKeys(EntityModel entity)
    {
        var claimed = new HashSet<string>(TableKeys(entity).Select(static key => key.AttributeName));
        return [.. entity.Indexes.SelectMany(static index => new[] { index.IsLocal ? null : index.PartitionKey, index.SortKey }).OfType<KeyModel>().Where(key => claimed.Add(key.AttributeName))];
    }

    private static IEnumerable<PlaceholderSegment> Placeholders(KeyModel key) => (key.Template?.Segments ?? default).OfType<PlaceholderSegment>();

    private static List<PlaceholderSegment> PlaceholdersOf(List<KeyModel> keys) => [.. keys.SelectMany(Placeholders)];

    private static IEnumerable<string> Inputs(KeyModel key) =>
        key.Template is null ? [key.PlainProperty!] : Placeholders(key).Select(static placeholder => placeholder.Property);

    private static string? AttributeOf(TypeModel type, string property) =>
        type.Properties.FirstOrDefault(candidate => candidate.Name == property && candidate.Converter is not null)?.AttributeName;

    // What StoreKeyInputs writes. A plain key is its attribute, and a [KeyOnly] property and a computed one that is not stored have none.
    private static HashSet<string> StoredInputs(TypeModel type, List<KeyModel> tableKeys) =>
        [.. PlaceholdersOf(tableKeys).Select(static placeholder => placeholder.Property).Where(property => AttributeOf(type, property) is { } attribute && !tableKeys.Exists(key => key.AttributeName == attribute))];

    // Parsing a truncated timestamp is no loss to an index key of the same format, which truncates it again.
    private static bool GivesBack(PlaceholderSegment placeholder, List<PlaceholderSegment> readers)
    {
        var repeats = readers.FindAll(reader => reader.Property == placeholder.Property);
        return placeholder.Encoding.IsLossless || (repeats.Count > 0 && repeats.TrueForAll(reader => reader.Encoding == placeholder.Encoding));
    }

    // What an index wants of an input takes a placeholder that gives it back, and the attribute of an input takes one that holds all of it.
    private static HashSet<string> LostInputs(List<KeyModel> tableKeys, List<PlaceholderSegment> readers, HashSet<string> stored)
    {
        var placeholders = PlaceholdersOf(tableKeys);
        var held = placeholders.FindAll(placeholder => readers.Exists(reader => reader.Property == placeholder.Property));
        var lost = new HashSet<string>(held.Select(static placeholder => placeholder.Property).Where(property => !held.Exists(placeholder => placeholder.Property == property && GivesBack(placeholder, readers))));
        lost.UnionWith(stored.Where(property => !placeholders.Exists(placeholder => placeholder.Property == property && placeholder.Encoding.IsLossless)));
        return lost;
    }

    private static string Line(TypeModel type, EntityModel entity, KeyModel key, bool isTableKey, HashSet<string> stored, string? format, string? parse, bool losesValues, string? copyOf)
    {
        var text = key.Template?.Text ?? "{" + key.PlainProperty + "}";
        var inputs = key.Template is null
            ? [Input(type, entity, key.PlainProperty!, null)]
            : Placeholders(key).Select(placeholder => Input(type, entity, placeholder.Property, isTableKey && stored.Contains(placeholder.Property) ? placeholder : null)).ToList();
        var arguments = new List<string>
        {
            CodeText.Literal(key.AttributeName),
            CodeText.Literal(text),
            isTableKey ? "true" : "false",
            inputs.Count > 0 ? $"new[] {{ {string.Join(", ", inputs)} }}" : "global::System.Array.Empty<__Map.DynamoDbKeyInput>()",
        };
        if (format is not null)
        {
            arguments.Add(format);
        }

        var members = new List<string>();
        if (parse is not null)
        {
            members.Add($"Parse = {parse}");
        }

        if (losesValues)
        {
            members.Add("LosesValues = true");
        }

        if (copyOf is not null)
        {
            members.Add($"CopyOf = {CodeText.Literal(copyOf)}");
        }

        var line = $"new __Map.DynamoDbKeyTemplate({string.Join(", ", arguments)})";
        return members.Count == 0 ? line : $"{line} {{ {string.Join(", ", members)} }}";
    }

    // A [KeyOnly] property and a computed one that is not stored have no attribute, so no handle can set them. The value of a stored input goes through the type parameter, which reaches a Write that a converter implements explicitly.
    private static string Input(TypeModel type, EntityModel entity, string property, PlaceholderSegment? stored)
    {
        var arguments = new List<string> { CodeText.Literal(property), CodeText.OptionalLiteral(AttributeOf(type, property)) };
        if (entity.ComputedInputs.FirstOrDefault(input => input.Property == property) is { } computed)
        {
            arguments.Add($"ComputedFromAttributes: new[] {{ {string.Join(", ", computed.DependencyAttributes.Select(CodeText.Literal))} }}");
        }

        if (stored is not null)
        {
            var converter = type.Property(property).Converter!.WriteConverter;
            arguments.Add($"ToAttributeValue: static {Value} => __Ser.ConverterChecks.ToAttributeValue<{stored.ValueType}, {converter}>(({stored.ValueType}){Value})");
            if (!stored.Encoding.IsLossless)
            {
                arguments.Add("IsCutOff: true");
            }
        }

        return $"new __Map.DynamoDbKeyInput({string.Join(", ", arguments)})";
    }

    private static string? Format(KeyModel key)
    {
        if (key.Template is null)
        {
            return null;
        }

        var values = Placeholders(key).Select(static (placeholder, index) => $"({placeholder.ValueType}){Values}[{CodeText.Number(index)}]!");
        return $"static {Values} => Keys.{key.BuilderName}.Format({string.Join(", ", values)})";
    }

    private static string? Parse(TypeModel type, KeyModel key, List<PlaceholderSegment> readers, HashSet<string> stored)
    {
        if (!Inputs(key).Any(input => stored.Contains(input) || readers.Exists(reader => reader.Property == input)))
        {
            return null;
        }

        if (key.Template is null)
        {
            var property = type.Property(key.PlainProperty!);
            return $"static {Key} => new object?[] {{ __Ser.ConverterChecks.ReadValue<{property.ValueType}, {property.Converter!.ReadConverter}>({Key}) }}";
        }

        var values = Placeholders(key).Select(placeholder => GivesBack(placeholder, readers) ? $"{Parts}.{Names.Identifier(placeholder.Property)}" : "null");
        return $"static {Key} => {Key}.Type == global::DynaSharp.AttributeType.String && Keys.{key.BuilderName}.TryParse({Key}.AsString(), out var {Parts}) ? new object?[] {{ {string.Join(", ", values)} }} : null";
    }
}
