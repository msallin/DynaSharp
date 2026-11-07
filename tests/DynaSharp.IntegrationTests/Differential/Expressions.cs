using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Writes the expressions of one request: conditions, filters, update expressions, projections and key conditions, with the placeholders they use. Names go through
/// <c>#</c> placeholders, except that a name that is no reserved word is now and then written bare, and rarely a reserved word is too, which DynamoDB rejects.
/// Every value is a <c>:</c> placeholder. <see cref="WriteTo"/> adds the maps a request needs, and rarely one entry no expression uses, which DynamoDB rejects as well.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes a sequence reproducible.")]
internal sealed class Expressions(ValueSource values)
{
    private static readonly HashSet<string> Reserved = new(["status", "size", "count", "name", "data"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Comparators = ["=", "<>", "<", "<=", ">", ">="];

    private static readonly string[] TypeNames = ["S", "N", "B", "SS", "NS", "BS", "M", "L", "NULL", "BOOL"];

    private readonly Dictionary<string, string> _names = new([], StringComparer.Ordinal);
    private readonly JsonObject _values = [];

    /// <summary>Gets a name placeholder for an attribute, or the bare name.</summary>
    /// <param name="attribute">The attribute name.</param>
    /// <returns>The text that stands for it in an expression.</returns>
    public string Name(string attribute)
    {
        var bare = attribute is not "x.y" && (Reserved.Contains(attribute) ? values.Chance(0.01) : values.Chance(0.3));
        if (bare)
        {
            return attribute;
        }

        if (!_names.TryGetValue(attribute, out var placeholder))
        {
            placeholder = "#n" + _names.Count.ToString(CultureInfo.InvariantCulture);
            _names[attribute] = placeholder;
        }

        return placeholder;
    }

    /// <summary>Gets a new value placeholder.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The placeholder.</returns>
    public string Value(JsonObject value)
    {
        var placeholder = ":v" + _values.Count.ToString(CultureInfo.InvariantCulture);
        _values[placeholder] = value;
        return placeholder;
    }

    /// <summary>Draws a document path: an attribute, a map entry of <c>m</c> or <c>data</c>, or a list element of <c>l</c>. Rarely the key of the table.</summary>
    /// <param name="keys">The key attributes of the table, which a path names rarely.</param>
    /// <returns>The path.</returns>
    public string Path(IReadOnlyList<string> keys)
    {
        if (values.Rare())
        {
            return Name(values.Pick(keys));
        }

        var attribute = values.Pick(ValueSource.AttributeNames);
        var path = Name(attribute);
        if (attribute is "m" or "data" && values.Chance(0.5))
        {
            path += "." + Name(values.Pick(["k", "j", "count", "x"]));
        }
        else if (attribute == "l" && values.Chance(0.5))
        {
            path += "[" + values.Pick([0, 1, 2, 5]).ToString(CultureInfo.InvariantCulture) + "]";
        }

        return path;
    }

    /// <summary>Draws a condition over the attributes of an item.</summary>
    /// <param name="keys">The key attributes of the table.</param>
    /// <param name="depth">How many levels of AND, OR and NOT it may have.</param>
    /// <returns>The condition.</returns>
    public string Condition(IReadOnlyList<string> keys, int depth = 2)
    {
        if (depth > 0 && values.Chance(0.3))
        {
            return values.Next(0, 4) switch
            {
                0 => $"{Condition(keys, depth - 1)} AND {Condition(keys, depth - 1)}",
                1 => $"{Condition(keys, depth - 1)} OR {Condition(keys, depth - 1)}",
                2 => $"NOT {Condition(keys, depth - 1)}",
                _ => $"({Condition(keys, depth - 1)})",
            };
        }

        var path = Path(keys);
        return values.Next(0, 11) switch
        {
            0 => $"attribute_exists({path})",
            1 => $"attribute_not_exists({path})",
            2 => $"attribute_type({path}, {Value(ValueSource.Value("S", values.Rare() ? "X" : values.Pick(TypeNames)))})",
            3 => $"begins_with({path}, {Value(values.Chance(0.8) ? values.String() : values.Binary())})",
            4 => $"contains({path}, {Value(values.Chance(0.5) ? values.String() : values.Number())})",
            5 => $"size({path}) {values.Pick(Comparators)} {Value(ValueSource.Value("N", values.Pick(["0", "1", "2", "3", "5"])))}",
            6 => $"{path} BETWEEN {Value(Operand(path))} AND {Value(Operand(path))}",
            7 => $"{path} IN ({string.Join(", ", Enumerable.Range(0, values.Next(1, 4)).Select(_ => Value(Operand(path))))})",
            8 => $"{path} {values.Pick(Comparators)} {Path(keys)}",
            _ => $"{path} {values.Pick(Comparators)} {Value(Operand(path))}",
        };
    }

    /// <summary>Draws an update expression of one to four actions in up to four clauses.</summary>
    /// <param name="keys">The key attributes of the table.</param>
    /// <returns>The update expression.</returns>
    public string Update(IReadOnlyList<string> keys)
    {
        var set = new List<string>();
        var remove = new List<string>();
        var add = new List<string>();
        var delete = new List<string>();
        var actions = values.Next(1, 5);
        for (var i = 0; i < actions; i++)
        {
            switch (values.Next(0, 10))
            {
                case 0 or 1 or 2 or 3:
                    set.Add(SetAction(keys));
                    break;
                case 4 or 5:
                    remove.Add(Path(keys));
                    break;
                case 6 or 7:
                    add.Add(AddAction(keys));
                    break;
                default:
                    delete.Add($"{SetPath(keys)} {Value(values.Pick<Func<JsonObject>>([values.StringSet, values.NumberSet, values.BinarySet])())}");
                    break;
            }
        }

        var clauses = new List<string>();
        Clause(clauses, "SET", set);
        Clause(clauses, "REMOVE", remove);
        Clause(clauses, "ADD", add);
        Clause(clauses, "DELETE", delete);
        return string.Join(" ", clauses);
    }

    /// <summary>Draws a projection of one to four paths.</summary>
    /// <param name="keys">The key attributes of the table, which a projection names now and then.</param>
    /// <returns>The projection expression.</returns>
    public string Projection(IReadOnlyList<string> keys)
    {
        var paths = new List<string>();
        var count = values.Next(1, 5);
        for (var i = 0; i < count; i++)
        {
            paths.Add(values.Chance(0.25) ? Name(values.Pick(keys)) : Path(keys));
        }

        return string.Join(", ", paths);
    }

    /// <summary>Adds the placeholder maps to a request, and rarely an entry no expression uses: a value only where the request has values, since a read has no such member.</summary>
    /// <param name="request">The request.</param>
    public void WriteTo(JsonObject request)
    {
        if (values.Rare())
        {
            if (_values.Count == 0 || values.Chance(0.5))
            {
                _names["unused"] = "#unused";
            }
            else
            {
                _values[":unused"] = ValueSource.Value("S", "x");
            }
        }

        if (_names.Count > 0)
        {
            var names = new JsonObject();
            foreach (var (attribute, placeholder) in _names)
            {
                names[placeholder] = attribute;
            }

            request["ExpressionAttributeNames"] = names;
        }

        if (_values.Count > 0)
        {
            request["ExpressionAttributeValues"] = _values.DeepClone();
        }
    }

    private static void Clause(List<string> clauses, string keyword, List<string> actions)
    {
        if (actions.Count > 0)
        {
            clauses.Add(keyword + " " + string.Join(", ", actions));
        }
    }

    // An operand of the usual type of the attribute under the path, so that comparisons are not all false.
    private JsonObject Operand(string path)
    {
        return AttributeOf(path) switch
        {
            "n" or "count" or "size" or "gsk" => values.Number(),
            "b" => values.Binary(),
            "sk" => values.SortKeyValue(),
            _ => values.Chance(0.8) ? values.String() : values.Any(0),
        };
    }

    // The attribute a path starts with: the name behind its first placeholder, or its first bare name.
    private string AttributeOf(string path)
    {
        var first = path.Split('.', '[')[0];
        return _names.FirstOrDefault(pair => pair.Value == first).Key ?? first;
    }

    private string SetAction(IReadOnlyList<string> keys)
    {
        var path = Path(keys);
        return values.Next(0, 8) switch
        {
            0 => $"{path} = {Path(keys)}",
            1 => $"{path} = {Path(keys)} + {Value(values.Number())}",
            2 => $"{path} = {Path(keys)} - {Value(values.Number())}",
            3 => $"{path} = if_not_exists({Path(keys)}, {Value(values.Any(1))})",
            4 => $"{path} = list_append({Path(keys)}, {Value(values.List(1))})",
            5 => $"{path} = list_append({Value(values.List(1))}, {Path(keys)})",
            _ => $"{path} = {Value(values.ValueFor(AttributeOf(path)))}",
        };
    }

    private string AddAction(IReadOnlyList<string> keys)
    {
        var path = values.Chance(0.5) ? Name(values.Pick(["n", "count"])) : SetPath(keys);
        var operand = values.Next(0, 5) switch
        {
            0 or 1 => values.Number(),
            2 => values.StringSet(),
            3 => values.NumberSet(),
            _ => values.Any(0),
        };
        return $"{path} {Value(operand)}";
    }

    private string SetPath(IReadOnlyList<string> keys) => values.Rare() ? Path(keys) : Name(values.Pick(["ss", "ns", "bs"]));
}
