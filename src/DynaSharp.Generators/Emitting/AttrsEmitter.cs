using System.Text.RegularExpressions;

namespace DynaSharp.Generators;

/// <summary>
/// Writes the nested <c>Attrs</c> class: one typed handle per stored attribute, for conditions, updates and projections. A nested document
/// has handles of its own, which <c>Order.Attrs.ShipTo.At(Address.Attrs.City)</c> combines into a path.
/// </summary>
internal static class AttrsEmitter
{
    /// <summary>What the name of a forwarder starts with. The converter of an enum is a file type, which no public signature can name, so a handle takes a nested struct that forwards to it.</summary>
    public const string ForwarderPrefix = "__";

    /// <summary>The name of the converter of a version that takes an item without one for version 0. An entity has one version, so one name will do.</summary>
    public const string VersionConverter = ForwarderPrefix + "VersionConverter";

    private const string Serialization = "global::DynaSharp.Serialization.";

    public static void Emit(IndentedCode code, TypeModel type)
    {
        var handles = type.Properties.Where(static property => property.Converter is not null && IsPlainName(property.AttributeName)).ToList();
        code.Line($"/// <summary>The attributes of <c>{type.Name}</c> as typed handles, for conditions, updates and projections. A raw path stays possible wherever a handle is.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public {(type.HidesInheritedAttrs ? "new " : string.Empty)}static class Attrs");
        code.Open();
        var isFirst = true;
        foreach (var handle in handles)
        {
            code.Separate(ref isFirst);
            EmitHandle(code, handle, type.Enums);
        }

        foreach (var version in handles.Where(static handle => handle.ReadsMissingAsZero))
        {
            code.Separate(ref isFirst);
            EmitVersionConverter(code, version);
        }

        foreach (var model in type.Enums)
        {
            var users = handles.Where(handle => Regex.IsMatch(handle.Converter!.WriteConverter, ConverterPattern(model))).ToList();
            if (users.Count > 0)
            {
                code.Separate(ref isFirst);
                EmitForwarder(code, model, users);
            }
        }

        code.Close();
    }

    // A path splits at these, so a handle would address something else. No path can address such an attribute.
    private static bool IsPlainName(string attributeName) => attributeName.IndexOfAny(['.', '[']) < 0;

    // "Shop_StatusConverter" must not match inside "Shop_StatusConverter2", the name a second enum of that text gets.
    private static string ConverterPattern(EnumModel model) => $@"\b{model.ConverterName}\b";

    private static void EmitHandle(IndentedCode code, PropertyModel handle, EquatableArray<EnumModel> enums)
    {
        var converter = enums.Aggregate(handle.Converter!.WriteConverter, static (text, model) => Regex.Replace(text, ConverterPattern(model), ForwarderPrefix + model.ConverterName));

        // The handle says it in its type, so that a property without [OmitEmpty] pays nothing, in a handle or in a command.
        converter = handle.OmitEmptyConverter is { } wrapper ? $"{wrapper}{converter}>" : converter;

        // A handle holds the type without its ?, for Set and the conditions, so this is the one place that tells a nullable property from another.
        converter = handle.Converter.ReadConverter == handle.Converter.WriteConverter ? converter : $"{Serialization}{NullableHandle(handle)}<{handle.InnerValueType()}, {converter}>";
        converter = handle.ReadsMissingAsZero ? VersionConverter : converter;
        code.Line($"/// <summary>The attribute <c>{CodeText.Xml(handle.AttributeName)}</c>.</summary>");
        code.Line($"{Accessibility(handle.IsPublic)} static global::DynaSharp.Attr<{handle.InnerValueType()}, {converter}> {handle.Identifier} => new({CodeText.Literal(handle.AttributeName)});");
    }

    // A nullable property reads through NullableConverter<T, C> if it is a value type and NullableReferenceConverter<T, C> if not. A model holds its converters as text so that it stays
    // comparable in an incremental pipeline, with no symbol left to ask, and the name that text starts with is what tells them apart.
    private static string NullableHandle(PropertyModel handle) =>
        handle.Converter!.ReadConverter.StartsWith(Serialization + "NullableConverter<", StringComparison.Ordinal) ? "NullableHandleConverter" : "NullableReferenceHandleConverter";

    private static string Accessibility(bool isPublic) => isPublic ? "public" : "internal";

    // Select asks a converter what a missing attribute means, so this is where the 0 that a read of the entity gives reaches a handle. For everything else it is the number converter, an Add included.
    private static void EmitVersionConverter(IndentedCode code, PropertyModel version)
    {
        var (type, converter) = (version.ValueType, version.Converter!.WriteConverter);
        code.Line($"/// <summary>The converter of <c>{CodeText.Xml(version.Name)}</c>, which reads a missing attribute as 0.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.Line($"{Accessibility(version.IsPublic)} readonly struct {VersionConverter} : {Serialization}IDynamoDbConverter<{type}>, {Serialization}IAddableConverter");
        code.Open();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static void Write({Serialization}DynamoDbWriter writer, {type} value) => {converter}.Write(writer, value);");
        code.Blank();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static {type} Read(ref {Serialization}DynamoDbReader reader, global::DynaSharp.AttributeType type) => {converter}.Read(ref reader, type);");
        code.Blank();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static bool TryReadMissing(out {type} value)");
        code.Open();
        code.Line("value = 0;");
        code.Line("return true;");
        code.Close();
        code.Close();
    }

    private static void EmitForwarder(IndentedCode code, EnumModel model, List<PropertyModel> users)
    {
        // HashSetConverter<E, C>, ReadOnlySetConverter<E, C> and SetArrayConverter<E, C> are where a converter serves as a set element.
        var arguments = $"<{model.FullName}, {model.ConverterName}>";
        var isElement = users.Exists(handle => handle.Converter!.WriteConverter.Contains("SetConverter" + arguments) || handle.Converter.WriteConverter.Contains("SetArrayConverter" + arguments));
        var converter = $"{Serialization}IDynamoDbConverter<{model.FullName}>";
        var element = isElement ? $", {Serialization}IDynamoDbSetElementConverter<{model.FullName}>" : string.Empty;
        code.Line($"/// <summary>The converter of <c>{CodeText.Xml(model.FullName)}</c>, in a form that a handle can name.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.Line($"{Accessibility(users.Exists(static handle => handle.IsPublic))} readonly struct {ForwarderPrefix}{model.ConverterName} : {converter}{element}");
        code.Open();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static void Write({Serialization}DynamoDbWriter writer, {model.FullName} value) => {model.ConverterName}.Write(writer, value);");
        code.Blank();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static {model.FullName} Read(ref {Serialization}DynamoDbReader reader, global::DynaSharp.AttributeType type) => {model.ConverterName}.Read(ref reader, type);");

        // Select asks the converter of the handle, so a name the enum lacks gives there what it gives a read of the entity.
        if (model.CountsUnknownAsMissing)
        {
            code.Blank();
            code.Line("/// <inheritdoc/>");
            code.Line($"public static bool CountsAsMissing(in {Serialization}DynamoDbReader reader, global::DynaSharp.AttributeType type) => {model.ConverterName}.CountsAsMissing(in reader, type);");
        }

        if (isElement)
        {
            code.Blank();
            code.Line("/// <inheritdoc/>");
            code.Line($"public static global::DynaSharp.AttributeType SetType => {model.ConverterName}.SetType;");
            code.Blank();
            code.Line("/// <inheritdoc/>");
            code.Line($"public static void WriteElement({Serialization}DynamoDbWriter writer, {model.FullName} value) => {model.ConverterName}.WriteElement(writer, value);");
            code.Blank();
            code.Line("/// <inheritdoc/>");
            code.Line($"public static {model.FullName} ReadElement(ref {Serialization}DynamoDbReader reader) => {model.ConverterName}.ReadElement(ref reader);");
        }

        code.Close();
    }
}
