namespace DynaSharp.Generators;

/// <summary>
/// Writes the nested builder of one key template, for example <c>Keys.Sk</c>, and the record of its parsed values. Everything that touches
/// the text goes through <c>KeyWriter</c> and <c>KeyReader</c>, so an encoding rule exists once and the builders only say in which order to
/// call them.
/// </summary>
/// <param name="code">Where the code goes.</param>
/// <param name="key">The key.</param>
/// <param name="template">The template of the key.</param>
/// <param name="isSortKey">Whether the key can be matched by prefix: <c>begins_with</c> and ranges exist only on sort keys.</param>
/// <param name="entityName">The entity, for documentation.</param>
/// <param name="entityType">The entity as code, which a partition key carries in its type.</param>
internal sealed class KeyBuilderEmitter(IndentedCode code, KeyModel key, KeyTemplate template, bool isSortKey, string entityName, string entityType)
{
    private const string Writer = "global::DynaSharp.Keys.KeyWriter";

    private const string Reader = "global::DynaSharp.Keys.KeyReader";

    private const string FormatException = "global::DynaSharp.KeyFormatException";

    private const string Utf8Text = "global::System.ReadOnlySpan<byte>";

    private const string SortKey = "global::DynaSharp.SortKey";

    private const string Local = "__key";

    private const string Buffer = "BufferLength";

    private const string InvalidValue = "/// <exception cref=\"" + FormatException + "\">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>";

    private const string InvalidText = "/// <exception cref=\"" + FormatException + "\">The text is not a key of this template.</exception>";

    // The UTF-8 bytes of U+10FFFF that KeyWriter.WriteUpperBound appends.
    private const int UpperBoundLength = 4;

    private readonly List<PlaceholderSegment> _placeholders = [.. template.Segments.OfType<PlaceholderSegment>()];

    private readonly string _delimiters = CodeText.Utf8(template.Delimiters);

    private string Parts => key.BuilderName + "Parts";

    // The text of a template without placeholders. The template itself is not that text: it may spell a brace as {{.
    private string LiteralText => string.Concat(template.Segments.OfType<LiteralSegment>().Select(static literal => literal.Text));

    public void Emit()
    {
        var attribute = CodeText.Xml(key.AttributeName);
        code.Blank();
        EmitParts(attribute);
        code.Blank();
        code.Line($"/// <summary>Builds and parses the {(isSortKey ? "sort" : "partition")} key <c>{attribute}</c> of <c>{entityName}</c>, with the template <c>{CodeText.Xml(template.Text)}</c>.</summary>");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public static class {key.BuilderName}");
        code.Open();
        code.Line($"private const int {Buffer} = {template.StackBufferLength};");
        code.Blank();
        EmitFormat();
        if (isSortKey)
        {
            EmitPrefixes();
            EmitPrefixPartial();
        }
        else
        {
            EmitOf();
        }

        EmitParsers();
        EmitWrite();
        code.Close();
    }

    private static string Signature(IEnumerable<PlaceholderSegment> placeholders) =>
        string.Join(", ", placeholders.Select(static placeholder => $"{placeholder.ValueType} {placeholder.ParameterName}"));

    private static string Arguments(IEnumerable<PlaceholderSegment> placeholders) =>
        string.Join(", ", placeholders.Select(static placeholder => placeholder.ParameterName));

    private static string WriteLiteral(string text) => $"{Local}.WriteLiteral({CodeText.Utf8(text)});";

    private string EncodeCall() => $"Encode({string.Join(", ", new[] { "ref " + Local }.Concat(_placeholders.Select(static placeholder => placeholder.ParameterName)))});";

    private void EmitParts(string attribute)
    {
        code.Line($"/// <summary>The values of a parsed <c>{attribute}</c>.</summary>");
        foreach (var placeholder in _placeholders)
        {
            code.Line($"/// <param name=\"{placeholder.Property}\">The value of the placeholder <c>{placeholder.Property}</c>.</param>");
        }

        var members = _placeholders.Select(static placeholder => $"{placeholder.ValueType} {Names.Identifier(placeholder.Property)}");
        code.Line(CodeText.GeneratedCode);
        code.Line($"public readonly record struct {Parts}({string.Join(", ", members)});");
    }

    private void EmitFormat()
    {
        code.Line("/// <summary>Formats the complete key.</summary>");
        if (_placeholders.Count == 0)
        {
            code.Line($"public static string Format() => {CodeText.Literal(LiteralText)};");
        }
        else
        {
            code.Line(InvalidValue);
            EmitText("Format", Signature(_placeholders), Buffer, [EncodeCall()]);
        }

        code.Blank();
    }

    private void EmitOf()
    {
        var partitionKey = $"global::DynaSharp.PartitionKey<{entityType}>";
        code.Line($"/// <summary>Formats the complete key as a key of <c>{entityName}</c>, which a query of another entity does not take.</summary>");
        if (_placeholders.Count > 0)
        {
            code.Line(InvalidValue);
        }

        code.Line($"public static {partitionKey} Of({Signature(_placeholders)}) => new {partitionKey}(Format({Arguments(_placeholders)}), {CodeText.Literal(key.AttributeName)});");
        code.Blank();
    }

    // A literal between placeholders ends on the delimiter of the next one, and all of it belongs to the prefix, because every key under the
    // prefix has it. Of a literal at the end only the delimiter does: the rest is not a field, and other types on the attribute may differ there.
    private void EmitPrefixes()
    {
        var segments = template.Segments;
        for (var index = 0; index < segments.Count; index++)
        {
            var isLast = index + 1 == segments.Count;
            if (segments[index] is LiteralSegment literal && !(isLast && index == 0))
            {
                var text = isLast ? literal.Text.Substring(0, 1) : literal.Text;
                var bound = segments.Take(index).ToList();
                EmitPrefix([.. bound.OfType<PlaceholderSegment>()], text, [.. bound.Select(WriteSegment), WriteLiteral(text)]);
            }
        }
    }

    private void EmitPrefix(List<PlaceholderSegment> bound, string text, List<string> lines)
    {
        var position = bound.Count == 0 ? "before the first placeholder" : $"through <c>{bound[bound.Count - 1].Property}</c>";
        code.Line($"/// <summary>The prefix {position}, for <c>begins_with</c>. It ends on a delimiter, so it does not match a longer value of the last field.</summary>");
        if (bound.Count == 0)
        {
            code.Line($"public static string Prefix() => {CodeText.Literal(text)};");
        }
        else
        {
            code.Line(InvalidValue);
            EmitText("Prefix", Signature(bound), Buffer, lines);
        }

        code.Blank();
        code.Line("/// <summary>The same prefix plus U+10FFFF, above every key under it: <c>BETWEEN Prefix(a) AND UpperBound(b)</c> includes the keys at <c>b</c>.</summary>");
        if (bound.Count > 0)
        {
            code.Line(InvalidValue);
        }

        EmitText("UpperBound", Signature(bound), $"{Buffer} + {UpperBoundLength}", [.. lines, $"{Local}.WriteUpperBound();"]);
        code.Blank();
        code.Line("/// <summary>The condition <c>begins_with</c> on the same prefix.</summary>");
        if (bound.Count > 0)
        {
            code.Line(InvalidValue);
        }

        code.Line($"public static {SortKey} BeginsWith({Signature(bound)}) => {SortKey}.BeginsWith(Prefix({Arguments(bound)}));");
        code.Blank();
    }

    private void EmitPrefixPartial()
    {
        if (_placeholders.LastOrDefault() is { Encoding: StringEncoding } last)
        {
            var length = template.Segments.ToList().IndexOf(last) + 1;
            code.Line($"/// <summary>A prefix that ends inside <c>{last.Property}</c>, validated like a value. It also matches longer values, which is the point: autocomplete, geohash cells.</summary>");
            code.Line(InvalidValue);
            EmitText("PrefixPartial", Signature(_placeholders), Buffer, template.Segments.Take(length).Select(WriteSegment));
            code.Blank();
        }
    }

    private void EmitParsers()
    {
        code.Line("/// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>");
        code.Line(InvalidText);
        code.Line($"public static {Parts} Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();");
        code.Blank();
        code.Line("/// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>");
        code.Line(InvalidText);
        code.Line($"public static {Parts} Parse({Utf8Text} utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();");
        code.Blank();
        code.Line("/// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>");
        code.Line($"public static bool TryParse(string? text, out {Parts} parts)");
        code.Open();
        OpenWriter(Buffer);
        code.Line($"if ({Local}.TryWriteText(text))");
        code.Open();
        code.Line($"return TryParse({Local}.WrittenSpan, out parts);");
        code.Close();
        code.Blank();
        code.Line("parts = default;");
        code.Line("return false;");
        CloseWriter();
        code.Close();
        code.Blank();
        code.Line("/// <summary>Tries to parse a key given as UTF-8.</summary>");
        code.Line($"public static bool TryParse({Utf8Text} utf8Text, out {Parts} parts)");
        code.Open();
        code.Line($"var {Local} = new {Reader}(utf8Text);");
        EmitReads();
        code.Blank();
        code.Line("parts = default;");
        code.Line("return false;");
        code.Close();
        code.Blank();
    }

    private void EmitReads()
    {
        var reads = new List<string>();
        var values = new List<string>();
        foreach (var segment in template.Segments)
        {
            if (segment is LiteralSegment literal)
            {
                reads.Add($"{Local}.TryReadLiteral({CodeText.Utf8(literal.Text)})");
            }
            else
            {
                var encoding = ((PlaceholderSegment)segment).Encoding;
                var target = $"__p{values.Count}";
                reads.Add(encoding.TryReadCall(Local, target, _delimiters));
                values.Add(encoding.ReadValue(target));
            }
        }

        code.Line($"if ({reads[0]}");
        foreach (var read in reads.Skip(1))
        {
            code.Line($"    && {read}");
        }

        code.Line($"    && {Local}.IsAtEnd)");
        code.Open();
        code.Line($"parts = new {Parts}({string.Join(", ", values)});");
        code.Line("return true;");
        code.Close();
    }

    private void EmitWrite()
    {
        var parameters = _placeholders.Count == 0 ? string.Empty : ", " + Signature(_placeholders);
        code.Line($"internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer{parameters})");
        code.Open();
        if (_placeholders.Count == 0)
        {
            code.Line($"__writer.WriteString({CodeText.Utf8(LiteralText)});");
        }
        else
        {
            OpenWriter(Buffer);
            code.Line(EncodeCall());
            code.Line($"__writer.WriteString({Local}.WrittenSpan);");
            CloseWriter();
        }

        code.Close();
        code.Blank();
        code.Line($"private static {FormatException} Invalid() => new {FormatException}({CodeText.Literal($"The text is not a key of the template {template.Text}.")});");
        if (_placeholders.Count > 0)
        {
            code.Blank();
            code.Line($"private static void Encode(ref {Writer} {Local}{parameters})");
            code.Open();
            foreach (var segment in template.Segments)
            {
                code.Line(WriteSegment(segment));
            }

            code.Close();
        }
    }

    private void EmitText(string name, string signature, string buffer, IEnumerable<string> lines)
    {
        code.Line($"public static string {name}({signature})");
        code.Open();
        OpenWriter(buffer);
        foreach (var line in lines)
        {
            code.Line(line);
        }

        code.Line($"return {Local}.ToString();");
        CloseWriter();
        code.Close();
    }

    private void OpenWriter(string buffer)
    {
        code.Line($"var {Local} = new {Writer}(stackalloc byte[{buffer}]);");
        code.Line("try");
        code.Open();
    }

    private void CloseWriter()
    {
        code.Close();
        code.Line("finally");
        code.Open();
        code.Line($"{Local}.Dispose();");
        code.Close();
    }

    private string WriteSegment(TemplateSegment segment) => segment is LiteralSegment literal
        ? WriteLiteral(literal.Text)
        : WritePlaceholder((PlaceholderSegment)segment);

    private string WritePlaceholder(PlaceholderSegment placeholder) =>
        placeholder.Encoding.WriteCall(Local, placeholder.ParameterName, _delimiters, CodeText.Literal(placeholder.Property)) + ";";
}
