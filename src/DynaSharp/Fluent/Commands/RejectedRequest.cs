using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Explains a request that DynamoDB rejected as invalid. With every name a placeholder its messages say things like "attribute value :v3 is not
/// defined", so the exception gets what the placeholders stand for, read from the body as it was sent. Values stay out, only their types
/// go in, and the exception goes nowhere near telemetry, which reads its error code alone.
/// </summary>
/// <remarks>
/// The expressions of most requests are members of the request. In a transaction each action has its own, and its own <c>#n0</c>, and a batch get has them per table,
/// so there the entries are named with a prefix: <c>TransactItems[2].ConditionExpression</c>, <c>TransactItems[2].#n0</c>.
/// </remarks>
internal static class RejectedRequest
{
    private static readonly string[] ExpressionMembers =
        ["KeyConditionExpression", "ConditionExpression", "UpdateExpression", "FilterExpression", "ProjectionExpression"];

    /// <summary>Attaches the expressions of a body to the exception. A request without expressions leaves it as it is.</summary>
    /// <param name="rejection">The failure.</param>
    /// <param name="body">The request body, which this library wrote.</param>
    public static void Describe(ValidationException rejection, ArraySegment<byte> body)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = WireJson.ReaderOptions.MaxDepth });
        var root = document.RootElement;
        var found = new Found();
        found.Read(root, string.Empty);
        if (root.TryGetProperty("TransactItems", out var actions))
        {
            ReadActions(actions, found);
        }

        if (root.TryGetProperty("RequestItems", out var tables))
        {
            ReadTables(tables, found);
        }

        if (found.Expressions.Count > 0)
        {
            rejection.Describe(found.Expressions, found.Names, found.ValueTypes, found.Explain());
        }
    }

    // [{"Update":{"TableName":"Shop","UpdateExpression":"SET #n0 = :v0",...}}]: each action has one member, which is named after its kind.
    private static void ReadActions(JsonElement actions, Found found)
    {
        var index = 0;
        foreach (var action in actions.EnumerateArray())
        {
            foreach (var kind in action.EnumerateObject())
            {
                found.Read(kind.Value, string.Create(CultureInfo.InvariantCulture, $"TransactItems[{index}]."));
            }

            index++;
        }
    }

    // {"Shop":{"Keys":[...],"ProjectionExpression":"#n0"}}. The writes of a batch are an array and have no expressions.
    private static void ReadTables(JsonElement tables, Found found)
    {
        foreach (var table in tables.EnumerateObject())
        {
            if (table.Value.ValueKind == JsonValueKind.Object)
            {
                found.Read(table.Value, $"RequestItems.{table.Name}.");
            }
        }
    }

    [SuppressMessage("Style", "IDE0028:Simplify collection initialization", Justification = "The compiler in Stryker cannot parse collection expression arguments.")]
    private sealed class Found
    {
        private readonly List<string> _texts = [];

        public Dictionary<string, string> Expressions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ValueTypes { get; } = new(StringComparer.Ordinal);

        public void Read(JsonElement owner, string prefix)
        {
            foreach (var member in ExpressionMembers)
            {
                if (owner.TryGetProperty(member, out var expression))
                {
                    var text = expression.GetString()!;
                    Expressions[prefix + member] = text;
                    _texts.Add(prefix.Length == 0 ? text : $"{prefix.TrimEnd('.')}: {text}");
                }
            }

            if (owner.TryGetProperty("ExpressionAttributeNames", out var names))
            {
                foreach (var name in names.EnumerateObject())
                {
                    Names[prefix + name.Name] = name.Value.GetString()!;
                }
            }

            // {":v0":{"S":"secret"}} gives :v0 with S. The value itself is not looked at.
            if (owner.TryGetProperty("ExpressionAttributeValues", out var values))
            {
                foreach (var value in values.EnumerateObject())
                {
                    foreach (var descriptor in value.Value.EnumerateObject())
                    {
                        ValueTypes[prefix + value.Name] = descriptor.Name;
                    }
                }
            }
        }

        // Expression: #n0 = :v0 AND #n1 > :v1, where #n0 = status, #n1 = total, :v0 (S), :v1 (N)
        // Expression: TransactItems[1]: #n0 = :v0, where TransactItems[1].#n0 = status, TransactItems[1].:v0 (S)
        public string Explain()
        {
            var line = new StringBuilder("Expression: ").AppendJoin("; ", _texts);
            var separator = ", where ";
            foreach (var (placeholder, name) in Names)
            {
                line.Append(separator).Append(placeholder).Append(" = ").Append(name);
                separator = ", ";
            }

            foreach (var (placeholder, type) in ValueTypes)
            {
                line.Append(separator).Append(placeholder).Append(" (").Append(type).Append(')');
                separator = ", ";
            }

            return line.ToString();
        }
    }
}
