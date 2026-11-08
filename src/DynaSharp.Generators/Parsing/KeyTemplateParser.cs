using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// Turns the text of a key template into nodes. It is pure, so every error position is tested over text alone.
/// </summary>
/// <remarks>
/// <c>template := (literal | "{" Property [":" format] "}")+</c>, with <c>{{</c> and <c>}}</c> for literal braces. A format ends at the
/// first <c>}</c> and may contain colons, which <c>yyyy-MM-ddTHH:mm</c> needs.
/// <code>
/// Input:  ORDER#{CreatedAt:yyyy-MM-ddTHH:mm}#{{x}}
/// Output: Literal "ORDER#", Placeholder CreatedAt with format yyyy-MM-ddTHH:mm, Literal "#{x}"
/// </code>
/// </remarks>
internal static class KeyTemplateParser
{
    private const char FirstPrintable = ' ';

    private const char LastPrintable = '~';

    public static TemplateParse Parse(string text)
    {
        if (text.Length == 0)
        {
            return Failure(0, "the template is empty");
        }

        var nodes = new List<TemplateNode>();
        var literal = new StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            var character = text[position];
            if (IsDoubled(text, position))
            {
                literal.Append(character);
                position += 2;
            }
            else if (character == '{')
            {
                FlushLiteral(literal, nodes);
                if (ReadPlaceholder(text, position + 1, out var placeholder, out position) is { } error)
                {
                    return new TemplateParse(nodes, error);
                }

                nodes.Add(placeholder!);
            }
            else if (character == '}')
            {
                return Failure(position, "a '}' outside a placeholder must be written '}}'");
            }
            else if (character is < FirstPrintable or > LastPrintable)
            {
                return new TemplateParse(nodes, new TemplateError(position, "a literal must be printable ASCII", IsLiteral: true));
            }
            else
            {
                literal.Append(character);
                position++;
            }
        }

        FlushLiteral(literal, nodes);
        return new TemplateParse(nodes, null);
    }

    /// <summary>
    /// Where the placeholder of <paramref name="property"/> stands, from its <c>{</c> to its <c>}</c>. The text may be the C# literal of a template, which writes a placeholder as it is:
    /// <c>"A#{Id:d3}"</c> with <c>Id</c> gives 3 and 7.
    /// </summary>
    /// <param name="text">The template, or a literal of it.</param>
    /// <param name="property">The property the placeholder names.</param>
    /// <returns>The start and the length, or null when no placeholder names it.</returns>
    public static (int Start, int Length)? FindPlaceholder(string text, string property)
    {
        for (var position = 0; position < text.Length; position++)
        {
            var end = position + 1 + property.Length;
            if (IsDoubled(text, position))
            {
                position++;
            }
            else if (text[position] == '{' && end < text.Length && string.CompareOrdinal(text, position + 1, property, 0, property.Length) == 0 && text[end] is ':' or '}')
            {
                return (position, text.IndexOf('}', end) - position + 1);
            }
        }

        return null;
    }

    private static bool IsDoubled(string text, int position) =>
        text[position] is '{' or '}' && position + 1 < text.Length && text[position + 1] == text[position];

    private static void FlushLiteral(StringBuilder literal, List<TemplateNode> nodes)
    {
        if (literal.Length > 0)
        {
            nodes.Add(new LiteralNode(literal.ToString()));
            literal.Clear();
        }
    }

    // Reads from just after the opening brace. On success next is just after the closing brace.
    private static TemplateError? ReadPlaceholder(string text, int start, out PlaceholderNode? placeholder, out int next)
    {
        placeholder = null;
        next = start;
        var end = start;
        while (end < text.Length && IsNameCharacter(text[end], isFirst: end == start))
        {
            end++;
        }

        if (end == text.Length)
        {
            return new TemplateError(end, "the placeholder is not closed", IsLiteral: false);
        }

        var property = text.Substring(start, end - start);
        if (property.Length == 0)
        {
            return new TemplateError(end, text[end] == '}' ? "a placeholder needs a property name" : $"'{text[end]}' cannot start a property name", IsLiteral: false);
        }

        if (text[end] == '}')
        {
            placeholder = new PlaceholderNode(property, null);
            next = end + 1;
            return null;
        }

        if (text[end] != ':')
        {
            return new TemplateError(end, $"'{text[end]}' cannot be part of a property name", IsLiteral: false);
        }

        var formatStart = end + 1;
        var close = text.IndexOf('}', formatStart);
        if (close < 0)
        {
            return new TemplateError(text.Length, "the placeholder is not closed", IsLiteral: false);
        }

        if (close == formatStart)
        {
            return new TemplateError(formatStart, "a format cannot be empty", IsLiteral: false);
        }

        placeholder = new PlaceholderNode(property, text.Substring(formatStart, close - formatStart));
        next = close + 1;
        return null;
    }

    private static bool IsNameCharacter(char character, bool isFirst) =>
        character == '_' || (isFirst ? char.IsLetter(character) : char.IsLetterOrDigit(character));

    private static TemplateParse Failure(int position, string message) => new([], new TemplateError(position, message, IsLiteral: false));
}
