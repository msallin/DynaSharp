namespace DynaSharp.Generators;

/// <summary>
/// Writes the nested builder of a key without a template, for example <c>Keys.Pk</c> of a <c>[PartitionKey]</c> on a property. The attribute of the property is the key, so what a template
/// builder works out is the job of the converter of the property here: <c>Of</c> goes through it, which is what makes a number, binary or custom key come out with its type. There is nothing
/// to parse and no prefix to work out, so the builder has no <c>Parse</c> and no <c>Prefix</c>. On a sort key it has the conditions that fit what is stored: DynamoDB compares that value itself,
/// so a range needs no bounds of its own, only a stored form in the order of the values.
/// </summary>
internal static class PlainKeyBuilderEmitter
{
    private const string StringConverter = "global::DynaSharp.Serialization.StringConverter";

    private const string ValueOf = "global::DynaSharp.Serialization.ConverterChecks.ToAttributeValue";

    private const string AttributeValue = "global::DynaSharp.AttributeValue";

    private const string Condition = "global::DynaSharp.SortKeyCondition";

    // The converters that write text in the order of the value: fixed widths, and a string as it is. A TimeSpan is not one of them, nor is an enum, or a converter nobody can read.
    private static readonly HashSet<string> OrderedText =
    [
        StringConverter,
        "global::DynaSharp.Serialization.GuidConverter",
        "global::DynaSharp.Serialization.DateTimeOffsetConverter",
        "global::DynaSharp.Serialization.DateTimeConverter",
        "global::DynaSharp.Serialization.SdkDateTimeConverter",
        "global::DynaSharp.Serialization.DateOnlyConverter",
        "global::DynaSharp.Serialization.TimeOnlyConverter",
    ];

    /// <summary>Writes the builder.</summary>
    /// <param name="code">Where the code goes.</param>
    /// <param name="key">The key.</param>
    /// <param name="property">The property that is the key.</param>
    /// <param name="isSortKey">Whether it is a sort key, whose value goes into a <c>SortKeyCondition</c>, and not into a <c>PartitionKey</c>.</param>
    /// <param name="type">The entity.</param>
    public static void Emit(IndentedCode code, KeyModel key, PropertyModel property, bool isSortKey, TypeModel type)
    {
        var signature = $"{property.InnerValueType()} {property.ParameterName}";
        code.Blank();
        code.Line($"/// <summary>Builds the {(isSortKey ? "sort" : "partition")} key <c>{CodeText.Xml(key.AttributeName)}</c> of <c>{type.Name}</c>, which is the property <c>{property.Name}</c> as stored.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public static class {key.BuilderName}");
        code.Open();
        EmitOf(code, key, property, signature, isSortKey, type);
        if (isSortKey)
        {
            EmitConditions(code, key, property, signature);
        }

        // A list of strings would query a number with a string, which DynamoDB rejects.
        if (key.WireType == KeyWireType.String)
        {
            var text = isSortKey ? "AsString()" : "Value";
            code.Blank();
            code.Line("/// <summary>Formats the complete key.</summary>");
            code.Line($"public static string Format({signature}) => Of({property.ParameterName}).{text};");
        }

        code.Close();
    }

    private static void EmitConditions(IndentedCode code, KeyModel key, PropertyModel property, string signature)
    {
        var converter = property.Converter!.WriteConverter;
        var parameter = property.ParameterName;
        code.Blank();
        code.Line("/// <summary>The condition <c>=</c> on the key, which is one item of the partition.</summary>");
        code.Line($"public static {Condition} Eq({signature}) => {Condition}.Eq(Of({parameter}));");
        if (key.WireType is KeyWireType.Number or KeyWireType.Binary || OrderedText.Contains(converter))
        {
            EmitRange(code, "Ge", "&gt;=", signature, parameter);
            EmitRange(code, "Gt", "&gt;", signature, parameter);
            EmitRange(code, "Le", "&lt;=", signature, parameter);
            EmitRange(code, "Lt", "&lt;", signature, parameter);
            code.Blank();
            code.Line("/// <summary>The condition <c>BETWEEN</c> on the key, with both bounds included. DynamoDB rejects a lower bound above the upper one.</summary>");
            code.Line($"public static {Condition} Between({property.InnerValueType()} from, {property.InnerValueType()} to) => {Condition}.Between(Of(from), Of(to));");
        }

        if (converter == StringConverter)
        {
            code.Blank();
            code.Line("/// <summary>The condition <c>begins_with</c> on the key. It also matches a longer value of the same start.</summary>");
            code.Line($"public static {Condition} BeginsWith(string prefix) => {Condition}.BeginsWith(prefix);");
        }
    }

    private static void EmitRange(IndentedCode code, string name, string comparison, string signature, string parameter)
    {
        code.Blank();
        code.Line($"/// <summary>The condition <c>{comparison}</c> on the key. It has only this one end, so what else sorts there in the partition matches too.</summary>");
        code.Line($"public static {Condition} {name}({signature}) => {Condition}.{name}(Of({parameter}));");
    }

    private static void EmitOf(IndentedCode code, KeyModel key, PropertyModel property, string signature, bool isSortKey, TypeModel type)
    {
        var converter = property.Converter!.WriteConverter;
        var value = converter == StringConverter
            ? $"{AttributeValue}.FromString({property.ParameterName})"
            : $"{ValueOf}<{property.InnerValueType()}, {converter}>({property.ParameterName})";
        if (isSortKey)
        {
            code.Line("/// <summary>Gives the key as it is stored, for a condition such as <c>SortKeyCondition.Ge(...)</c>.</summary>");
            code.Line($"public static {AttributeValue} Of({signature}) => {value};");
        }
        else
        {
            var partitionKey = $"global::DynaSharp.PartitionKey<{type.FullName}>";
            code.Line($"/// <summary>Gives the key as a key of <c>{type.Name}</c>, which a query of another entity does not take.</summary>");
            code.Line($"public static {partitionKey} Of({signature}) => new {partitionKey}({value}, {CodeText.Literal(key.AttributeName)});");
        }
    }
}
