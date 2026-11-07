using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Generates a random sequence of data plane operations from a seed: the same seed gives the same schema and the same requests. Most requests are valid; edge values and
/// a few invalid ones come from <see cref="ValueSource"/> and <see cref="Expressions"/>. Every sequence ends with a strongly consistent scan of both tables, which compares
/// what the operations left behind.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes a sequence reproducible.")]
internal sealed class SequenceGenerator
{
    private static readonly string[] MainKeys = ["pk", "sk"];
    private static readonly string[] OtherKeys = ["id"];
    private static readonly string[] IndexKeys = ["gpk", "gsk"];
    private static readonly string[] Comparators = ["=", "<", "<=", ">", ">="];

    private readonly Random _random;
    private readonly ValueSource _values;
    private readonly int _seed;
    private int _tokens;

    private SequenceGenerator(int seed)
    {
        _seed = seed;
        _random = new Random(seed);
        Schema = Schema.Pick(_random);
        _values = new ValueSource(_random, Schema);
    }

    /// <summary>Gets the schema of the sequence.</summary>
    public Schema Schema { get; }

    /// <summary>Generates the sequence of a seed.</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="length">The most operations before the final scans.</param>
    /// <returns>The schema and the operations.</returns>
    public static (Schema Schema, List<Operation> Operations) Generate(int seed, int length)
    {
        var generator = new SequenceGenerator(seed);
        var operations = new List<Operation>();
        var count = generator._random.Next(Math.Min(5, length), length + 1);
        for (var i = 0; i < count; i++)
        {
            generator.Next(operations);
        }

        operations.Add(FinalScan(Schema.Main));
        operations.Add(FinalScan(Schema.Other));
        return (generator.Schema, operations);
    }

    private static Operation FinalScan(string table) =>
        new("Scan", new JsonObject { ["TableName"] = table, ["ConsistentRead"] = true }, Reading.Unordered);

    private static JsonObject KeyOf(JsonObject item, bool main)
    {
        var key = new JsonObject();
        foreach (var name in main ? MainKeys : OtherKeys)
        {
            if (item[name] is { } value)
            {
                key[name] = value.DeepClone();
            }
        }

        return key;
    }

    private static JsonObject ToObject(Dictionary<string, JsonArray> items)
    {
        var result = new JsonObject();
        foreach (var (table, writes) in items)
        {
            result[table] = writes;
        }

        return result;
    }

    private void Next(List<Operation> operations)
    {
        var roll = _random.Next(100);
        switch (roll)
        {
            case < 22:
                operations.Add(PutItem());
                break;
            case < 42:
                operations.Add(UpdateItem());
                break;
            case < 50:
                operations.Add(DeleteItem());
                break;
            case < 59:
                operations.Add(GetItem());
                break;
            case < 71:
                operations.Add(Query());
                break;
            case < 77:
                operations.Add(Scan());
                break;
            case < 83:
                operations.Add(BatchWriteItem());
                break;
            case < 88:
                operations.Add(BatchGetItem());
                break;
            case < 97:
                TransactWriteItems(operations);
                break;
            default:
                operations.Add(TransactGetItems());
                break;
        }
    }

    private Operation PutItem()
    {
        var main = _values.Chance(0.85);
        var request = new JsonObject { ["TableName"] = main ? Schema.Main : Schema.Other, ["Item"] = main ? _values.MainItem() : _values.OtherItem() };
        var expressions = new Expressions(_values);
        if (_values.Chance(0.35))
        {
            request["ConditionExpression"] = expressions.Condition(main ? MainKeys : OtherKeys);
            ConditionFailureReturn(request);
        }

        if (_values.Chance(0.5))
        {
            request["ReturnValues"] = _values.Rare() ? _values.Pick(["ALL_NEW", "UPDATED_OLD", "UPDATED_NEW"]) : _values.Pick(["NONE", "ALL_OLD"]);
        }

        Capacity(request);
        expressions.WriteTo(request);
        return new Operation("PutItem", request);
    }

    private Operation UpdateItem()
    {
        var main = _values.Chance(0.85);
        var keys = main ? MainKeys : OtherKeys;
        var request = new JsonObject { ["TableName"] = main ? Schema.Main : Schema.Other, ["Key"] = main ? _values.MainKey() : _values.OtherKey() };
        var expressions = new Expressions(_values);

        // Without an update expression, UpdateItem creates an item of the key alone.
        if (!_values.Rare())
        {
            request["UpdateExpression"] = expressions.Update(keys);
        }

        if (_values.Chance(0.35))
        {
            request["ConditionExpression"] = expressions.Condition(keys);
            ConditionFailureReturn(request);
        }

        if (_values.Chance(0.6))
        {
            request["ReturnValues"] = _values.Pick(["NONE", "ALL_OLD", "ALL_NEW", "UPDATED_OLD", "UPDATED_NEW"]);
        }

        Capacity(request);
        expressions.WriteTo(request);
        return new Operation("UpdateItem", request);
    }

    private Operation DeleteItem()
    {
        var main = _values.Chance(0.85);
        var request = new JsonObject { ["TableName"] = main ? Schema.Main : Schema.Other, ["Key"] = main ? _values.MainKey() : _values.OtherKey() };
        var expressions = new Expressions(_values);
        if (_values.Chance(0.4))
        {
            request["ConditionExpression"] = expressions.Condition(main ? MainKeys : OtherKeys);
            ConditionFailureReturn(request);
        }

        if (_values.Chance(0.5))
        {
            request["ReturnValues"] = _values.Rare() ? "ALL_NEW" : _values.Pick(["NONE", "ALL_OLD"]);
        }

        Capacity(request);
        expressions.WriteTo(request);
        return new Operation("DeleteItem", request);
    }

    private Operation GetItem()
    {
        var main = _values.Chance(0.8);
        var request = new JsonObject { ["TableName"] = main ? Schema.Main : Schema.Other, ["Key"] = main ? _values.MainKey() : _values.OtherKey() };
        var expressions = new Expressions(_values);
        if (_values.Chance(0.5))
        {
            request["ConsistentRead"] = _values.Chance(0.5);
        }

        if (_values.Chance(0.3))
        {
            request["ProjectionExpression"] = expressions.Projection(main ? MainKeys : OtherKeys);
        }

        Capacity(request);
        expressions.WriteTo(request);
        return new Operation("GetItem", request);
    }

    private Operation Query()
    {
        var onIndex = _values.Chance(0.3);
        var request = new JsonObject { ["TableName"] = Schema.Main };
        var expressions = new Expressions(_values);
        string[] keys;
        if (onIndex)
        {
            request["IndexName"] = Schema.Index;
            keys = IndexKeys;
            request["KeyConditionExpression"] = $"{expressions.Name("gpk")} = {expressions.Value(_values.IndexPartitionValue())}" + SortCondition(expressions, "gsk", _values.IndexSortValue, numeric: true);
        }
        else
        {
            keys = MainKeys;
            var partition = expressions.Value(_values.Rare() ? _values.Number() : ValueSource.Value("S", _values.Pick(ValueSource.PartitionKeys)));
            request["KeyConditionExpression"] = $"{expressions.Name("pk")} = {partition}" + SortCondition(expressions, "sk", _values.SortKeyValue, Schema.SortKeyType == "N");
        }

        if (_values.Chance(0.4))
        {
            request["FilterExpression"] = expressions.Condition(keys, 1);
        }

        ReadOptions(request, expressions, onIndex, keys);
        if (_values.Chance(0.3))
        {
            request["ScanIndexForward"] = false;
        }

        expressions.WriteTo(request);
        return new Operation("Query", request, onIndex ? Reading.Unordered : Reading.Pages);
    }

    private Operation Scan()
    {
        var main = _values.Chance(0.8);
        var onIndex = main && _values.Chance(0.25);
        var request = new JsonObject { ["TableName"] = main ? Schema.Main : Schema.Other };
        var keys = onIndex ? IndexKeys : main ? MainKeys : OtherKeys;
        if (onIndex)
        {
            request["IndexName"] = Schema.Index;
        }

        var expressions = new Expressions(_values);
        if (_values.Chance(0.5))
        {
            request["FilterExpression"] = expressions.Condition(keys, 1);
        }

        ReadOptions(request, expressions, onIndex, keys);
        if (_values.Chance(0.15))
        {
            request["TotalSegments"] = _values.Next(1, 4);
        }

        expressions.WriteTo(request);
        return new Operation("Scan", request, Reading.Unordered);
    }

    private string SortCondition(Expressions expressions, string attribute, Func<JsonObject> value, bool numeric)
    {
        if (_values.Chance(0.35))
        {
            return string.Empty;
        }

        var name = expressions.Name(attribute);
        return _values.Next(0, 4) switch
        {
            0 => $" AND {name} BETWEEN {expressions.Value(value())} AND {expressions.Value(value())}",
            1 when !numeric || _values.Rare() => $" AND begins_with({name}, {expressions.Value(Prefix(value()))})",
            _ => $" AND {name} {_values.Pick(Comparators)} {expressions.Value(value())}",
        };
    }

    // A prefix of a key value: a few leading characters of a string, the value itself for others.
    private JsonObject Prefix(JsonObject value)
    {
        if (value.ContainsKey("S"))
        {
            var text = value["S"]!.GetValue<string>();
            return ValueSource.Value("S", text[..Math.Min(text.Length, _values.Next(0, 3))]);
        }

        return value;
    }

    private void ReadOptions(JsonObject request, Expressions expressions, bool onIndex, string[] keys)
    {
        if (_values.Chance(0.4))
        {
            request["Limit"] = _values.Rare() ? 0 : _values.Next(1, 6);
        }

        var select = _values.Next(0, 100) switch
        {
            < 12 => "COUNT",
            < 17 => "SPECIFIC_ATTRIBUTES",
            < 21 => "ALL_PROJECTED_ATTRIBUTES",
            < 24 => "ALL_ATTRIBUTES",
            _ => null,
        };
        if (select is not null)
        {
            request["Select"] = select;
        }

        if (select is "SPECIFIC_ATTRIBUTES" ? !_values.Rare() : _values.Chance(0.2))
        {
            request["ProjectionExpression"] = expressions.Projection(keys);
        }

        if (_values.Chance(0.3) && (!onIndex || _values.Rare()))
        {
            request["ConsistentRead"] = true;
        }

        Capacity(request);
    }

    private Operation BatchWriteItem()
    {
        var count = _values.Rare() ? 26 : _values.Next(1, 9);
        var items = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var main = _values.Chance(0.75);
            var table = main ? Schema.Main : Schema.Other;
            JsonObject write;
            if (_values.Chance(0.7))
            {
                var item = main ? _values.MainItem() : _values.OtherItem();
                if (!Fresh(seen, table, KeyOf(item, main)))
                {
                    continue;
                }

                write = new JsonObject { ["PutRequest"] = new JsonObject { ["Item"] = item } };
            }
            else
            {
                var key = main ? _values.MainKey() : _values.OtherKey();
                if (!Fresh(seen, table, key))
                {
                    continue;
                }

                write = new JsonObject { ["DeleteRequest"] = new JsonObject { ["Key"] = key } };
            }

            if (!items.TryGetValue(table, out var writes))
            {
                items[table] = writes = [];
            }

            writes.Add(write);
        }

        var request = new JsonObject { ["RequestItems"] = ToObject(items) };
        Capacity(request);
        return new Operation("BatchWriteItem", request);
    }

    private Operation BatchGetItem()
    {
        var tables = new JsonObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var main in _values.Chance(0.3) ? [true, false] : new[] { _values.Chance(0.75) })
        {
            var table = main ? Schema.Main : Schema.Other;
            var keys = new JsonArray();
            var count = _values.Next(1, 7);
            for (var i = 0; i < count; i++)
            {
                var key = main ? _values.MainKey() : _values.OtherKey();
                if (Fresh(seen, table, key))
                {
                    keys.Add(key);
                }
            }

            var entry = new JsonObject { ["Keys"] = keys };
            var expressions = new Expressions(_values);
            if (_values.Chance(0.3))
            {
                entry["ProjectionExpression"] = expressions.Projection(main ? MainKeys : OtherKeys);
            }

            if (_values.Chance(0.3))
            {
                entry["ConsistentRead"] = true;
            }

            expressions.WriteTo(entry);
            tables[table] = entry;
        }

        var request = new JsonObject { ["RequestItems"] = tables };
        Capacity(request);
        return new Operation("BatchGetItem", request);
    }

    private void TransactWriteItems(List<Operation> operations)
    {
        var actions = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = _values.Next(1, 5);
        for (var i = 0; i < count; i++)
        {
            var main = _values.Chance(0.7);
            var table = main ? Schema.Main : Schema.Other;
            var keys = main ? MainKeys : OtherKeys;
            var expressions = new Expressions(_values);
            var action = new JsonObject { ["TableName"] = table };
            string kind;
            var roll = _values.Next(0, 10);
            if (roll < 4)
            {
                kind = "Put";
                var item = main ? _values.MainItem() : _values.OtherItem();
                if (!Fresh(seen, table, KeyOf(item, main)))
                {
                    continue;
                }

                action["Item"] = item;
            }
            else
            {
                var key = main ? _values.MainKey() : _values.OtherKey();
                if (!Fresh(seen, table, key))
                {
                    continue;
                }

                action["Key"] = key;
                kind = roll switch { < 7 => "Update", < 9 => "Delete", _ => "ConditionCheck" };
                if (kind == "Update")
                {
                    action["UpdateExpression"] = expressions.Update(keys);
                }
            }

            if (kind == "ConditionCheck" || _values.Chance(0.5))
            {
                action["ConditionExpression"] = expressions.Condition(keys);
                if (_values.Chance(0.4))
                {
                    action["ReturnValuesOnConditionCheckFailure"] = "ALL_OLD";
                }
            }

            expressions.WriteTo(action);
            actions.Add(new JsonObject { [kind] = action });
        }

        var request = new JsonObject { ["TransactItems"] = actions };
        Capacity(request);
        if (!_values.Chance(0.12))
        {
            operations.Add(new Operation("TransactWriteItems", request));
            return;
        }

        // Idempotency: the same token again with the same request changes nothing; with another request it is IdempotentParameterMismatchException.
        request["ClientRequestToken"] = "$RUN-" + _seed.ToString(CultureInfo.InvariantCulture) + "-" + (_tokens++).ToString(CultureInfo.InvariantCulture);
        operations.Add(new Operation("TransactWriteItems", request));
        var again = (JsonObject)request.DeepClone();
        if (_values.Chance(0.3))
        {
            if (!again.Remove("ReturnConsumedCapacity"))
            {
                again["ReturnConsumedCapacity"] = "TOTAL";
            }
        }
        else if (_values.Chance(0.3))
        {
            again["TransactItems"]!.AsArray().Add(new JsonObject
            {
                ["ConditionCheck"] = new JsonObject { ["TableName"] = Schema.Other, ["Key"] = new JsonObject { ["id"] = ValueSource.Value("S", "never") }, ["ConditionExpression"] = "attribute_not_exists(id)" },
            });
        }

        operations.Add(new Operation("TransactWriteItems", again));
    }

    private Operation TransactGetItems()
    {
        var gets = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = _values.Next(1, 5);
        for (var i = 0; i < count; i++)
        {
            var main = _values.Chance(0.7);
            var table = main ? Schema.Main : Schema.Other;
            var key = main ? _values.MainKey() : _values.OtherKey();
            if (!Fresh(seen, table, key))
            {
                continue;
            }

            var get = new JsonObject { ["TableName"] = table, ["Key"] = key };
            var expressions = new Expressions(_values);
            if (_values.Chance(0.3))
            {
                get["ProjectionExpression"] = expressions.Projection(main ? MainKeys : OtherKeys);
            }

            expressions.WriteTo(get);
            gets.Add(new JsonObject { ["Get"] = get });
        }

        var request = new JsonObject { ["TransactItems"] = gets };
        Capacity(request);
        return new Operation("TransactGetItems", request);
    }

    private void ConditionFailureReturn(JsonObject request)
    {
        if (_values.Chance(0.4))
        {
            request["ReturnValuesOnConditionCheckFailure"] = _values.Rare() ? "ALL_NEW" : "ALL_OLD";
        }
    }

    private void Capacity(JsonObject request)
    {
        if (_values.Chance(0.15))
        {
            request["ReturnConsumedCapacity"] = _values.Chance(0.7) ? "TOTAL" : "INDEXES";
        }
    }

    // Keys of one request are kept apart, except rarely, because DynamoDB rejects a key twice and that would end most batches.
    private bool Fresh(HashSet<string> seen, string table, JsonObject key) => seen.Add(table + Canonical.Of(key)) || _values.Rare();
}
