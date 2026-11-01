namespace DynaSharp.Generators;

/// <summary>
/// Writes the metadata of an update: every key attribute that is made of properties, once, with a delegate that formats it. A key of the table has none, because an
/// update must not touch it, and neither has a plain key, whose attribute is the property. A plain key of the table is left out altogether: DynamoDB rejects its update by itself,
/// and an entity with nothing here costs an update nothing. A key without inputs cannot go stale, and it is here because an update has to write it: with the other key of its index, or on every
/// update when all keys of the index are like it.
/// </summary>
internal static class KeyTemplateEmitter
{
    private const string Values = "__values";

    public static List<string> Lines(TypeModel type, EntityModel entity)
    {
        var claimed = new HashSet<string>();
        var lines = new List<string>();
        foreach (var key in new[] { entity.PartitionKey, entity.SortKey }.OfType<KeyModel>())
        {
            claimed.Add(key.AttributeName);
            if (key.Template is { } template && template.Segments.OfType<PlaceholderSegment>().Any())
            {
                lines.Add(Line(type, key, isTableKey: true, format: null));
            }
        }

        foreach (var key in entity.Indexes.SelectMany(IndexKeys).Where(key => claimed.Add(key.AttributeName)))
        {
            lines.Add(Line(type, key, isTableKey: false, Format(key)));
        }

        return lines;
    }

    // A local index has the partition key of the table.
    private static IEnumerable<KeyModel> IndexKeys(IndexModel index) => new[] { index.IsLocal ? null : index.PartitionKey, index.SortKey }.OfType<KeyModel>();

    private static string Line(TypeModel type, KeyModel key, bool isTableKey, string? format)
    {
        var text = key.Template?.Text ?? "{" + key.PlainProperty + "}";
        var inputs = key.Template is { } template
            ? template.Segments.OfType<PlaceholderSegment>().Select(placeholder => Input(type, placeholder.Property))
            : [Input(type, key.PlainProperty!)];
        var arguments = new List<string>
        {
            CodeText.Literal(key.AttributeName),
            CodeText.Literal(text),
            isTableKey ? "true" : "false",
            inputs.Any() ? $"new[] {{ {string.Join(", ", inputs)} }}" : "global::System.Array.Empty<__Map.DynamoDbKeyInput>()",
        };
        if (format is not null)
        {
            arguments.Add(format);
        }

        return $"new __Map.DynamoDbKeyTemplate({string.Join(", ", arguments)})";
    }

    // A [KeyOnly] property and a computed one that is not stored have no attribute, so no handle can set them.
    private static string Input(TypeModel type, string property)
    {
        var attribute = type.Properties.FirstOrDefault(candidate => candidate.Name == property && candidate.Converter is not null)?.AttributeName;
        return $"new __Map.DynamoDbKeyInput({CodeText.Literal(property)}, {CodeText.OptionalLiteral(attribute)})";
    }

    private static string? Format(KeyModel key)
    {
        if (key.Template is not { } template)
        {
            return null;
        }

        var values = template.Segments.OfType<PlaceholderSegment>().Select(static (placeholder, index) => $"({placeholder.ValueType}){Values}[{CodeText.Number(index)}]!");
        return $"static {Values} => Keys.{key.BuilderName}.Format({string.Join(", ", values)})";
    }
}
