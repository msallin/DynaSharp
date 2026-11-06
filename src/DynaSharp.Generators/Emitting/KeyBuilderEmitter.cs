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
/// <param name="isTableKey">Whether the key is part of <c>Keys.Of</c>, which checks its values through <c>Check</c>.</param>
/// <param name="entityName">The entity, for documentation.</param>
/// <param name="entityType">The entity as code, which a partition key carries in its type.</param>
internal sealed class KeyBuilderEmitter(IndentedCode code, KeyModel key, KeyTemplate template, bool isSortKey, bool isTableKey, string entityName, string entityType)
{
    private const string Writer = "global::DynaSharp.Keys.KeyWriter";

    private const string Reader = "global::DynaSharp.Keys.KeyReader";

    private const string FormatException = "global::DynaSharp.KeyFormatException";

    private const string Utf8Text = "global::System.ReadOnlySpan<byte>";

    private const string Condition = "global::DynaSharp.SortKeyCondition";

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
            EmitEq();
            EmitPrefixes();
            EmitPrefixPartial();
            EmitFinalRanges();
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

    private static string Call(string method, IEnumerable<PlaceholderSegment> placeholders, string last)
    {
        var arguments = (List<string>)[.. placeholders.Select(static placeholder => placeholder.ParameterName), last];
        return $"{method}({string.Join(", ", arguments)})";
    }

    // A bound of Between beside the placeholders before it, which may hold the same name: {From:d2}#{To:d2}#{Id}.
    private static string BoundName(List<PlaceholderSegment> leading, string name) => Names.Free(name, leading.Select(static placeholder => placeholder.ParameterName));

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

    private void EmitEq()
    {
        code.Line("/// <summary>The condition <c>=</c> on the complete key, which is one item of the partition.</summary>");
        if (_placeholders.Count > 0)
        {
            code.Line(InvalidValue);
        }

        code.Line($"public static {Condition} Eq({Signature(_placeholders)}) => {Condition}.Eq(Format({Arguments(_placeholders)}));");
        code.Blank();
    }

    private void EmitOf()
    {
        var partitionKey = $"global::DynaSharp.PartitionKey<{entityType}>";
        var tryOf = template.CanReject ? " <c>TryOf</c> says the same without an exception, for a value from outside." : string.Empty;
        code.Line($"/// <summary>Formats the complete key as a key of <c>{entityName}</c>, which a query of another entity does not take.{tryOf}</summary>");
        if (_placeholders.Count > 0)
        {
            code.Line(InvalidValue);
        }

        code.Line($"public static {partitionKey} Of({Signature(_placeholders)}) => new {partitionKey}(Format({Arguments(_placeholders)}), {CodeText.Literal(key.AttributeName)});");
        code.Blank();
        if (template.CanReject)
        {
            EmitTryOf(partitionKey);
        }
    }

    // One pass over the values, as Of makes in Format: the text it checks is the text of the key.
    private void EmitTryOf(string partitionKey)
    {
        code.Line("/// <summary>Does what <c>Of</c> does and returns <see langword=\"null\"/> for a value that <c>Of</c> throws <c>KeyFormatException</c> for, for <c>is { } key</c>. Nothing is thrown on the way, and it costs what <c>Of</c> costs.</summary>");
        code.Line($"public static {partitionKey}? TryOf({Signature(_placeholders)})");
        code.Open();
        OpenWriter(Buffer, "false");
        code.Line(EncodeCall());
        code.Line($"return {Local}.IsInvalid ? null : new {partitionKey}({Local}.ToString(), {CodeText.Literal(key.AttributeName)});");
        CloseWriter();
        code.Close();
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
                var goesOn = !isLast || literal.Text.Length > text.Length;
                EmitPrefix([.. bound.OfType<PlaceholderSegment>()], text, [.. bound.Select(WriteSegment), WriteLiteral(text)], goesOn);
            }
        }
    }

    private void EmitPrefix(List<PlaceholderSegment> bound, string text, List<string> lines, bool goesOn)
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
        code.Line("/// <summary>The same prefix plus U+10FFFF, above every key under it, for the upper end of a range: <c>BETWEEN Prefix(a) AND UpperBound(b)</c> includes both ends, with every key at <c>b</c>.</summary>");
        if (bound.Count > 0)
        {
            code.Line(InvalidValue);
        }

        EmitText("UpperBound", Signature(bound), $"{Buffer} + {UpperBoundLength}", [.. lines, $"{Local}.WriteUpperBound();"]);
        code.Blank();
        var fields = bound.Count == 0 ? string.Empty : $" Its arguments fill the fields through <c>{bound[bound.Count - 1].Property}</c>, each as the template formats it, so a value keeps only what its format holds.";
        code.Line($"/// <summary>The condition <c>begins_with</c> on the same prefix.{fields}</summary>");
        if (bound.Count > 0)
        {
            code.Line(InvalidValue);
        }

        code.Line($"public static {Condition} BeginsWith({Signature(bound)}) => {Condition}.BeginsWith(Prefix({Arguments(bound)}));");
        code.Blank();
        EmitRanges(bound, "Prefix", "UpperBound", goesOn);
    }

    // The fields of a key that ends in a placeholder have no prefix: nothing ends the last one, and every value is a key. So the complete key is both bounds, which keeps a
    // longer string above a shorter one, where the U+10FFFF of an upper bound would put it inside.
    private void EmitFinalRanges()
    {
        if (template.Segments[template.Segments.Count - 1] is PlaceholderSegment)
        {
            EmitRanges(_placeholders, "Format", "Format", goesOn: false);
        }
    }

    // What holds for every value or does not exist. No value means no range: Ge() of a prefix reads what sorts after the template, which is not a question about this key, and
    // BeginsWith() is the one that means all of it. A range of the last field needs that field in order, and Between keeps the fields before it equal, so they can sort any way
    // they like. An open range goes on across those fields, so all of them have to be in order.
    private void EmitRanges(List<PlaceholderSegment> bound, string low, string high, bool goesOn)
    {
        if (bound.Count > 0 && IsInOrder(bound[bound.Count - 1]))
        {
            EmitBetween(bound, low, high, goesOn);
            if (goesOn)
            {
                EmitRange(bound);
            }

            if (bound.TrueForAll(IsInOrder))
            {
                EmitOpenRange("Ge", "&gt;=", "from these values on", low, bound);
                EmitOpenRange("Gt", "&gt;", "after these values", high, bound);
                EmitOpenRange("Le", "&lt;=", "up to these values", high, bound);
                EmitOpenRange("Lt", "&lt;", "before these values", low, bound);
            }
        }
    }

    private void EmitBetween(List<PlaceholderSegment> bound, string low, string high, bool goesOn)
    {
        var last = bound[bound.Count - 1];
        var leading = bound.GetRange(0, bound.Count - 1);
        var from = BoundName(leading, "from");
        var to = BoundName(leading, "to");
        var parameters = (List<string>)[.. leading.Select(static placeholder => $"{placeholder.ValueType} {placeholder.ParameterName}"), $"{last.ValueType} {from}", $"{last.ValueType} {to}"];
        var ends = goesOn ? $"both ends included, with every key at <c>{to}</c>" : "both ends included";
        code.Line($"/// <summary>The condition <c>BETWEEN {low} AND {high}</c> on <c>{last.Property}</c>, with {ends} and any field before it equal. DynamoDB rejects a lower bound above the upper one.</summary>");
        if (goesOn)
        {
            code.Line($"/// <remarks>The key goes on behind <c>{last.Property}</c>, and the upper bound lies above all of it, so this includes every key at <c>{to}</c>. <c>Range</c> ends before them.</remarks>");
        }

        code.Line(InvalidValue);
        code.Line($"public static {Condition} Between({string.Join(", ", parameters)}) => {Condition}.Between({Call(low, leading, from)}, {Call(high, leading, to)});");
        code.Blank();
    }

    // Half-open by what is stored and not by the condition, which includes both of its bounds: every key under a prefix goes on behind it, so no key is the prefix itself, and the keys at the
    // upper bound sort above it. Where a template ends on the delimiter of the field, the prefix is a whole key, and BETWEEN would take it.
    private void EmitRange(List<PlaceholderSegment> bound)
    {
        var last = bound[bound.Count - 1];
        var leading = bound.GetRange(0, bound.Count - 1);
        var from = BoundName(leading, "from");
        var to = BoundName(leading, "to");
        var parameters = (List<string>)[.. leading.Select(static placeholder => $"{placeholder.ValueType} {placeholder.ParameterName}"), $"{last.ValueType} {from}", $"{last.ValueType} {to}"];
        code.Line($"/// <summary>The condition <c>BETWEEN Prefix AND Prefix</c> on <c>{last.Property}</c>: from the first key at <c>{from}</c> up to and without the keys at <c>{to}</c>, with any field before it equal. <c>{to}</c> stays out because the key goes on behind <c>{last.Property}</c>, so no key equals the upper bound. DynamoDB rejects a lower bound above the upper one.</summary>");
        code.Line(InvalidValue);
        code.Line($"public static {Condition} Range({string.Join(", ", parameters)}) => {Condition}.Between({Call("Prefix", leading, from)}, {Call("Prefix", leading, to)});");
        code.Blank();
    }

    private void EmitOpenRange(string name, string comparison, string meaning, string side, List<PlaceholderSegment> bound)
    {
        code.Line($"/// <summary>The condition <c>{comparison} {side}</c>: the keys {meaning}, in the order of the key. It has only this one end, so what else sorts there in the partition matches too.</summary>");
        code.Line(InvalidValue);
        code.Line($"public static {Condition} {name}({Signature(bound)}) => {Condition}.{name}({side}({Arguments(bound)}));");
        code.Blank();
    }

    // Whether the bytes of the key follow the value. A fixed width does, which is what it is for. A string does at the end of the key, and before a space, which sorts below
    // everything a value may hold. Before another delimiter a value such as "a b" sorts below "a". The other variable widths sort by their text: 10 before 9, an enum by name.
    private bool IsInOrder(PlaceholderSegment placeholder)
    {
        if (placeholder.Encoding.FixedWidth > 0)
        {
            return true;
        }

        var next = template.Segments.ToList().IndexOf(placeholder) + 1;
        return placeholder.Encoding is StringEncoding
            && (next == template.Segments.Count || ((LiteralSegment)template.Segments[next]).Text[0] == ' ');
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
            CloseWriter(throws: true);
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

        if (isTableKey && template.CanReject)
        {
            EmitCheck(parameters);
        }
    }

    // Keys.Of and Keys.TryOf ask this. The text goes to the stack and away, which keeps a key without a string until it is written.
    private void EmitCheck(string parameters)
    {
        code.Blank();
        code.Line($"internal static bool Check(bool __throwOnInvalid{parameters})");
        code.Open();
        OpenWriter(Buffer, "__throwOnInvalid");
        code.Line(EncodeCall());
        code.Line($"return !{Local}.IsInvalid;");
        CloseWriter(throws: true);
        code.Close();
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
        CloseWriter(throws: signature.Length > 0);
        code.Close();
    }

    private void OpenWriter(string buffer, string? throwOnInvalid = null)
    {
        code.Line($"var {Local} = new {Writer}(stackalloc byte[{buffer}]{(throwOnInvalid is null ? string.Empty : ", " + throwOnInvalid)});");
        code.Line("try");
        code.Open();
    }

    // A writer that throws names the placeholder, and the catch adds the template, which the writer does not know. A catch costs a key that is valid nothing, where an argument of every
    // write would cost something.
    private void CloseWriter(bool throws = false)
    {
        code.Close();
        if (throws && template.CanReject)
        {
            code.Line($"catch ({FormatException} __failure)");
            code.Open();
            code.Line($"throw __failure.InTemplate({CodeText.Literal(template.Text)}, {CodeText.Literal(template.Delimiters)});");
            code.Close();
        }

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
