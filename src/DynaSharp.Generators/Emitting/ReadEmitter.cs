using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Writes <c>ReadAttributes</c> and its <c>__Fields</c>. Names are matched as UTF-8, by length first, so a known name costs no string and an
/// unknown one is skipped without parsing its value. Candidates of one length are ordered ordinally, which keeps the output deterministic. A failure
/// inside a skipped value names no attribute: naming it would cost a string for every unknown attribute, and the last known name would be a lie.
/// A property that a key holds without loss falls back on that key when its attribute is missing, which is what an item created by an update looks like. JSON has no order of
/// attributes and the reader goes one way, so a read keeps a copy of the reader, a struct, and walks the item a second time only for such an item: one that has its attributes
/// pays for the copy and for nothing else.
/// </summary>
internal static class ReadEmitter
{
    private const string Indent = "                            ";

    public static void Emit(StringBuilder body, TypeModel type)
    {
        var read = type.Properties.Where(static property => !property.IsWriteOnly).ToList();
        var hasFields = read.Count > 0 || type.Constructor.NeedsFieldsConstructor;
        body.Line();
        body.Line($"    static {type.FullName} __Map.IDynamoDbDocument<{type.FullName}>.ReadAttributes(ref __Ser.DynamoDbReader reader)");
        body.Line("    {");
        if (hasFields)
        {
            body.Line("        __Fields fields = default;");
        }

        var fallbacks = read.Where(static property => property.FallbackKey is not null).GroupBy(static property => property.FallbackKey!).ToList();
        if (fallbacks.Count > 0)
        {
            body.Line("        var __item = reader;");
        }

        body.Line("        string? attribute = null;");
        body.Line("        try");
        body.Line("        {");
        body.Line("            while (reader.TryReadAttributeName())");
        body.Line("            {");
        EmitDispatch(body, Candidates(type, read));
        body.Line("                attribute = null;");
        body.Line("                reader.SkipAttributeValue();");
        body.Line("            }");
        body.Line("        }");
        body.Line("        catch (global::System.Exception exception) when (global::DynaSharp.DynamoDbMappingException.IsConversionFailure(exception))");
        body.Line("        {");
        body.Line($"            throw new global::DynaSharp.DynamoDbMappingException({CodeText.Literal(type.Name)}, attribute, exception);");
        body.Line("        }");
        body.Line();
        for (var group = 0; group < fallbacks.Count; group++)
        {
            EmitFallback(body, type, fallbacks[group], CodeText.Number(group));
        }

        foreach (var property in read.Where(static property => property.Missing == MissingBehavior.Throw))
        {
            var attribute = CodeText.Literal(property.RestoredFromKey ?? property.AttributeName);
            body.Line($"        if (!fields.{property.Name}__set) {{ throw global::DynaSharp.DynamoDbMappingException.Missing({CodeText.Literal(type.Name)}, {attribute}); }}");
        }

        body.Line($"        return {ConstructorEmitter.Creation(type)};");
        body.Line("    }");

        if (hasFields)
        {
            EmitFields(body, read);
        }
    }

    private static List<ReadCandidate> Candidates(TypeModel type, List<PropertyModel> read)
    {
        var candidates = read
            .Where(static property => property.RestoredFromKey is null)
            .Select(static property => new ReadCandidate(property.AttributeName, [
                $"fields.{property.Identifier} = reader.ReadAttribute<{property.ValueType}, {property.Converter!.ReadConverter}>();",
                $"fields.{property.Name}__set = true;"]))
            .ToList();

        var restored = read.Where(static property => property.RestoredFromKey is not null).GroupBy(static property => property.RestoredFromKey!).ToList();
        for (var group = 0; group < restored.Count; group++)
        {
            candidates.Add(RestoreFromKey(type, restored[group].Key, restored[group], "__parts" + CodeText.Number(group)));
        }

        if (type.Entity is { Discriminator: { } discriminator } entity)
        {
            var attribute = CodeText.Literal(entity.DiscriminatorAttribute);
            var check = $"__Ser.ConverterChecks.ExpectDiscriminator(ref reader, {CodeText.Utf8(discriminator)}, {CodeText.Literal(type.Name)}, {attribute});";
            candidates.Add(new ReadCandidate(entity.DiscriminatorAttribute, [check]));
        }

        return candidates;
    }

    // Keys.{Builder}.TryParse is what KeyBuilderEmitter emits: it reports text the template cannot have produced, and the parts are a struct of the placeholders.
    private static ReadCandidate RestoreFromKey(TypeModel type, string keyAttribute, IEnumerable<PropertyModel> properties, string parts)
    {
        var key = type.Entity!.Keys().First(candidate => candidate.AttributeName == keyAttribute);
        var attribute = CodeText.Literal(keyAttribute);
        var lines = new List<string>
        {
            "__Ser.ConverterChecks.Expect(reader.ReadStartAttribute(), global::DynaSharp.AttributeType.String);",
            $"if (!Keys.{key.BuilderName}.TryParse(reader.GetStringUtf8(), out var {parts})) {{ throw new global::DynaSharp.DynamoDbMappingException({CodeText.Literal(type.Name)}, {attribute}); }}",
        };
        foreach (var property in properties)
        {
            lines.Add($"fields.{property.Identifier} = {parts}.{property.Identifier};");
            lines.Add($"fields.{property.Name}__set = true;");
        }

        lines.Add("reader.ReadEndAttribute();");
        return new ReadCandidate(keyAttribute, lines);
    }

    // A key that is absent, of another type or of another shape restores nothing, so what an item lacks fails or defaults here as it does without a key.
    private static void EmitFallback(StringBuilder body, TypeModel type, IGrouping<string, PropertyModel> properties, string group)
    {
        var key = type.Entity!.Keys().First(candidate => candidate.AttributeName == properties.Key);
        body.Line($"        if (({string.Join(" || ", properties.Select(static property => $"!fields.{property.Name}__set"))})");
        body.Line($"            && __Ser.ConverterChecks.TryFindString(__item, {CodeText.Utf8(properties.Key)}, out var __text{group})");
        body.Line($"            && Keys.{key.BuilderName}.TryParse(__text{group}, out var __restored{group}))");
        body.Line("        {");
        foreach (var property in properties)
        {
            body.Line($"            if (!fields.{property.Name}__set) {{ fields.{property.Identifier} = __restored{group}.{property.Identifier}; fields.{property.Name}__set = true; }}");
        }

        body.Line("        }");
        body.Line();
    }

    private static void EmitDispatch(StringBuilder body, List<ReadCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        body.Line("                var name = reader.GetAttributeNameUtf8();");
        body.Line("                switch (name.Length)");
        body.Line("                {");
        foreach (var length in candidates.OrderBy(static candidate => candidate.Attribute, StringComparer.Ordinal).GroupBy(static candidate => candidate.Utf8Length).OrderBy(static group => group.Key))
        {
            body.Line($"                    case {CodeText.Number(length.Key)}:");
            foreach (var candidate in length)
            {
                EmitCandidate(body, candidate);
            }

            body.Line("                        break;");
        }

        body.Line("                }");
        body.Line();
    }

    private static void EmitCandidate(StringBuilder body, ReadCandidate candidate)
    {
        body.Line($"                        if (global::System.MemoryExtensions.SequenceEqual(name, {CodeText.Utf8(candidate.Attribute)}))");
        body.Line("                        {");
        body.Line($"{Indent}attribute = {CodeText.Literal(candidate.Attribute)};");
        foreach (var line in candidate.Lines)
        {
            body.Line(Indent + line);
        }

        body.Line($"{Indent}continue;");
        body.Line("                        }");
        body.Line();
    }

    private static void EmitFields(StringBuilder body, List<PropertyModel> read)
    {
        body.Line();
        body.Line("    private struct __Fields");
        body.Line("    {");
        foreach (var property in read)
        {
            body.Line($"        public {property.ValueType} {property.Identifier}; public bool {property.Name}__set;");
        }

        body.Line("    }");
    }

    /// <summary>What a read does for one attribute name.</summary>
    /// <param name="Attribute">The attribute.</param>
    /// <param name="Lines">The statements that read its value.</param>
    private sealed record ReadCandidate(string Attribute, IReadOnlyList<string> Lines)
    {
        public int Utf8Length { get; } = Encoding.UTF8.GetByteCount(Attribute);
    }
}
