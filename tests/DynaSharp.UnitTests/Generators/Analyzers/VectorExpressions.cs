using System.Text.Json;
using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The expressions in <c>operation_vectors.json</c>, which come from botocore and from the commands.</summary>
internal static class VectorExpressions
{
    public static IReadOnlyList<(ExpressionKind Kind, string Text)> All()
    {
        using var stream = typeof(VectorExpressions).Assembly.GetManifestResourceStream("operation_vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        var found = new HashSet<(ExpressionKind Kind, string Text)>();
        Walk(document.RootElement, found);
        return [.. found];
    }

    private static void Walk(JsonElement element, HashSet<(ExpressionKind Kind, string Text)> found)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, found);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    Walk(property.Value, found);
                }
                else if (property.Name == "UpdateExpression")
                {
                    found.Add((ExpressionKind.Update, property.Value.GetString()!));
                }
                else if (property.Name is "ConditionExpression" or "FilterExpression" or "KeyConditionExpression")
                {
                    found.Add((ExpressionKind.Condition, property.Value.GetString()!));
                }
                else if (property.Name == "ProjectionExpression")
                {
                    // The grammar has no projection of its own: each path of it must read as a path of a condition.
                    foreach (var path in property.Value.GetString()!.Split(", "))
                    {
                        found.Add((ExpressionKind.Condition, path + " = :v"));
                    }
                }
            }
        }
    }
}
