using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

/// <summary>Items, values and placeholders of the expression tests, written in DynamoDB JSON as a request carries them and read the way the fake reads a request.</summary>
internal static class ExpressionFixture
{
    /// <summary>Reads an item: <c>{"a":{"N":"1"}}</c>.</summary>
    /// <param name="json">The item in DynamoDB JSON.</param>
    /// <returns>The item, in its stored form.</returns>
    public static Item Item(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RequestJson.ReadItem(document.RootElement, "Item");
    }

    /// <summary>Reads one value: <c>{"N":"1"}</c>.</summary>
    /// <param name="json">The value in DynamoDB JSON.</param>
    /// <returns>The value, in its stored form.</returns>
    public static AttributeValue Value(string json) => Item($$"""{"v":{{json}}}""")["v"];

    /// <summary>Makes the placeholders of a request.</summary>
    /// <param name="values">The <c>ExpressionAttributeValues</c> in DynamoDB JSON, or <see langword="null"/>.</param>
    /// <param name="names">The <c>ExpressionAttributeNames</c> as JSON, or <see langword="null"/>.</param>
    /// <returns>The scope.</returns>
    public static PlaceholderScope Scope(string? values = null, string? names = null)
    {
        Dictionary<string, string>? nameMap = null;
        if (names is not null)
        {
            using var document = JsonDocument.Parse(names);
            nameMap = RequestJson.ReadStringMap(document.RootElement, "ExpressionAttributeNames");
        }

        return new PlaceholderScope(nameMap, values is null ? null : Item(values));
    }

    /// <summary>Makes the placeholders of a request, every value placeholder of the expression bound to one value and every name placeholder to its own name without the <c>#</c>.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">The value of every value placeholder in DynamoDB JSON.</param>
    /// <returns>The scope.</returns>
    public static PlaceholderScope ScopeFor(string expression, string value)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Item();
        var bound = Value(value);
        var tokens = ExpressionTokenizer.Tokenize(expression);
        foreach (var token in tokens)
        {
            var text = expression.Substring(token.Start, token.Length);
            if (token.Kind == ExpressionTokenKind.NamePlaceholder)
            {
                names[text] = text[1..];
            }
            else if (token.Kind == ExpressionTokenKind.ValuePlaceholder)
            {
                values[text] = bound;
            }
        }

        return new PlaceholderScope(names, values);
    }
}
