using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Binds parsed nodes to properties: resolves formats to encodings, computes the delimiters and the buffer length, and checks the rules
/// that span several placeholders.
/// </summary>
internal static class KeyTemplateBinder
{
    private const int VariableBufferLength = 256;

    public static KeyBinding Bind(
        string text,
        IReadOnlyList<TemplateNode> nodes,
        IReadOnlyDictionary<string, TemplateInput> inputs,
        KeyRole role)
    {
        var segments = new List<TemplateSegment>();
        var problems = new List<KeyProblem>();
        var used = new HashSet<string>();
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is LiteralNode literal)
            {
                segments.Add(new LiteralSegment(literal.Text));
            }
            else if (BindPlaceholder((PlaceholderNode)nodes[index], index + 1 == nodes.Count, inputs, role, used, problems) is { } placeholder)
            {
                segments.Add(placeholder);
            }
        }

        CheckNeighbours(segments, role, problems);
        var template = new KeyTemplate(text, new EquatableArray<TemplateSegment>([.. segments]), Delimiters(segments), BufferLength(segments));
        return new KeyBinding(template, problems);
    }

    private static PlaceholderSegment? BindPlaceholder(
        PlaceholderNode node,
        bool endsTemplate,
        IReadOnlyDictionary<string, TemplateInput> inputs,
        KeyRole role,
        HashSet<string> used,
        List<KeyProblem> problems)
    {
        if (!inputs.TryGetValue(node.Property, out var input))
        {
            problems.Add(new KeyProblem(Descriptors.UnknownProperty, [node.Property]));
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
            problems.Add(new KeyProblem(Descriptors.NullableInput, [input.Property]));
        }

        var binding = input.Type.Bind(node.Format, input.Property);
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

        if (role.IsSortKey && node.Format is null && binding.Encoding is PlainIntegerEncoding or PlainDecimalEncoding)
        {
            problems.Add(new KeyProblem(Descriptors.NumberWithoutFixedFormat, [input.Property]));
        }

        return new PlaceholderSegment(input.Property, input.ParameterName, input.ValueType, binding.Encoding, input.IsNullable);
    }

    private static void CheckNeighbours(List<TemplateSegment> segments, KeyRole role, List<KeyProblem> problems)
    {
        for (var index = 0; index + 1 < segments.Count; index++)
        {
            if (segments[index] is not PlaceholderSegment { Encoding.FixedWidth: 0 } placeholder)
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
