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
    public static void Emit(IndentedCode code, TypeModel type)
    {
        var read = type.Properties.Where(static property => !property.IsWriteOnly).ToList();
        var hasFields = read.Count > 0 || type.Constructor.NeedsFieldsConstructor;
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"static {type.FullName} __Map.IDynamoDbDocument<{type.FullName}>.ReadAttributes(ref __Ser.DynamoDbReader reader)");
        code.Open();
        if (hasFields)
        {
            code.Line("__Fields fields = default;");
        }

        var fallbacks = read.Where(static property => property.FallbackKey is not null).GroupBy(static property => property.FallbackKey!).ToList();
        if (fallbacks.Count > 0)
        {
            code.Line("var __item = reader;");
        }

        // A key of another type does not fit either, and an item has no order of attributes: what the discriminator says comes first, so a key that does not fit fails after the walk.
        var restoresFromKey = read.Exists(static property => property.RestoredFromKey is not null);
        if (restoresFromKey)
        {
            code.Line("string? __misfit = null;");
        }

        code.Line("string? attribute = null;");
        code.Line("try");
        code.Open();
        code.Line("while (reader.TryReadAttributeName())");
        code.Open();
        EmitDispatch(code, Candidates(type, read));
        code.Line("attribute = null;");
        code.Line("reader.SkipAttributeValue();");
        code.Close();
        code.Close();
        code.Line("catch (global::System.Exception exception) when (global::DynaSharp.DynamoDbMappingException.IsConversionFailure(exception))");
        code.Open();
        code.Line($"throw new global::DynaSharp.DynamoDbMappingException({CodeText.Literal(type.Name)}, attribute, exception);");
        code.Close();
        code.Blank();
        if (restoresFromKey)
        {
            code.Line($"if (__misfit is not null) {{ throw global::DynaSharp.DynamoDbMappingException.KeyDoesNotFit({CodeText.Literal(type.Name)}, __misfit); }}");
            code.Blank();
        }

        for (var group = 0; group < fallbacks.Count; group++)
        {
            EmitFallback(code, type, fallbacks[group], CodeText.Number(group));
        }

        foreach (var property in read.Where(static property => property.Missing == MissingBehavior.Throw))
        {
            var attribute = CodeText.Literal(property.RestoredFromKey ?? property.AttributeName);

            // The attribute is what an item created by an update never has, so what is wrong with such an item is the key.
            var key = property.FallbackKey is { } fallback ? $", {CodeText.Literal(fallback)}" : string.Empty;
            code.Line($"if (!fields.{property.SetFlag}) {{ throw global::DynaSharp.DynamoDbMappingException.Missing({CodeText.Literal(type.Name)}, {attribute}{key}); }}");
        }

        code.Line($"return {ConstructorEmitter.Creation(type)};");
        code.Close();

        if (hasFields)
        {
            EmitFields(code, read);
        }
    }

    private static List<ReadCandidate> Candidates(TypeModel type, List<PropertyModel> read)
    {
        var candidates = read
            .Where(static property => property.RestoredFromKey is null)
            .Select(static property => new ReadCandidate(property.AttributeName, ReadLines(property)))
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

    // A value that the converter counts as missing leaves __set alone, so everything that a missing attribute goes through follows: the initial value, the throw for a required member, the fallback key.
    private static string[] ReadLines(PropertyModel property)
    {
        var (value, converter) = (property.ValueType, property.Converter!.ReadConverter);
        var set = $"fields.{property.SetFlag} = true;";
        return property.Converter.CountsAsMissing
            ? [$"if (reader.TryReadAttribute<{value}, {converter}>(out var __v)) {{ fields.{property.Identifier} = __v; {set} }}"]
            : [$"fields.{property.Identifier} = reader.ReadAttribute<{value}, {converter}>();", set];
    }

    // Keys.{Builder}.TryParse is what KeyBuilderEmitter emits: it reports text the template cannot have produced, and the parts are a struct of the placeholders.
    private static ReadCandidate RestoreFromKey(TypeModel type, string keyAttribute, IEnumerable<PropertyModel> properties, string parts)
    {
        var key = type.Entity!.Keys().First(candidate => candidate.AttributeName == keyAttribute);
        var attribute = CodeText.Literal(keyAttribute);
        var lines = new List<string>
        {
            "__Ser.ConverterChecks.Expect(reader.ReadStartAttribute(), global::DynaSharp.AttributeType.String);",
            $"if (!Keys.{key.BuilderName}.TryParse(reader.GetStringUtf8(), out var {parts})) {{ __misfit = {attribute}; }}",
        };
        foreach (var property in properties)
        {
            lines.Add($"fields.{property.Identifier} = {parts}.{property.Identifier};");
            lines.Add($"fields.{property.SetFlag} = true;");
        }

        lines.Add("reader.ReadEndAttribute();");
        return new ReadCandidate(keyAttribute, lines);
    }

    // A key that is absent, of another type or of another shape restores nothing, so what an item lacks fails or defaults here as it does without a key.
    private static void EmitFallback(IndentedCode code, TypeModel type, IGrouping<string, PropertyModel> properties, string group)
    {
        var key = type.Entity!.Keys().First(candidate => candidate.AttributeName == properties.Key);
        code.Line($"if (({string.Join(" || ", properties.Select(static property => $"!fields.{property.SetFlag}"))})");
        code.Line($"    && __Ser.ConverterChecks.TryFindString(__item, {CodeText.Utf8(properties.Key)}, out var __text{group})");
        code.Line($"    && Keys.{key.BuilderName}.TryParse(__text{group}, out var __restored{group}))");
        code.Open();
        foreach (var property in properties)
        {
            code.Line($"if (!fields.{property.SetFlag}) {{ fields.{property.Identifier} = __restored{group}.{property.Identifier}; fields.{property.SetFlag} = true; }}");
        }

        code.Close();
        code.Blank();
    }

    private static void EmitDispatch(IndentedCode code, List<ReadCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        code.Line("var name = reader.GetAttributeNameUtf8();");
        code.Line("switch (name.Length)");
        code.Open();
        foreach (var length in candidates.OrderBy(static candidate => candidate.Attribute, StringComparer.Ordinal).GroupBy(static candidate => candidate.Utf8Length).OrderBy(static group => group.Key))
        {
            code.Line($"case {CodeText.Number(length.Key)}:");
            code.Indent();
            foreach (var candidate in length)
            {
                EmitCandidate(code, candidate);
            }

            code.Line("break;");
            code.Outdent();
        }

        code.Close();
        code.Blank();
    }

    private static void EmitCandidate(IndentedCode code, ReadCandidate candidate)
    {
        code.Line($"if (global::System.MemoryExtensions.SequenceEqual(name, {CodeText.Utf8(candidate.Attribute)}))");
        code.Open();
        code.Line($"attribute = {CodeText.Literal(candidate.Attribute)};");
        foreach (var line in candidate.Lines)
        {
            code.Line(line);
        }

        code.Line("continue;");
        code.Close();
        code.Blank();
    }

    private static void EmitFields(IndentedCode code, List<PropertyModel> read)
    {
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line("private struct __Fields");
        code.Open();
        foreach (var property in read)
        {
            code.Line($"public {property.ValueType} {property.Identifier}; public bool {property.SetFlag};");
        }

        code.Close();
    }

    /// <summary>What a read does for one attribute name.</summary>
    /// <param name="Attribute">The attribute.</param>
    /// <param name="Lines">The statements that read its value.</param>
    private sealed record ReadCandidate(string Attribute, IReadOnlyList<string> Lines)
    {
        public int Utf8Length { get; } = Encoding.UTF8.GetByteCount(Attribute);
    }
}
