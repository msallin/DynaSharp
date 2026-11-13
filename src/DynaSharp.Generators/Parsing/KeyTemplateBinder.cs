using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Binds parsed nodes to properties: resolves formats to encodings, computes the delimiters and the buffer length, and checks the rules
/// that span several placeholders.
/// </summary>
internal static class KeyTemplateBinder
{
    private const int VariableBufferLength = 256;

    private const int MaxSpellings = 3;

    public static KeyBinding Bind(
        string text,
        IReadOnlyList<TemplateNode> nodes,
        IReadOnlyDictionary<string, TemplateInput> inputs,
        KeyRole role)
    {
        var segments = new List<TemplateSegment>();
        var problems = new List<KeyProblem>();
        var used = new HashSet<string>();
        var fallbacks = new HashSet<string>();
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is LiteralNode literal)
            {
                segments.Add(new LiteralSegment(literal.Text));
            }
            else if (BindPlaceholder((PlaceholderNode)nodes[index], index + 1 == nodes.Count, inputs, role, used, fallbacks, problems) is { } placeholder)
            {
                segments.Add(placeholder);
            }
        }

        CheckNeighbours(segments, role, fallbacks, problems);
        var delimiters = Delimiters(segments);
        CheckStoredNames(segments, delimiters, problems);
        var template = new KeyTemplate(text, new EquatableArray<TemplateSegment>([.. segments]), delimiters, BufferLength(segments));
        return new KeyBinding(template, problems, fallbacks);
    }

    private static PlaceholderSegment? BindPlaceholder(
        PlaceholderNode node,
        bool endsTemplate,
        IReadOnlyDictionary<string, TemplateInput> inputs,
        KeyRole role,
        HashSet<string> used,
        HashSet<string> fallbacks,
        List<KeyProblem> problems)
    {
        if (!inputs.TryGetValue(node.Property, out var input))
        {
            var spellings = Spelling.Closest(node.Property, inputs.Keys, Spelling.TypoDistance(node.Property)).Take(MaxSpellings).Select(name => (name, node.Format));
            problems.Add(new KeyProblem(Descriptors.UnknownProperty, [node.Property], Fix: KeyProblem.Rewrite(node.Property, spellings)));
            return null;
        }

        if (!used.Add(node.Property))
        {
            problems.Add(new KeyProblem(Descriptors.RepeatedPlaceholder, [node.Property]));
            return null;
        }

        if (!input.IsAccessible)
        {
            problems.Add(new KeyProblem(Descriptors.InaccessibleType, [input.TypeName, input.Property]));
        }

        if (role.IsIndex && input is { IsComputed: true, HasDependencies: false })
        {
            problems.Add(new KeyProblem(Descriptors.ComputedInIndex, [input.Property]));
        }

        if (input.IsNullable && !role.IsSparse)
        {
            problems.Add(new KeyProblem(Descriptors.NullableInput, [input.Property], Fix: input.Property));
        }

        var binding = input.Type.Bind(node.Format, input.Property);
        if (binding.Problem is { } misfit && misfit.Descriptor == Descriptors.FormatDoesNotFit)
        {
            binding = binding with { Problem = misfit with { Fix = KeyProblem.Rewrite(input.Property, Fitting(input, endsTemplate)) } };
        }

        // A format that does not fit is a slip in one placeholder: the default encoding of the type keeps the model, so the error is not buried under one per use of the entity.
        if (binding.Problem is { } wrongFormat
            && (wrongFormat.Descriptor == Descriptors.FormatDoesNotFit || wrongFormat.Descriptor == Descriptors.FormatOutOfRange)
            && input.Type.Bind(null, input.Property) is { Encoding: not null } fallback)
        {
            problems.Add(wrongFormat with { KeepsModel = true });
            fallbacks.Add(input.Property);
            binding = fallback;
        }

        if (binding.Encoding is null)
        {
            problems.Add(binding.Problem!);
            return null;
        }

        // Parsing takes what is left of the text for a raw value, so it round-trips only with nothing behind it.
        if (binding.Encoding is StringEncoding { IsRaw: true } && !endsTemplate)
        {
            problems.Add(new KeyProblem(Descriptors.RawNotLast, [input.Property]));
            return null;
        }

        if (input.Converter is { } converter && binding.Encoding.MirrorsAttribute && !binding.Encoding.HasTextOf(converter))
        {
            problems.Add(new KeyProblem(Descriptors.ConverterIgnoredInKey, [input.Property, .. IgnoredConverter(input, binding.Encoding, converter)], KeepsModel: true));
        }

        if (role.IsSortKey && node.Format is null && binding.Encoding is PlainIntegerEncoding or PlainDecimalEncoding)
        {
            string[] formats = binding.Encoding is PlainIntegerEncoding { Type: var integer } ? ["d" + integer.MaxDigits, "n" + integer.MaxDigits, "plain"] : ["plain"];
            problems.Add(new KeyProblem(Descriptors.NumberWithoutFixedFormat, [input.Property], Fix: KeyProblem.Rewrite(input.Property, formats.Select(format => (input.Property, (string?)format)))));
        }

        return new PlaceholderSegment(input.Property, input.ParameterName, input.ValueType, binding.Encoding, input.IsNullable || input.OmitsEmpty);
    }

    // A template writes its own text, and a converter that writes another one leaves the key and the attribute apart. An enum gets its other names where the key reads them too.
    private static string[] IgnoredConverter(TemplateInput input, KeyEncoding encoding, string converter) =>
        encoding is EnumNameEncoding
            ? ["the member name", converter.Replace("global::", string.Empty), "Remove the converter and give the members their stored names with [DynamoDbEnumName], or use d<w> for the number"]
            : ["the value as it is", converter.Replace("global::", string.Empty), $"Write the text in a computed string property and use that in the template, or make '{input.Property}' the whole key, which goes through the converter"];

    // The formats of the type that hold every value here: raw only where the placeholder ends the template.
    private static IEnumerable<(string Property, string? Format)> Fitting(TemplateInput input, bool endsTemplate) =>
        from format in input.Type.Fits
        let encoding = input.Type.Bind(format, input.Property).Encoding
        where encoding is not null && (endsTemplate || encoding is not StringEncoding { IsRaw: true })
        select (input.Property, format);

    // A placeholder that fell back to its default has an error already, and the width of the default says nothing about the one the format meant.
    private static void CheckNeighbours(List<TemplateSegment> segments, KeyRole role, HashSet<string> fallbacks, List<KeyProblem> problems)
    {
        for (var index = 0; index + 1 < segments.Count; index++)
        {
            if (segments[index] is not PlaceholderSegment { Encoding.FixedWidth: 0 } placeholder || fallbacks.Contains(placeholder.Property))
            {
                continue;
            }

            if (segments[index + 1] is PlaceholderSegment next)
            {
                problems.Add(new KeyProblem(Descriptors.NoLiteralBetween, [placeholder.Property, next.Property]));
            }
            else if (role.IsSortKey && placeholder.Encoding is StringEncoding && ((LiteralSegment)segments[index + 1]).Text[0] is var delimiter && delimiter > ' ')
            {
                // Values reject everything below the space, so a space delimiter has nothing that sorts below it.
                problems.Add(new KeyProblem(Descriptors.StringBeforeDelimiter, [placeholder.Property, delimiter.ToString()]));
            }
        }
    }

    // A stored name of an enum that KeyWriter turns away fails every key of its member, which the names, known here with the delimiters, show before a write does.
    private static void CheckStoredNames(List<TemplateSegment> segments, string delimiters, List<KeyProblem> problems)
    {
        foreach (var segment in segments)
        {
            if (segment is not PlaceholderSegment { Encoding: EnumNameEncoding encoding } placeholder)
            {
                continue;
            }

            foreach (var name in encoding.StoredNames)
            {
                if (WhyNotInKey(name, delimiters) is { } reason)
                {
                    problems.Add(new KeyProblem(Descriptors.EnumStoredNameNotInKey, [placeholder.Property, name, encoding.EnumType.Replace("global::", string.Empty), reason], KeepsModel: true));
                }
            }
        }
    }

    // What KeyText.Inspect refuses of a value, in its order: an empty one, one that is no well-formed text, a control character, a delimiter, and U+10FFFF.
    private static string? WhyNotInKey(string name, string delimiters)
    {
        if (name.Length == 0)
        {
            return "is empty";
        }

        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsSurrogatePair(name, index))
            {
                index++;
            }
            else if (char.IsSurrogate(name[index]))
            {
                return "is not well-formed text";
            }
        }

        if (name.Any(static character => character < ' '))
        {
            return "holds a control character";
        }

        var delimiter = name.IndexOfAny(delimiters.ToCharArray());
        if (delimiter >= 0)
        {
            return $"holds '{name[delimiter]}', a delimiter of the template";
        }

        return name.Contains("\uDBFF\uDFFF") ? "holds U+10FFFF" : null;
    }

    // USER#{Id}-{Name}.csv gives "#-.": every character next to a placeholder, each once.
    private static string Delimiters(List<TemplateSegment> segments)
    {
        var delimiters = new StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index] is LiteralSegment literal)
            {
                if (index + 1 < segments.Count)
                {
                    AddOnce(delimiters, literal.Text[literal.Text.Length - 1]);
                }

                if (index > 0)
                {
                    AddOnce(delimiters, literal.Text[0]);
                }
            }
        }

        return delimiters.ToString();
    }

    private static void AddOnce(StringBuilder delimiters, char delimiter)
    {
        if (delimiters.ToString().IndexOf(delimiter) < 0)
        {
            delimiters.Append(delimiter);
        }
    }

    private static int BufferLength(List<TemplateSegment> segments)
    {
        var length = 0;
        foreach (var segment in segments)
        {
            if (segment is LiteralSegment literal)
            {
                length += literal.Text.Length;
            }
            else if (((PlaceholderSegment)segment).Encoding.FixedWidth is var width && width > 0)
            {
                length += width;
            }
            else
            {
                return VariableBufferLength;
            }
        }

        return length;
    }
}
