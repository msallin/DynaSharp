namespace DynaSharp.Generators;

/// <summary>
/// Writes the metadata of an update: every key attribute that is made of properties, once, with a delegate that formats it. A key of the table has none, because an
/// update must not touch it, and neither has a plain key, whose attribute is the property. A plain key of the table is left out, DynamoDB rejects its update by itself and an entity with
/// nothing here costs an update nothing, unless an index key is made of it. A key without inputs cannot go stale, and it is here because an update has to write it: with the other key of its
/// index, or on every update when the other key of the index is like it or a key of the table. A key of the table with an input that an index key repeats also says how to read that input from the key, which is how
/// an update that was given the key writes the index key without a read.
/// </summary>
internal static class KeyTemplateEmitter
{
    private const string Values = "__values";

    private const string Key = "__key";

    private const string Parts = "__parts";

    public static List<string> Lines(TypeModel type, EntityModel entity)
    {
        var tableKeys = TableKeys(entity);
        var indexKeys = IndexKeys(entity);
        var readers = PlaceholdersOf(indexKeys);
        var lost = LostInputs(tableKeys, readers);
        var lines = new List<string>();
        foreach (var key in tableKeys)
        {
            var parse = Parse(type, key, readers);
            if (Placeholders(key).Any() || parse is not null)
            {
                lines.Add(Line(type, key, isTableKey: true, format: null, parse, Placeholders(key).Any(placeholder => lost.Contains(placeholder.Property))));
            }
        }

        lines.AddRange(indexKeys.Select(key => Line(type, key, isTableKey: false, Format(key), parse: null, losesValues: false)));
        return lines;
    }

    /// <summary>Finds what a generated key has to give an update, because the text of the key does not.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The properties an index key repeats, or nothing if the text of the key gives each of them back.</returns>
    public static List<string> HeldInputs(EntityModel entity)
    {
        var tableKeys = TableKeys(entity);
        var readers = PlaceholdersOf(IndexKeys(entity));
        return LostInputs(tableKeys, readers).Count == 0
            ? []
            : [.. tableKeys.SelectMany(Inputs).Where(input => readers.Exists(reader => reader.Property == input)).Distinct()];
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

    // Parsing a truncated timestamp is no loss to an index key of the same format, which truncates it again.
    private static bool GivesBack(PlaceholderSegment placeholder, List<PlaceholderSegment> readers) =>
        placeholder.Encoding.IsLossless || readers.Where(reader => reader.Property == placeholder.Property).All(reader => reader.Encoding == placeholder.Encoding);

    private static HashSet<string> LostInputs(List<KeyModel> tableKeys, List<PlaceholderSegment> readers)
    {
        var held = PlaceholdersOf(tableKeys).FindAll(placeholder => readers.Exists(reader => reader.Property == placeholder.Property));
        return [.. held.Select(static placeholder => placeholder.Property).Where(property => !held.Exists(placeholder => placeholder.Property == property && GivesBack(placeholder, readers)))];
    }

    private static string Line(TypeModel type, KeyModel key, bool isTableKey, string? format, string? parse, bool losesValues)
    {
        var text = key.Template?.Text ?? "{" + key.PlainProperty + "}";
        var inputs = Inputs(key).Select(property => Input(type, property)).ToList();
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

        var line = $"new __Map.DynamoDbKeyTemplate({string.Join(", ", arguments)})";
        if (parse is null)
        {
            return line;
        }

        return $"{line} {{ Parse = {parse}{(losesValues ? ", LosesValues = true" : string.Empty)} }}";
    }

    // A [KeyOnly] property and a computed one that is not stored have no attribute, so no handle can set them.
    private static string Input(TypeModel type, string property)
    {
        var attribute = type.Properties.FirstOrDefault(candidate => candidate.Name == property && candidate.Converter is not null)?.AttributeName;
        return $"new __Map.DynamoDbKeyInput({CodeText.Literal(property)}, {CodeText.OptionalLiteral(attribute)})";
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

    private static string? Parse(TypeModel type, KeyModel key, List<PlaceholderSegment> readers)
    {
        if (!Inputs(key).Any(input => readers.Exists(reader => reader.Property == input)))
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
