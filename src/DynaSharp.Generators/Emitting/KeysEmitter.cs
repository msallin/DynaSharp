namespace DynaSharp.Generators;

/// <summary>
/// Writes what a type offers for addressing its data into the body of its type: the <c>Attrs</c> handles of every mapped type, and for an
/// entity the nested <c>Keys</c> class, and <c>Indexes</c> if it has an index. The body is members only, each after a blank line like the other parts of a body; the file around it,
/// and the converters of enums it refers to, belong to <see cref="EntityEmitter"/>.
/// </summary>
/// <remarks>
/// Generated code targets C# 11 and is compiled by consumers with implicit usings off, so every name is fully qualified and collection
/// expressions and primary constructors stay out.
/// </remarks>
internal static class KeysEmitter
{
    public static void Emit(IndentedCode code, TypeModel type)
    {
        code.Blank();
        AttrsEmitter.Emit(code, type);
        if (type.Entity is not { } entity)
        {
            return;
        }

        code.Blank();
        code.Line($"/// <summary>The key builders of <c>{type.Name}</c>.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public {(type.HidesInheritedKeys ? "new " : string.Empty)}static class Keys");
        code.Open();
        EmitPrimaryKey(code, type, entity);
        foreach (var (key, isSortKey) in KeysWithBuilders(entity))
        {
            if (key.Template is { } template)
            {
                new KeyBuilderEmitter(code, key, template, isSortKey, type.Name, type.FullName).Emit();
            }
            else
            {
                PlainKeyBuilderEmitter.Emit(code, key, type.Property(key.PlainProperty!), isSortKey, type);
            }
        }

        code.Close();
        if (entity.Indexes.Count > 0)
        {
            code.Blank();
            EmitIndexes(code, type, entity);
        }
    }

    private static void EmitIndexes(IndentedCode code, TypeModel type, EntityModel entity)
    {
        code.Line($"/// <summary>The names of the indexes of <c>{type.Name}</c>, for <c>table.Index(...)</c>.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public {(type.HidesInheritedIndexes ? "new " : string.Empty)}static class Indexes");
        code.Open();
        var isFirst = true;
        foreach (var index in entity.Indexes)
        {
            code.Separate(ref isFirst);
            code.Line($"/// <summary>The index <c>{index.IndexName}</c>.</summary>");
            code.Line($"public const string {Names.IndexConstant(index.IndexName)} = {CodeText.Literal(index.IndexName)};");
        }

        code.Close();
    }

    // Every key has a builder. The partition key of a local index is the one of the table, which has its builder already.
    private static List<(KeyModel Key, bool IsSortKey)> KeysWithBuilders(EntityModel entity)
    {
        var keys = new List<(KeyModel Key, bool IsSortKey)>();
        foreach (var key in PrimaryKeys(entity))
        {
            keys.Add((key, key == entity.SortKey));
        }

        foreach (var index in entity.Indexes)
        {
            if (!index.IsLocal)
            {
                keys.Add((index.PartitionKey, false));
            }

            if (index.SortKey is { } sortKey)
            {
                keys.Add((sortKey, true));
            }
        }

        return keys;
    }

    private static List<KeyModel> PrimaryKeys(EntityModel entity) =>
        entity.SortKey is { } sortKey ? [entity.PartitionKey, sortKey] : [entity.PartitionKey];

    private static void EmitPrimaryKey(IndentedCode code, TypeModel type, EntityModel entity)
    {
        var keys = PrimaryKeys(entity);
        var inputs = new List<KeyInput>();
        foreach (var input in keys.SelectMany(key => Inputs(type, key)))
        {
            // A property in both keys is one value and one parameter.
            if (!inputs.Exists(known => known.Property == input.Property))
            {
                inputs.Add(input);
            }
        }

        var signature = string.Join(", ", inputs.Select(static input => $"{input.ValueType} {input.Parameter}"));
        code.Line($"/// <summary>Creates the primary key of <c>{type.Name}</c>. It holds the values and checks none of them: this returns for a value that a key cannot hold, and <c>KeyFormatException</c> comes when the key is written, by the command it goes to, where <c>Format</c> and the <c>Of</c> of a builder of a partition key throw at once.</summary>");
        code.Line($"public static PrimaryKey Of({signature}) => new PrimaryKey({string.Join(", ", inputs.Select(static input => input.Parameter))});");
        code.Blank();
        code.Line($"/// <summary>The primary key of <c>{type.Name}</c>, which holds the values of the key and costs no string until it is written.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public readonly struct PrimaryKey : global::DynaSharp.Mapping.IKey<{type.FullName}>");
        code.Open();
        if (inputs.Count > 0)
        {
            EmitFields(code, inputs, signature);
        }

        code.Line("/// <inheritdoc/>");
        code.Line("public void WriteKey(global::DynaSharp.Serialization.DynamoDbWriter writer)");
        code.Open();
        foreach (var key in keys)
        {
            var fields = Inputs(type, key).Select(input => $"_{inputs.FindIndex(known => known.Property == input.Property)}").ToList();
            code.Line($"writer.WriteAttributeName({CodeText.Utf8(key.AttributeName)});");
            code.Line(key.Template is null
                ? $"{type.Property(key.PlainProperty!).Converter!.WriteCall(fields[0])};"
                : $"{key.BuilderName}.Write({string.Join(", ", (List<string>)["writer", .. fields])});");
        }

        code.Close();
        EmitValueOf(code, entity, inputs);
        code.Close();
    }

    // An entity whose keys give their inputs back has no use for this, and the default of the interface says so.
    private static void EmitValueOf(IndentedCode code, EntityModel entity, List<KeyInput> inputs)
    {
        var held = KeyTemplateEmitter.HeldInputs(entity);
        if (held.Count > 0)
        {
            code.Blank();
            code.Line("/// <inheritdoc/>");
            code.Line("public object? ValueOf(string propertyName)");
            code.Open();
            foreach (var property in held)
            {
                code.Line($"if (propertyName == {CodeText.Literal(property)})");
                code.Open();
                code.Line($"return _{inputs.FindIndex(known => known.Property == property)};");
                code.Close();
                code.Blank();
            }

            code.Line("return null;");
            code.Close();
        }
    }

    // A key of literals has none: a struct may not declare an internal constructor without parameters.
    private static void EmitFields(IndentedCode code, List<KeyInput> inputs, string signature)
    {
        for (var index = 0; index < inputs.Count; index++)
        {
            code.Line($"private readonly {inputs[index].ValueType} _{index};");
        }

        code.Blank();
        code.Line($"internal PrimaryKey({signature})");
        code.Open();
        for (var index = 0; index < inputs.Count; index++)
        {
            code.Line($"_{index} = {inputs[index].Parameter};");
        }

        code.Close();
        code.Blank();
    }

    private static IEnumerable<KeyInput> Inputs(TypeModel type, KeyModel key)
    {
        if (key.Template is { } template)
        {
            return template.Segments.OfType<PlaceholderSegment>().Select(static placeholder => new KeyInput(placeholder.Property, placeholder.ParameterName, placeholder.ValueType));
        }

        var property = type.Property(key.PlainProperty!);
        return [new KeyInput(property.Name, property.ParameterName, property.ValueType)];
    }

    /// <summary>A value that <c>Keys.Of</c> takes.</summary>
    /// <param name="Property">The property.</param>
    /// <param name="Parameter">The parameter as code.</param>
    /// <param name="ValueType">The type as code.</param>
    private readonly record struct KeyInput(string Property, string Parameter, string ValueType);
}
