namespace DynaSharp.Generators;

/// <summary>
/// Writes <c>WriteKey</c> and <c>WriteAttributes</c>. Template keys go through <c>Keys.{Builder}.Write</c>, which <see cref="KeyBuilderEmitter"/> emits:
/// it takes the writer and the placeholders in template order, and writes the value only. Attribute names cost no string, and the value of
/// a nullable property is only touched through a pattern variable, whose name is unique per method because such variables leak into the
/// enclosing scope.
/// </summary>
internal static class WriteEmitter
{
    private const string VersionParameter = "__version";

    public static void Emit(IndentedCode code, TypeModel type)
    {
        if (type.Entity is { } entity)
        {
            EmitWriteKey(code, type, entity);
        }

        var version = type.Entity?.VersionProperty is { } name ? type.Property(name) : null;
        code.Blank();
        if (version is null)
        {
            code.Line(CodeText.GeneratedCode);
            code.Line($"static void __Map.IDynamoDbDocument<{type.FullName}>.WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        }
        else
        {
            EmitVersionedWrites(code, type, version);
            code.Line(CodeText.GeneratedCode);
            code.Line($"private static void __WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity, {version.ValueType} {VersionParameter})");
        }

        code.Open();
        var variables = new VariableNames();
        if (type.Entity is not null)
        {
            EmitKeyAttributes(code, type.Entity, variables);
        }

        foreach (var property in type.Properties)
        {
            EmitProperty(code, property, variables, property == version ? VersionParameter : $"entity.{property.Identifier}");
        }

        code.Close();
    }

    // A plain write gives the version as it stands, and a put gives the next one. A value that does not fit the property fails here, not on a read of the item.
    private static void EmitVersionedWrites(IndentedCode code, TypeModel type, PropertyModel version)
    {
        code.Line(CodeText.GeneratedCode);
        code.Line($"static void __Map.IDynamoDbDocument<{type.FullName}>.WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity) => __WriteAttributes(writer, entity, entity.{version.Identifier});");
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"static long __Map.IDynamoDbDocument<{type.FullName}>.WriteNextVersion(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        code.Open();
        code.Line($"var held = entity.{version.Identifier};");
        code.Line($"__WriteAttributes(writer, entity, checked(({version.ValueType})(held + 1)));");
        code.Line("return checked((long)held);");
        code.Close();
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"static long __Map.IDynamoDbEntity<{type.FullName}>.ReadVersion({type.FullName} entity) => checked((long)entity.{version.Identifier});");
        code.Blank();
    }

    private static void EmitWriteKey(IndentedCode code, TypeModel type, EntityModel entity)
    {
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"static void __Map.IDynamoDbEntity<{type.FullName}>.WriteKey(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        code.Open();
        EmitTableKey(code, type, entity.PartitionKey);
        if (entity.SortKey is { } sortKey)
        {
            EmitTableKey(code, type, sortKey);
        }

        code.Close();
    }

    private static void EmitTableKey(IndentedCode code, TypeModel type, KeyModel key)
    {
        if (key.PlainProperty is { } name)
        {
            var property = type.Property(name);
            EmitConverterCall(code, property, $"entity.{property.Identifier}");
        }
        else
        {
            EmitTemplateKey(code, key, []);
        }
    }

    // A plain key is the attribute of its property, and a shared attribute is written by the key that claimed it first.
    private static List<KeyModel> WithOwnAttribute(params KeyModel?[] keys) =>
        [.. keys.OfType<KeyModel>().Where(static key => key is { PlainProperty: null, SharesAttribute: false })];

    private static void EmitKeyAttributes(IndentedCode code, EntityModel entity, VariableNames variables)
    {
        foreach (var key in WithOwnAttribute(entity.PartitionKey, entity.SortKey))
        {
            EmitTemplateKey(code, key, []);
        }

        foreach (var index in entity.Indexes)
        {
            EmitIndex(code, index, variables);
        }

        if (entity.Discriminator is { } discriminator)
        {
            code.Line($"writer.WriteAttributeName({CodeText.Utf8(entity.DiscriminatorAttribute)});");
            code.Line($"writer.WriteString({CodeText.Utf8(discriminator)});");
        }
    }

    private static void EmitIndex(IndentedCode code, IndexModel index, VariableNames variables)
    {
        // A local index shares the partition key of the table, which is written already.
        var written = WithOwnAttribute(index.IsLocal ? null : index.PartitionKey, index.SortKey);
        foreach (var key in written.Where(static key => key.Gates.Count > 0))
        {
            EmitSharedKey(code, key, variables);
        }

        var keys = written.Where(static key => key.Gates.Count == 0).ToList();
        var inputs = index.Sparse && keys.Count > 0 ? index.NullableInputs.ToDictionary(static input => input, _ => variables.Next()) : [];
        if (inputs.Count > 0)
        {
            var conditions = inputs.Select(static input => $"entity.{Names.Identifier(input.Key)} is {{ }} {input.Value}");
            code.Line($"if ({string.Join(" && ", conditions)})");
            code.Open();
        }

        foreach (var key in keys)
        {
            EmitTemplateKey(code, key, inputs);
        }

        if (inputs.Count > 0)
        {
            code.Close();
        }
    }

    // The inputs of the key are in every gate, so they are what binds the values. Anything left of a gate is another index's business, and only tested:
    // if (entity.Nick is { } __v0 && ((entity.Age is not null) || (entity.City is not null)))
    private static void EmitSharedKey(IndentedCode code, KeyModel key, VariableNames variables)
    {
        var inputs = key.Template!.Segments
            .OfType<PlaceholderSegment>()
            .Where(static placeholder => placeholder.InputIsNullable)
            .ToDictionary(static placeholder => placeholder.Property, _ => variables.Next());
        var alternatives = key.Gates.Select(gate => gate.Where(input => !inputs.ContainsKey(input)).ToList()).ToList();
        var conditions = inputs.Select(static input => $"entity.{Names.Identifier(input.Key)} is {{ }} {input.Value}").ToList();
        if (alternatives.TrueForAll(static gate => gate.Count > 0))
        {
            conditions.Add($"({string.Join(" || ", alternatives.Select(static gate => $"({string.Join(" && ", gate.Select(static input => $"entity.{Names.Identifier(input)} is not null"))})"))})");
        }

        if (conditions.Count == 0)
        {
            EmitTemplateKey(code, key, inputs);
            return;
        }

        code.Line($"if ({string.Join(" && ", conditions)})");
        code.Open();
        EmitTemplateKey(code, key, inputs);
        code.Close();
    }

    private static void EmitTemplateKey(IndentedCode code, KeyModel key, Dictionary<string, string> inputs)
    {
        var values = key.Template!.Segments
            .OfType<PlaceholderSegment>()
            .Select(placeholder => inputs.TryGetValue(placeholder.Property, out var input) ? input : $"entity.{Names.Identifier(placeholder.Property)}");
        code.Line($"writer.WriteAttributeName({CodeText.Utf8(key.AttributeName)});");
        code.Line($"Keys.{key.BuilderName}.Write({string.Join(", ", values.Prepend("writer"))});");
    }

    // A converter that answers for itself is asked after every other condition, so it never hears of a null. The value is a pattern variable, so that a computed property runs once:
    // if (entity.Tags is { } __v0 && !global::DynaSharp.Serialization.NullableReferenceHandleConverter<List<string>, TagsConverter>.IsOmitted(__v0))
    private static void EmitProperty(IndentedCode code, PropertyModel property, VariableNames variables, string value)
    {
        if (property.Write == WriteCondition.Never)
        {
            return;
        }

        var omission = property.Converter!.OmissionForwarder;
        if (property.Write == WriteCondition.Always && omission is null)
        {
            EmitConverterCall(code, property, value);
            return;
        }

        var variable = variables.Next();
        var pattern = property.Write switch
        {
            WriteCondition.Always => "var",
            WriteCondition.WhenNotNull => "{ }",
            WriteCondition.WhenNotEmptyCount => "{ Count: > 0 }",
            _ => "{ Length: > 0 }",
        };
        var kept = omission is null ? string.Empty : $" && !{omission}.IsOmitted({variable})";
        code.Line($"if ({value} is {pattern} {variable}{kept})");
        code.Open();
        EmitConverterCall(code, property, variable);
        code.Close();
    }

    private static void EmitConverterCall(IndentedCode code, PropertyModel property, string value)
    {
        code.Line($"writer.WriteAttributeName({CodeText.Utf8(property.AttributeName)});");
        code.Line($"{property.Converter!.WriteCall(value)};");
    }

    /// <summary>Hands out the names of pattern variables within one method.</summary>
    private sealed class VariableNames
    {
        private int _next;

        public string Next() => "__v" + CodeText.Number(_next++);
    }
}
