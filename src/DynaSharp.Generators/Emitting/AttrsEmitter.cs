using System.Text.RegularExpressions;

namespace DynaSharp.Generators;

/// <summary>
/// Writes the nested <c>Attrs</c> class: one typed handle per stored attribute, for conditions, updates and projections. A nested document
/// has handles of its own, which <c>Order.Attrs.ShipTo.At(Address.Attrs.City)</c> combines into a path.
/// </summary>
internal static class AttrsEmitter
{
    private const string Serialization = "global::DynaSharp.Serialization.";

    // The converter of an enum is a file type, which no public signature can name, so a handle takes a nested struct that forwards to it.
    private const string ForwarderPrefix = "__";

    public static void Emit(IndentedCode code, TypeModel type)
    {
        var handles = type.Properties.Where(static property => property.Converter is not null && IsPlainName(property.AttributeName)).ToList();
        code.Line($"/// <summary>The attributes of <c>{type.Name}</c> as typed handles, for conditions, updates and projections. A raw path stays possible wherever a handle is.</summary>");
        code.Line($"public {(type.HidesInheritedAttrs ? "new " : string.Empty)}static class Attrs");
        code.Open();
        var isFirst = true;
        foreach (var handle in handles)
        {
            code.Separate(ref isFirst);
            EmitHandle(code, handle, type.Enums);
        }

        foreach (var model in type.Enums)
        {
            var users = handles.Where(handle => FindConverter(model).IsMatch(handle.Converter!.WriteConverter)).ToList();
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
    private static Regex FindConverter(EnumModel model) => new($@"\b{model.ConverterName}\b");

    private static void EmitHandle(IndentedCode code, PropertyModel handle, EquatableArray<EnumModel> enums)
    {
        var converter = enums.Aggregate(handle.Converter!.WriteConverter, static (text, model) => FindConverter(model).Replace(text, ForwarderPrefix + model.ConverterName));

        // The handle says it in its type, so that a property without [OmitEmpty] pays nothing, in a handle or in a command.
        converter = handle.OmitsEmpty ? $"{Serialization}OmitEmptyConverter<{converter}>" : converter;
        var value = handle.ValueType.EndsWith("?", StringComparison.Ordinal) ? handle.ValueType.Substring(0, handle.ValueType.Length - 1) : handle.ValueType;
        code.Line($"/// <summary>The attribute <c>{CodeText.Xml(handle.AttributeName)}</c>.</summary>");
        code.Line($"{Accessibility(handle.IsPublic)} static global::DynaSharp.Attr<{value}, {converter}> {handle.Identifier} => new({CodeText.Literal(handle.AttributeName)});");
    }

    private static string Accessibility(bool isPublic) => isPublic ? "public" : "internal";

    private static void EmitForwarder(IndentedCode code, EnumModel model, List<PropertyModel> users)
    {
        // HashSetConverter<E, C>, ReadOnlySetConverter<E, C> and SetArrayConverter<E, C> are where a converter serves as a set element.
        var elementOf = new Regex($@"Set(Array)?Converter<{Regex.Escape(model.FullName)}, {model.ConverterName}>");
        var isElement = users.Exists(handle => elementOf.IsMatch(handle.Converter!.WriteConverter));
        var converter = $"{Serialization}IDynamoDbConverter<{model.FullName}>";
        var element = isElement ? $", {Serialization}IDynamoDbSetElementConverter<{model.FullName}>" : string.Empty;
        code.Line($"/// <summary>The converter of <c>{CodeText.Xml(model.FullName)}</c>, in a form that a handle can name.</summary>");
        code.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.Line($"{Accessibility(users.Exists(static handle => handle.IsPublic))} readonly struct {ForwarderPrefix}{model.ConverterName} : {converter}{element}");
        code.Open();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static void Write({Serialization}DynamoDbWriter writer, {model.FullName} value) => {model.ConverterName}.Write(writer, value);");
        code.Blank();
        code.Line("/// <inheritdoc/>");
        code.Line($"public static {model.FullName} Read(ref {Serialization}DynamoDbReader reader, global::DynaSharp.AttributeType type) => {model.ConverterName}.Read(ref reader, type);");
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
