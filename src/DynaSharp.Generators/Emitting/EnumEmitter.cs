using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Writes the <c>file</c> struct that converts an enum, by name, or by value for <c>[Flags]</c>, whose combinations have no name. It is also the
/// element converter of a set of the enum. A name that is unknown is a <see cref="FormatException"/>, which the code around the read wraps with the
/// attribute it was reading.
/// </summary>
internal static class EnumEmitter
{
    private const string AttributeType = "global::DynaSharp.AttributeType";

    private const string GlobalPrefix = "global::";

    public static void Emit(StringBuilder file, EnumModel model)
    {
        file.Line();
        file.Line($"file readonly struct {model.ConverterName} : __Ser.IDynamoDbConverter<{model.FullName}>, __Ser.IDynamoDbSetElementConverter<{model.FullName}>");
        file.Line("{");
        if (model.StoreAsNumber)
        {
            EmitByValue(file, model);
        }
        else
        {
            EmitByName(file, model);
        }

        file.Line("}");
    }

    private static void EmitByValue(StringBuilder file, EnumModel model)
    {
        EmitSetType(file, "NumberSet");
        EmitWrite(file, model, $"writer.WriteNumber(({model.UnderlyingType})value)");
        EmitRead(file, model, "Number");
        EmitWriteElement(file, model, $"writer.WriteNumberSetElement(({model.UnderlyingType})value)");
        file.Line();
        file.Line($"    public static {model.FullName} ReadElement(ref __Ser.DynamoDbReader reader) => ({model.FullName})reader.GetNumber<{model.UnderlyingType}>();");
    }

    private static void EmitByName(StringBuilder file, EnumModel model)
    {
        var display = model.FullName.Substring(GlobalPrefix.Length).Replace("@", string.Empty);
        EmitSetType(file, "StringSet");
        file.Line();
        file.Line($"    public static global::System.ReadOnlySpan<byte> FindName({model.FullName} value) => value switch");
        file.Line("    {");
        foreach (var member in model.Members.Where(static member => member.IsCanonical))
        {
            file.Line($"        {model.FullName}.{member.Identifier} => {CodeText.Utf8(member.Name)},");
        }

        file.Line("        _ => default,");
        file.Line("    };");
        file.Line();
        file.Line($"    public static global::System.ReadOnlySpan<byte> GetName({model.FullName} value)");
        file.Line("    {");
        file.Line("        var name = FindName(value);");
        file.Line($"        return name.IsEmpty ? throw global::DynaSharp.DynamoDbMappingException.UndefinedEnumValue({CodeText.Literal(display)}) : name;");
        file.Line("    }");
        EmitTryParseName(file, model);
        EmitWrite(file, model, "writer.WriteString(GetName(value))");
        EmitRead(file, model, "String");
        EmitWriteElement(file, model, "writer.WriteStringSetElement(GetName(value))");
        file.Line();
        file.Line($"    public static {model.FullName} ReadElement(ref __Ser.DynamoDbReader reader) =>");
        file.Line($"        TryParseName(reader.GetStringUtf8(), out var value) ? value : throw new global::System.FormatException({CodeText.Literal($"The text is not a member of {display}.")});");
    }

    private static void EmitTryParseName(StringBuilder file, EnumModel model)
    {
        file.Line();
        file.Line($"    public static bool TryParseName(global::System.ReadOnlySpan<byte> name, out {model.FullName} value)");
        file.Line("    {");
        if (model.Members.Count > 0)
        {
            file.Line("        switch (name.Length)");
            file.Line("        {");
            foreach (var length in model.Members.OrderBy(static member => member.Name, StringComparer.Ordinal).GroupBy(static member => member.Utf8Length).OrderBy(static group => group.Key))
            {
                file.Line($"            case {CodeText.Number(length.Key)}:");
                foreach (var member in length)
                {
                    file.Line($"                if (global::System.MemoryExtensions.SequenceEqual(name, {CodeText.Utf8(member.Name)})) {{ value = {model.FullName}.{member.Identifier}; return true; }}");
                }

                file.Line("                break;");
            }

            file.Line("        }");
            file.Line();
        }

        file.Line("        value = default;");
        file.Line("        return false;");
        file.Line("    }");
    }

    private static void EmitSetType(StringBuilder file, string setType) =>
        file.Line($"    public static {AttributeType} SetType => {AttributeType}.{setType};");

    private static void EmitWrite(StringBuilder file, EnumModel model, string call)
    {
        file.Line();
        file.Line($"    public static void Write(__Ser.DynamoDbWriter writer, {model.FullName} value) => {call};");
    }

    private static void EmitWriteElement(StringBuilder file, EnumModel model, string call)
    {
        file.Line();
        file.Line($"    public static void WriteElement(__Ser.DynamoDbWriter writer, {model.FullName} value) => {call};");
    }

    private static void EmitRead(StringBuilder file, EnumModel model, string wireType)
    {
        file.Line();
        file.Line($"    public static {model.FullName} Read(ref __Ser.DynamoDbReader reader, {AttributeType} type)");
        file.Line("    {");
        file.Line($"        __Ser.ConverterChecks.Expect(type, {AttributeType}.{wireType});");
        file.Line("        return ReadElement(ref reader);");
        file.Line("    }");
    }
}
