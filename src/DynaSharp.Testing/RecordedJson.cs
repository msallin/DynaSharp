using System.Text;
using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Testing;

/// <summary>
/// What a <see cref="RecordedRequest"/> and a <see cref="RecordedTransactStep"/> read alike, from the object that holds the members: the body of a request, or the action of a step. The notation of
/// <see cref="ResolveExpression"/> is the one <see cref="RecordedRequest"/> documents.
/// </summary>
internal static class RecordedJson
{
    /// <summary>Reads a member that has the form of an item the way a response that carries it is read.</summary>
    /// <typeparam name="T">The type to read.</typeparam>
    /// <param name="container">The object that holds the member.</param>
    /// <param name="member">The name of the member.</param>
    /// <param name="owner">What the object is, as a message names it.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The object has no such member.</exception>
    public static T ReadDocument<T>(JsonElement container, string member, string owner)
        where T : IDynamoDbDocument<T>
    {
        if (!container.TryGetProperty(member, out var value))
        {
            throw new InvalidOperationException($"The {owner} has no member {member}.");
        }

        var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(value.GetRawText()));
        reader.ReadStartItem();
        return T.ReadAttributes(ref reader);
    }

    /// <summary>Gets an expression with its placeholders resolved from the maps next to it. A placeholder the maps lack stays as it is, which shows the mistake.</summary>
    /// <param name="container">The object that holds the expression and its maps.</param>
    /// <param name="member">The name of the expression, for example <c>ConditionExpression</c>.</param>
    /// <returns>The text, or <see langword="null"/> if there is no such expression.</returns>
    public static string? ResolveExpression(JsonElement container, string member)
    {
        if (!container.TryGetProperty(member, out var expression) || expression.GetString() is not { } source)
        {
            return null;
        }

        container.TryGetProperty("ExpressionAttributeNames", out var names);
        container.TryGetProperty("ExpressionAttributeValues", out var values);

        // One pass that reads each placeholder to its end: it never mistakes :v1 for the start of :v10, and it does not read again what it put in, so a value that looks like a placeholder stays a value.
        // Input:  #n0.#n1[2] = :v10 AND #n2 > :v1, with #n0 order, #n1 lines, #n2 total, :v1 {"N":"1"}, :v10 {"S":"x"}
        // Output: order.lines[2] = "x" AND total > 1
        var text = new StringBuilder(source.Length);
        var position = 0;
        while (position < source.Length)
        {
            if (source[position] is not ('#' or ':'))
            {
                text.Append(source[position]);
                position++;
                continue;
            }

            var end = position + 1;
            while (end < source.Length && (char.IsAsciiLetterOrDigit(source[end]) || source[end] == '_'))
            {
                end++;
            }

            var placeholder = source[position..end];
            if (source[position] == '#' && TryFind(names, placeholder, out var name))
            {
                text.Append(DisplayName(name.GetString()!));
            }
            else if (source[position] == ':' && TryFind(values, placeholder, out var value))
            {
                AppendValue(text, value);
            }
            else
            {
                text.Append(placeholder);
            }

            position = end;
        }

        return text.ToString();
    }

    // A map that is absent comes as a default element, which has no members to ask for.
    private static bool TryFind(JsonElement map, string placeholder, out JsonElement found)
    {
        found = default;
        return map.ValueKind == JsonValueKind.Object && map.TryGetProperty(placeholder, out found);
    }

    // A dot or a bracket in a name is legal, and without the backticks one attribute would read as a path through two.
    private static string DisplayName(string name) => name.AsSpan().ContainsAny(".[]") ? $"`{name}`" : name;

    // A value on the wire is an object of one member, whose name is the type: {"S":"a"}.
    private static void AppendValue(StringBuilder text, JsonElement value)
    {
        foreach (var member in value.EnumerateObject())
        {
            AppendTyped(text, member.Name, member.Value);
        }
    }

    // Comparisons and not a switch on the type: a switch with this many cases compiles to branches that tests cannot all reach.
    private static void AppendTyped(StringBuilder text, string type, JsonElement content)
    {
        if (type == "N")
        {
            text.Append(content.GetString());
        }
        else if (type == "B")
        {
            text.Append('b').Append(content.GetRawText());
        }
        else if (type == "NULL")
        {
            text.Append("null");
        }
        else if (type == "L")
        {
            AppendElements(text, "[", content, "]", null);
        }
        else if (type == "M")
        {
            AppendMap(text, content);
        }
        else if (type.Length == 2 && type[1] == 'S')
        {
            // {"SS":["a"]}, {"NS":["1"]} and {"BS":["aA=="]}: the first letter is the type of the elements, which on the wire are bare.
            AppendElements(text, "<<", content, ">>", type[..1]);
        }
        else
        {
            // A string with its quotes and escapes, true or false, and as it stands a type this library does not know.
            text.Append(content.GetRawText());
        }
    }

    private static void AppendElements(StringBuilder text, string open, JsonElement elements, string close, string? elementType)
    {
        text.Append(open);
        var separator = string.Empty;
        foreach (var element in elements.EnumerateArray())
        {
            text.Append(separator);
            if (elementType is null)
            {
                AppendValue(text, element);
            }
            else
            {
                AppendTyped(text, elementType, element);
            }

            separator = ", ";
        }

        text.Append(close);
    }

    private static void AppendMap(StringBuilder text, JsonElement map)
    {
        text.Append('{');
        var separator = string.Empty;
        foreach (var entry in map.EnumerateObject())
        {
            text.Append(separator).Append('"').Append(JsonEncodedText.Encode(entry.Name, WireJson.WriterOptions.Encoder)).Append("\": ");
            AppendValue(text, entry.Value);
            separator = ", ";
        }

        text.Append('}');
    }
}
