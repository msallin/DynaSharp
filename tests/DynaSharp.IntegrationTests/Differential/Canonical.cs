using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// The canonical form of DynamoDB JSON, in which two answers that DynamoDB considers equal are equal text: object members sorted by name, because JSON member order means
/// nothing, and the elements of <c>SS</c>, <c>NS</c> and <c>BS</c> sorted, because a set has no order. Numbers stay as the service wrote them: their form is compared.
/// </summary>
internal static class Canonical
{
    private const int ShownStringLength = 120;

    /// <summary>Gets the canonical text of a node.</summary>
    /// <param name="node">The node, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public static string Of(JsonNode? node) => Normalize(node)?.ToJsonString() ?? "null";

    /// <summary>Gets the canonical form of a node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>A new node in canonical form.</returns>
    public static JsonNode? Normalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject value:
                var result = new JsonObject();
                foreach (var (name, member) in value.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    var normalized = Normalize(member);
                    if (name is "SS" or "NS" or "BS" && normalized is JsonArray set && value.Count == 1)
                    {
                        normalized = new JsonArray([.. set.Select(static element => element!.ToJsonString()).Order(StringComparer.Ordinal).Select(static text => JsonNode.Parse(text))]);
                    }

                    result[name] = normalized;
                }

                return result;
            case JsonArray array:
                return new JsonArray([.. array.Select(Normalize)]);

            // A JSON number of the protocol, such as CapacityUnits, is a value: 2 and 2.0 are equal. The numbers of items are strings, and keep their form.
            case JsonValue number when number.GetValueKind() == System.Text.Json.JsonValueKind.Number:
                return JsonValue.Create(double.Parse(number.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture));
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>Gets a node for a report: long strings cut, so that a report of a 400 KB item stays readable.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text.</returns>
    public static string Shown(JsonNode? node) => Cut(node)?.ToJsonString() ?? "null";

    private static JsonNode? Cut(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.Select(static pair => KeyValuePair.Create(pair.Key, Cut(pair.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Cut)]),
        JsonValue value when value.TryGetValue<string>(out var text) && text.Length > ShownStringLength =>
            $"{text[..20]}...({text.Length} characters)",
        null => null,
        _ => node.DeepClone(),
    };
}
