namespace DynaSharp.Generators;

/// <summary>
/// Writes the nested builder of a key without a template, for example <c>Keys.Pk</c> of a <c>[PartitionKey]</c> on a property. The attribute of the property is the key, so what a template
/// builder works out is the job of the converter of the property here: <c>Of</c> goes through it, which is what makes a number, binary or custom key come out with its type. There is nothing
/// to parse and no prefix to match, so the builder has no <c>Parse</c> and no <c>Prefix</c>.
/// </summary>
internal static class PlainKeyBuilderEmitter
{
    private const string StringConverter = "global::DynaSharp.Serialization.StringConverter";

    private const string ValueOf = "global::DynaSharp.Serialization.ConverterChecks.ToAttributeValue";

    private const string AttributeValue = "global::DynaSharp.AttributeValue";

    /// <summary>Writes the builder.</summary>
    /// <param name="code">Where the code goes.</param>
    /// <param name="key">The key.</param>
    /// <param name="property">The property that is the key.</param>
    /// <param name="isSortKey">Whether it is a sort key, whose value goes into a <c>SortKey</c> condition, and not into a <c>PartitionKey</c>.</param>
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

    private static void EmitOf(IndentedCode code, KeyModel key, PropertyModel property, string signature, bool isSortKey, TypeModel type)
    {
        var converter = property.Converter!.WriteConverter;
        var value = converter == StringConverter
            ? $"{AttributeValue}.FromString({property.ParameterName})"
            : $"{ValueOf}<{property.InnerValueType()}, {converter}>({property.ParameterName})";
        if (isSortKey)
        {
            code.Line("/// <summary>Gives the key as it is stored, for a condition such as <c>SortKey.Ge(...)</c>.</summary>");
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
