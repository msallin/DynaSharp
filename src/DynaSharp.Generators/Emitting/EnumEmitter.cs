namespace DynaSharp.Generators;

/// <summary>
/// Writes the <c>file</c> struct that converts an enum, by name, or by value for <c>[Flags]</c>, whose combinations have no name. It is also the
/// element converter of a set of the enum. A name that is unknown is a <see cref="FormatException"/>, which the code around the read wraps with the
/// attribute it was reading. An enum with <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c> also answers <c>CountsAsMissing</c> for such a name, which only the read of a
/// property asks: <c>Read</c> and <c>ReadElement</c> stay strict, so an element of a collection still throws.
/// </summary>
internal static class EnumEmitter
{
    private const string AttributeType = "global::DynaSharp.AttributeType";

    private const string GlobalPrefix = "global::";

    public static void Emit(IndentedCode code, EnumModel model)
    {
        code.Blank();
        code.Line(CodeText.GeneratedCode);
        code.Line($"file readonly struct {model.ConverterName} : __Ser.IDynamoDbConverter<{model.FullName}>, __Ser.IDynamoDbSetElementConverter<{model.FullName}>");
        code.Open();
        if (model.StoreAsNumber)
        {
            EmitByValue(code, model);
        }
        else
        {
            EmitByName(code, model);
        }

        code.Close();
    }

    private static void EmitByValue(IndentedCode code, EnumModel model)
    {
        EmitSetType(code, "NumberSet");
        EmitWrite(code, model, $"writer.WriteNumber(({model.UnderlyingType})value)");
        EmitRead(code, model, "Number");
        EmitWriteElement(code, model, $"writer.WriteNumberSetElement(({model.UnderlyingType})value)");
        code.Blank();
        code.Line($"public static {model.FullName} ReadElement(ref __Ser.DynamoDbReader reader) => ({model.FullName})reader.GetNumber<{model.UnderlyingType}>();");
    }

    private static void EmitByName(IndentedCode code, EnumModel model)
    {
        var display = model.FullName.Substring(GlobalPrefix.Length).Replace("@", string.Empty);
        EmitSetType(code, "StringSet");
        code.Blank();
        code.Line($"public static global::System.ReadOnlySpan<byte> FindName({model.FullName} value) => value switch");
        code.Open();
        foreach (var member in model.Members.Where(static member => member.IsCanonical))
        {
            code.Line($"{model.FullName}.{member.Identifier} => {CodeText.Utf8(member.Name)},");
        }

        code.Line("_ => default,");
        code.Close("};");
        code.Blank();
        code.Line($"public static global::System.ReadOnlySpan<byte> GetName({model.FullName} value)");
        code.Open();
        code.Line("var name = FindName(value);");
        code.Line($"return name.IsEmpty ? throw global::DynaSharp.DynamoDbMappingException.UndefinedEnumValue({CodeText.Literal(display)}) : name;");
        code.Close();
        EmitTryParseName(code, model);
        EmitWrite(code, model, "writer.WriteString(GetName(value))");
        EmitRead(code, model, "String");
        if (model.CountsUnknownAsMissing)
        {
            code.Blank();
            code.Line($"public static bool CountsAsMissing(in __Ser.DynamoDbReader reader, {AttributeType} type) =>");
            code.Line($"    type == {AttributeType}.String && !TryParseName(reader.GetStringUtf8(), out _);");
        }

        EmitWriteElement(code, model, "writer.WriteStringSetElement(GetName(value))");
        code.Blank();
        code.Line($"public static {model.FullName} ReadElement(ref __Ser.DynamoDbReader reader) =>");
        code.Line($"    TryParseName(reader.GetStringUtf8(), out var value) ? value : throw new global::System.FormatException({CodeText.Literal($"The text is not a member of {display}.")});");
    }

    private static void EmitTryParseName(IndentedCode code, EnumModel model)
    {
        code.Blank();
        code.Line($"public static bool TryParseName(global::System.ReadOnlySpan<byte> name, out {model.FullName} value)");
        code.Open();
        if (model.Members.Count > 0)
        {
            code.Line("switch (name.Length)");
            code.Open();
            foreach (var length in model.Members.OrderBy(static member => member.Name, StringComparer.Ordinal).GroupBy(static member => member.Utf8Length).OrderBy(static group => group.Key))
            {
                code.Line($"case {CodeText.Number(length.Key)}:");
                code.Indent();
                foreach (var member in length)
                {
                    code.Line($"if (global::System.MemoryExtensions.SequenceEqual(name, {CodeText.Utf8(member.Name)})) {{ value = {model.FullName}.{member.Identifier}; return true; }}");
                }

                code.Line("break;");
                code.Outdent();
            }

            code.Close();
            code.Blank();
        }

        code.Line("value = default;");
        code.Line("return false;");
        code.Close();
    }

    private static void EmitSetType(IndentedCode code, string setType) =>
        code.Line($"public static {AttributeType} SetType => {AttributeType}.{setType};");

    private static void EmitWrite(IndentedCode code, EnumModel model, string call)
    {
        code.Blank();
        code.Line($"public static void Write(__Ser.DynamoDbWriter writer, {model.FullName} value) => {call};");
    }

    private static void EmitWriteElement(IndentedCode code, EnumModel model, string call)
    {
        code.Blank();
        code.Line($"public static void WriteElement(__Ser.DynamoDbWriter writer, {model.FullName} value) => {call};");
    }

    private static void EmitRead(IndentedCode code, EnumModel model, string wireType)
    {
        code.Blank();
        code.Line($"public static {model.FullName} Read(ref __Ser.DynamoDbReader reader, {AttributeType} type)");
        code.Open();
        code.Line($"__Ser.ConverterChecks.Expect(type, {AttributeType}.{wireType});");
        code.Line("return ReadElement(ref reader);");
        code.Close();
    }
}
