using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Writes <c>WriteKey</c> and <c>WriteAttributes</c>. Template keys go through <c>Keys.{Builder}.Write</c>, which <see cref="KeyBuilderEmitter"/> emits:
/// it takes the writer and the placeholders in template order, and writes the value only. Attribute names cost no string, and the value of
/// a nullable property is only touched through a pattern variable, whose name is unique per method because such variables leak into the
/// enclosing scope.
/// </summary>
internal static class WriteEmitter
{
    private const string Indent = "        ";

    private const string VersionParameter = "__version";

    public static void Emit(StringBuilder body, TypeModel type)
    {
        if (type.Entity is { } entity)
        {
            EmitWriteKey(body, type, entity);
        }

        var version = type.Entity?.VersionProperty is { } name ? type.Property(name) : null;
        body.Line();
        if (version is null)
        {
            body.Line($"    static void __Map.IDynamoDbDocument<{type.FullName}>.WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        }
        else
        {
            EmitVersionedWrites(body, type, version);
            body.Line($"    private static void __WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity, {version.ValueType} {VersionParameter})");
        }

        body.Line("    {");
        var variables = new VariableNames();
        if (type.Entity is not null)
        {
            EmitKeyAttributes(body, type.Entity, variables);
        }

        foreach (var property in type.Properties)
        {
            EmitProperty(body, property, variables, property == version ? VersionParameter : $"entity.{property.Identifier}");
        }

        body.Line("    }");
    }

    // A plain write gives the version as it stands, and a put gives the next one. A value that does not fit the property fails here, not on a read of the item.
    private static void EmitVersionedWrites(StringBuilder body, TypeModel type, PropertyModel version)
    {
        body.Line($"    static void __Map.IDynamoDbDocument<{type.FullName}>.WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity) => __WriteAttributes(writer, entity, entity.{version.Identifier});");
        body.Line();
        body.Line($"    static long __Map.IDynamoDbDocument<{type.FullName}>.WriteNextVersion(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        body.Line("    {");
        body.Line($"        var held = entity.{version.Identifier};");
        body.Line($"        __WriteAttributes(writer, entity, checked(({version.ValueType})(held + 1)));");
        body.Line("        return checked((long)held);");
        body.Line("    }");
        body.Line();
        body.Line($"    static long __Map.IDynamoDbEntity<{type.FullName}>.ReadVersion({type.FullName} entity) => checked((long)entity.{version.Identifier});");
        body.Line();
    }

    private static void EmitWriteKey(StringBuilder body, TypeModel type, EntityModel entity)
    {
        body.Line();
        body.Line($"    static void __Map.IDynamoDbEntity<{type.FullName}>.WriteKey(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        body.Line("    {");
        EmitTableKey(body, type, entity.PartitionKey);
        if (entity.SortKey is { } sortKey)
        {
            EmitTableKey(body, type, sortKey);
        }

        body.Line("    }");
    }

    private static void EmitTableKey(StringBuilder body, TypeModel type, KeyModel key)
    {
        if (key.PlainProperty is { } name)
        {
            var property = type.Property(name);
            EmitConverterCall(body, Indent, property, $"entity.{property.Identifier}");
        }
        else
        {
            EmitTemplateKey(body, Indent, key, []);
        }
    }

    // A plain key is the attribute of its property, and a shared attribute is written by the key that claimed it first.
    private static List<KeyModel> WithOwnAttribute(params KeyModel?[] keys) =>
        [.. keys.OfType<KeyModel>().Where(static key => key is { PlainProperty: null, SharesAttribute: false })];

    private static void EmitKeyAttributes(StringBuilder body, EntityModel entity, VariableNames variables)
    {
        foreach (var key in WithOwnAttribute(entity.PartitionKey, entity.SortKey))
        {
            EmitTemplateKey(body, Indent, key, []);
        }

        foreach (var index in entity.Indexes)
        {
            EmitIndex(body, index, variables);
        }

        if (entity.Discriminator is { } discriminator)
        {
            body.Line($"{Indent}writer.WriteAttributeName({CodeText.Utf8(entity.DiscriminatorAttribute)});");
            body.Line($"{Indent}writer.WriteString({CodeText.Utf8(discriminator)});");
        }
    }

    private static void EmitIndex(StringBuilder body, IndexModel index, VariableNames variables)
    {
        // A local index shares the partition key of the table, which is written already.
        var written = WithOwnAttribute(index.IsLocal ? null : index.PartitionKey, index.SortKey);
        foreach (var key in written.Where(static key => key.Gates.Count > 0))
        {
            EmitSharedKey(body, key, variables);
        }

        var keys = written.Where(static key => key.Gates.Count == 0).ToList();
        var inputs = index.Sparse && keys.Count > 0 ? index.NullableInputs.ToDictionary(static input => input, _ => variables.Next()) : [];
        if (inputs.Count > 0)
        {
            var conditions = inputs.Select(static input => $"entity.{Names.Identifier(input.Key)} is {{ }} {input.Value}");
            body.Line($"{Indent}if ({string.Join(" && ", conditions)})");
            body.Line($"{Indent}{{");
        }

        foreach (var key in keys)
        {
            EmitTemplateKey(body, inputs.Count > 0 ? Indent + "    " : Indent, key, inputs);
        }

        if (inputs.Count > 0)
        {
            body.Line($"{Indent}}}");
        }
    }

    // The inputs of the key are in every gate, so they are what binds the values. Anything left of a gate is another index's business, and only tested:
    // if (entity.Nick is { } __v0 && ((entity.Age is not null) || (entity.City is not null)))
    private static void EmitSharedKey(StringBuilder body, KeyModel key, VariableNames variables)
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
            EmitTemplateKey(body, Indent, key, inputs);
            return;
        }

        body.Line($"{Indent}if ({string.Join(" && ", conditions)})");
        body.Line($"{Indent}{{");
        EmitTemplateKey(body, Indent + "    ", key, inputs);
        body.Line($"{Indent}}}");
    }

    private static void EmitTemplateKey(StringBuilder body, string indent, KeyModel key, Dictionary<string, string> inputs)
    {
        var values = key.Template!.Segments
            .OfType<PlaceholderSegment>()
            .Select(placeholder => inputs.TryGetValue(placeholder.Property, out var input) ? input : $"entity.{Names.Identifier(placeholder.Property)}");
        body.Line($"{indent}writer.WriteAttributeName({CodeText.Utf8(key.AttributeName)});");
        body.Line($"{indent}Keys.{key.BuilderName}.Write({string.Join(", ", values.Prepend("writer"))});");
    }

    private static void EmitProperty(StringBuilder body, PropertyModel property, VariableNames variables, string value)
    {
        if (property.Write == WriteCondition.Always)
        {
            EmitConverterCall(body, Indent, property, value);
        }
        else if (property.Write != WriteCondition.Never)
        {
            var variable = variables.Next();
            var pattern = property.Write switch
            {
                WriteCondition.WhenNotNull => "{ }",
                WriteCondition.WhenNotEmptyCount => "{ Count: > 0 }",
                _ => "{ Length: > 0 }",
            };
            body.Line($"{Indent}if ({value} is {pattern} {variable})");
            body.Line($"{Indent}{{");
            EmitConverterCall(body, Indent + "    ", property, variable);
            body.Line($"{Indent}}}");
        }
    }

    private static void EmitConverterCall(StringBuilder body, string indent, PropertyModel property, string value)
    {
        body.Line($"{indent}writer.WriteAttributeName({CodeText.Utf8(property.AttributeName)});");
        body.Line($"{indent}{property.Converter!.WriteCall(value)};");
    }

    /// <summary>Hands out the names of pattern variables within one method.</summary>
    private sealed class VariableNames
    {
        private int _next;

        public string Next() => "__v" + CodeText.Number(_next++);
    }
}
