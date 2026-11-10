namespace DynaSharp.Generators;

/// <summary>
/// Writes <c>WriteKey</c> and <c>WriteAttributes</c>. Template keys go through <c>Keys.{Builder}.Write</c>, which <see cref="KeyBuilderEmitter"/> emits:
/// it takes the writer and the placeholders in template order, and writes the value only. Attribute names cost no string, and the value of
/// a nullable property is only touched through a pattern variable, whose name is unique per method because such variables leak into the
/// enclosing scope.
/// </summary>
internal static class WriteEmitter
{
    // A DateTime of no kind, and NaN or an infinity, alone or in a list, a dictionary or a set, an empty set in a list or a dictionary, and JSON that DynamoDB cannot store. No name here is the start of the name of another built-in.
    // WriteEmitterTests holds the list against the built-in converters of the library.
    internal static readonly string[] RefusingConverters =
    [
        "global::DynaSharp.Serialization.DateTimeConverter",
        "global::DynaSharp.Serialization.NumberConverter<double>",
        "global::DynaSharp.Serialization.NumberConverter<float>",
        "global::DynaSharp.Serialization.NumberConverter<global::System.Half>",
        "global::DynaSharp.Serialization.NumberSetElement<double>",
        "global::DynaSharp.Serialization.NumberSetElement<float>",
        "global::DynaSharp.Serialization.NumberSetElement<global::System.Half>",
        "global::DynaSharp.Serialization.NonEmptySetConverter<",
        "global::DynaSharp.Serialization.DictionaryConverter<",
        "global::DynaSharp.Serialization.ReadOnlyDictionaryConverter<",
        "global::DynaSharp.Serialization.JsonElementConverter",
    ];

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
            EmitKeyAttributes(code, type, type.Entity, variables);
        }

        foreach (var property in type.Properties)
        {
            EmitProperty(code, type, property, variables, property == version ? VersionParameter : $"entity.{property.Identifier}");
        }

        code.Close();
    }

    // A plain write gives the version as it stands, and a put gives the next one. A next version that does not fit the property, or the long of a write result, fails here, not on a
    // read of the item, and names the property as a value a converter refuses does:
    // if (held >= int.MaxValue) { throw new global::System.ArgumentOutOfRangeException(null, "The property Revision of Note, ... version " + held.ToString(...) + ", ..."); }
    private static void EmitVersionedWrites(IndentedCode code, TypeModel type, PropertyModel version)
    {
        var valueType = version.InnerValueType();
        var (limit, holder, hint) = valueType switch
        {
            "ulong" => ("(ulong)long.MaxValue", "the long of a write result", string.Empty),
            "long" => ("long.MaxValue", "the long property", string.Empty),
            _ => ($"{valueType}.MaxValue", $"the {valueType} property", " A long property holds more."),
        };
        var where = $"The property {version.Name} of {type.Name}, the attribute \"{version.AttributeName}\", cannot be written: it holds version ";
        var why = $", and a put stores the next one, which {holder} cannot hold.{hint}";

        code.Line(CodeText.GeneratedCode);
        code.Line($"static void __Map.IDynamoDbDocument<{type.FullName}>.WriteAttributes(__Ser.DynamoDbWriter writer, {type.FullName} entity) => __WriteAttributes(writer, entity, entity.{version.Identifier});");
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"static long __Map.IDynamoDbDocument<{type.FullName}>.WriteNextVersion(__Ser.DynamoDbWriter writer, {type.FullName} entity)");
        code.Open();
        code.Line($"var held = entity.{version.Identifier};");
        code.Line($"if (held >= {limit})");
        code.Open();
        code.Line($"throw new global::System.ArgumentOutOfRangeException(null, {CodeText.Literal(where)} + held.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + {CodeText.Literal(why)});");
        code.Close();
        code.Blank();
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
            EmitConverterCall(code, type, property, $"entity.{property.Identifier}");
        }
        else
        {
            EmitTemplateKey(code, key, []);
        }
    }

    // A plain key is the attribute of its property, and a shared attribute is written by the key that claimed it first.
    private static List<KeyModel> WithOwnAttribute(params KeyModel?[] keys) =>
        [.. keys.OfType<KeyModel>().Where(static key => key is { PlainProperty: null, SharesAttribute: false })];

    private static void EmitKeyAttributes(IndentedCode code, TypeModel type, EntityModel entity, VariableNames variables)
    {
        foreach (var key in WithOwnAttribute(entity.PartitionKey, entity.SortKey))
        {
            EmitTemplateKey(code, key, []);
        }

        foreach (var index in entity.Indexes)
        {
            EmitIndex(code, type, index, variables);
        }

        if (entity.Discriminator is { } discriminator)
        {
            code.Line($"writer.WriteAttributeName({CodeText.Utf8(entity.DiscriminatorAttribute)});");
            code.Line($"writer.WriteString({CodeText.Utf8(discriminator)});");
        }
    }

    private static void EmitIndex(IndentedCode code, TypeModel type, IndexModel index, VariableNames variables)
    {
        // A local index shares the partition key of the table, which is written already.
        var written = WithOwnAttribute(index.IsLocal ? null : index.PartitionKey, index.SortKey);
        foreach (var key in written.Where(static key => key.Gates.Count > 0))
        {
            EmitSharedKey(code, type, key, variables);
        }

        var keys = written.Where(static key => key.Gates.Count == 0).ToList();
        var inputs = index.Sparse && keys.Count > 0 ? index.NullableInputs.ToDictionary(static input => input, _ => variables.Next()) : [];
        if (inputs.Count > 0)
        {
            var conditions = inputs.Select(input => $"entity.{Names.Identifier(input.Key)} is {Present(type, input.Key)} {input.Value}");
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
    private static void EmitSharedKey(IndentedCode code, TypeModel type, KeyModel key, VariableNames variables)
    {
        var inputs = key.Template!.Segments
            .OfType<PlaceholderSegment>()
            .Where(static placeholder => placeholder.InputCanBeAbsent)
            .ToDictionary(static placeholder => placeholder.Property, _ => variables.Next());
        var alternatives = key.Gates.Select(gate => gate.Where(input => !inputs.ContainsKey(input)).ToList()).ToList();
        var conditions = inputs.Select(input => $"entity.{Names.Identifier(input.Key)} is {Present(type, input.Key)} {input.Value}").ToList();
        if (alternatives.TrueForAll(static gate => gate.Count > 0))
        {
            conditions.Add($"({string.Join(" || ", alternatives.Select(gate => $"({string.Join(" && ", gate.Select(input => $"entity.{Names.Identifier(input)} {Tested(type, input)}"))})"))})");
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

    // A string with [OmitEmpty] of its own is left out when it is empty, so its gate is the condition that writes its attribute. Null is absent for every other input, also one that has no attribute.
    private static bool OmitsEmpty(TypeModel type, string input) =>
        type.Properties.Any(property => property.Name == input && property.Write == WriteCondition.WhenNotEmptyLength);

    private static string Present(TypeModel type, string input) => OmitsEmpty(type, input) ? "{ Length: > 0 }" : "{ }";

    private static string Tested(TypeModel type, string input) => OmitsEmpty(type, input) ? "is { Length: > 0 }" : "is not null";

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
    private static void EmitProperty(IndentedCode code, TypeModel type, PropertyModel property, VariableNames variables, string value)
    {
        if (property.Write == WriteCondition.Never)
        {
            return;
        }

        var omission = property.Converter!.OmissionForwarder;
        if (property.Write == WriteCondition.Always && omission is null)
        {
            EmitConverterCall(code, type, property, value);
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
        EmitConverterCall(code, type, property, variable);
        code.Close();
    }

    // A write through a built-in converter that refuses a value of its type gets a catch that names the property. The try costs the success path nothing, and the filter lets every
    // other exception through as it is:
    // try { global::DynaSharp.Serialization.DateTimeConverter.Write(writer, entity.At); }
    // catch (global::System.ArgumentException __e) when (__Ser.ConverterChecks.IsRefusedValue(__e)) { throw __Ser.ConverterChecks.RefusedValue(__e, "Order", "At", "at"); }
    private static void EmitConverterCall(IndentedCode code, TypeModel type, PropertyModel property, string value)
    {
        code.Line($"writer.WriteAttributeName({CodeText.Utf8(property.AttributeName)});");
        var call = $"{property.Converter!.WriteCall(value)};";
        if (!CanRefuse(property.Converter.WriteConverter))
        {
            code.Line(call);
            return;
        }

        code.Line("try");
        code.Open();
        code.Line(call);
        code.Close();
        code.Line("catch (global::System.ArgumentException __e) when (__Ser.ConverterChecks.IsRefusedValue(__e))");
        code.Open();
        code.Line($"throw __Ser.ConverterChecks.RefusedValue(__e, {CodeText.Literal(type.Name)}, {CodeText.Literal(property.Name)}, {CodeText.Literal(property.AttributeName)});");
        code.Close();
    }

    // Whether the converter, or one it holds for its elements, is a built-in that refuses a value of its type on write. A new built-in that does goes into RefusingConverters,
    // which a test enforces, and its exception into ConverterChecks.IsRefusedValue.
    private static bool CanRefuse(string converter) =>
        Array.Exists(RefusingConverters, refusing => converter.IndexOf(refusing, StringComparison.Ordinal) >= 0);

    /// <summary>Hands out the names of pattern variables within one method.</summary>
    private sealed class VariableNames
    {
        private int _next;

        public string Next() => "__v" + CodeText.Number(_next++);
    }
}
