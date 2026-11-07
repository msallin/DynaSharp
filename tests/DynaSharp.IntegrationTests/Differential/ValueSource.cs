using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Draws keys, attribute values and items in DynamoDB JSON. The domains are small, so that operations meet the items earlier ones wrote, and they hold the edge values:
/// empty strings and binaries, numbers that normalize (<c>1.0</c>, <c>01.50</c>, <c>1E+2</c>, <c>-0</c>), numbers at the limits of precision and magnitude, sets,
/// nested documents, reserved words as attribute names and items at the size limit. A few values are invalid on purpose, so that errors get compared too.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A seeded generator makes a sequence reproducible.")]
internal sealed class ValueSource(Random random, Schema schema)
{
    /// <summary>The largest item DynamoDB stores, in bytes.</summary>
    public const int ItemLimit = 400 * 1024;

    /// <summary>The non-key attribute names. <c>status</c>, <c>size</c>, <c>count</c>, <c>name</c> and <c>data</c> are reserved words; <c>x.y</c> holds a dot.</summary>
    public static readonly string[] AttributeNames = ["a", "b", "n", "s", "l", "m", "ss", "ns", "bs", "f", "status", "size", "count", "name", "data", "x.y"];

    /// <summary>The partition key values of the main table.</summary>
    public static readonly string[] PartitionKeys = ["p1", "p2", "p3"];

    private static readonly string[] SortStrings = ["a", "a#1", "a#10", "a#2", "ab", "b", "B", "z", "é", "ÿ", "日本", "😀", "a b"];

    private static readonly string[] SortNumbers = ["1", "1.0", "2", "-1", "10", "0.5", "1E+1", "-0", "100", "99999999999999999999999999999999999999", "-1E-10", "3.14"];

    private static readonly string[] SortBinaries = ["AA==", "AQ==", "AAE=", "/w==", "AQA=", "AQID", "gA=="];

    private static readonly string[] OtherIds = ["i1", "i2", "i3", "i4"];

    private static readonly string[] IndexPartitions = ["g1", "g2"];

    private static readonly string[] IndexSorts = ["1", "2", "3", "2.0", "-1", "0.5"];

    private static readonly string[] Strings = ["x", "y", "hello", "Hello", "é", "日本", "😀", "a#1", "a", "ab", "b", "0", "1", "true", " "];

    private static readonly string[] Numbers =
    [
        "0", "1", "2", "-1", "1.0", "01.50", "1.5", "-0", "1E+2", "1e-2", "0.1", "123.456", "-7", "10", "100", "1E+125",
        "9.9999999999999999999999999999999999999E+125", "1E-130", "-1E-130", "12345678901234567890123456789012345678",
        "0.12345678901234567890123456789012345678", "1000000000000000000000000000000000000000", "5E+3",
    ];

    // Out of range, too precise, or not a number at all.
    private static readonly string[] BadNumbers = ["1E+126", "1E-131", "123456789012345678901234567890123456789", "abc", string.Empty, "1e", "NaN", "+1", ".5", "5.", " 1", "0x10"];

    private static readonly string[] Binaries = ["AA==", "AQID", "/w==", "aGVsbG8=", "AAAA"];

    private static readonly string[] MapKeys = ["k", "j", "count", "x"];

    /// <summary>A value of a type with its text.</summary>
    /// <param name="type">The type descriptor, such as <c>S</c>.</param>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static JsonObject Value(string type, string text) => new() { [type] = text };

    /// <summary>Tells whether a rare case happens: about three times in a hundred.</summary>
    /// <returns><see langword="true"/> rarely.</returns>
    public bool Rare() => random.NextDouble() < 0.03;

    /// <summary>Tells whether something happens with probability <paramref name="p"/>.</summary>
    /// <param name="p">The probability.</param>
    /// <returns><see langword="true"/> with that probability.</returns>
    public bool Chance(double p) => random.NextDouble() < p;

    /// <summary>Picks one of <paramref name="values"/>.</summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="values">The values.</param>
    /// <returns>One of them.</returns>
    public T Pick<T>(IReadOnlyList<T> values) => values[random.Next(values.Count)];

    /// <summary>Draws a number in a range.</summary>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The exclusive upper bound.</param>
    /// <returns>The number.</returns>
    public int Next(int min, int max) => random.Next(min, max);

    /// <summary>Draws a partition key value of the main table, rarely an empty one.</summary>
    /// <returns>The value.</returns>
    public JsonObject PartitionKey() => Value("S", Rare() ? string.Empty : Pick(PartitionKeys));

    /// <summary>Draws a sort key value of the main table, of its schema's type, rarely an empty or a mistyped one.</summary>
    /// <returns>The value.</returns>
    public JsonObject SortKey()
    {
        if (Rare())
        {
            return Chance(0.5) ? Value(schema.SortKeyType, string.Empty) : Value(schema.SortKeyType == "S" ? "N" : "S", "1");
        }

        return Value(schema.SortKeyType, SortKeyText());
    }

    /// <summary>Draws the text of a valid sort key value.</summary>
    /// <returns>The text, in the wire form of the schema's type.</returns>
    public string SortKeyText() => schema.SortKeyType switch
    {
        "S" => Pick(SortStrings),
        "N" => Pick(SortNumbers),
        _ => Pick(SortBinaries),
    };

    /// <summary>Draws the key of an item of the main table.</summary>
    /// <returns>The key.</returns>
    public JsonObject MainKey()
    {
        var key = new JsonObject { ["pk"] = PartitionKey(), ["sk"] = SortKey() };
        if (Rare())
        {
            // A key that does not match the schema: one attribute missing, or one too many.
            if (Chance(0.5))
            {
                key.Remove("sk");
            }
            else
            {
                key["a"] = Value("S", "x");
            }
        }

        return key;
    }

    /// <summary>Draws the key of an item of the other table.</summary>
    /// <returns>The key.</returns>
    public JsonObject OtherKey() => new() { ["id"] = Value("S", Pick(OtherIds)) };

    /// <summary>Draws an item of the main table: its key, some attributes and, mostly, the keys of the index.</summary>
    /// <returns>The item.</returns>
    public JsonObject MainItem()
    {
        var item = MainKey();
        if (Chance(0.012) && schema.SortKeyType == "S" && item.ContainsKey("sk"))
        {
            return BigItem(item);
        }

        AddAttributes(item);
        if (Chance(0.7))
        {
            item["gpk"] = Rare() ? Value("N", "1") : Value("S", Pick(IndexPartitions));
        }

        if (Chance(0.7))
        {
            item["gsk"] = Rare() ? Value("S", "1") : Value("N", Pick(IndexSorts));
        }

        return item;
    }

    /// <summary>Draws an item of the other table.</summary>
    /// <returns>The item.</returns>
    public JsonObject OtherItem()
    {
        var item = OtherKey();
        AddAttributes(item);
        return item;
    }

    /// <summary>Draws a value that fits the usual type of an attribute, or any value now and then.</summary>
    /// <param name="attribute">The attribute name.</param>
    /// <returns>The value.</returns>
    public JsonObject ValueFor(string attribute)
    {
        if (Chance(0.15))
        {
            return Any(2);
        }

        return attribute switch
        {
            "n" or "count" or "size" => Number(),
            "l" => List(2),
            "m" or "data" => Map(2),
            "ss" => StringSet(),
            "ns" => NumberSet(),
            "bs" => BinarySet(),
            "f" => new JsonObject { ["BOOL"] = Chance(0.5) },
            "b" => Binary(),
            _ => String(),
        };
    }

    /// <summary>Draws any value, nesting lists and maps up to <paramref name="depth"/>.</summary>
    /// <param name="depth">How deep documents may nest.</param>
    /// <returns>The value.</returns>
    public JsonObject Any(int depth)
    {
        var kinds = depth > 0 ? 11 : 9;
        return random.Next(kinds) switch
        {
            0 or 1 => String(),
            2 or 3 => Number(),
            4 => Binary(),
            5 => new JsonObject { ["BOOL"] = Chance(0.5) },
            6 => new JsonObject { ["NULL"] = true },
            7 => StringSet(),
            8 => Chance(0.5) ? NumberSet() : BinarySet(),
            9 => List(depth - 1),
            _ => Map(depth - 1),
        };
    }

    /// <summary>Draws a string value, mostly short, rarely empty.</summary>
    /// <returns>The value.</returns>
    public JsonObject String() => Value("S", Chance(0.06) ? string.Empty : Pick(Strings));

    /// <summary>Draws a number value, rarely an invalid one.</summary>
    /// <returns>The value.</returns>
    public JsonObject Number() => Value("N", NumberText());

    /// <summary>Draws the text of a number, rarely an invalid one.</summary>
    /// <returns>The text.</returns>
    public string NumberText() => Rare() ? Pick(BadNumbers) : Pick(Numbers);

    /// <summary>Draws a binary value, rarely empty.</summary>
    /// <returns>The value.</returns>
    public JsonObject Binary() => Value("B", Chance(0.06) ? string.Empty : Pick(Binaries));

    /// <summary>Draws a string set: mostly distinct elements, rarely an empty set or a duplicate.</summary>
    /// <returns>The value.</returns>
    public JsonObject StringSet() => Set("SS", Strings);

    /// <summary>Draws a number set: rarely an empty set, or a duplicate such as <c>1</c> and <c>1.0</c>.</summary>
    /// <returns>The value.</returns>
    public JsonObject NumberSet() => Set("NS", Numbers);

    /// <summary>Draws a binary set: rarely an empty set or a duplicate.</summary>
    /// <returns>The value.</returns>
    public JsonObject BinarySet() => Set("BS", Binaries);

    /// <summary>Draws a list value.</summary>
    /// <param name="depth">How deep it may nest.</param>
    /// <returns>The value.</returns>
    public JsonObject List(int depth)
    {
        var list = new JsonArray();
        var count = random.Next(0, 4);
        for (var i = 0; i < count; i++)
        {
            list.Add(depth > 0 ? Any(depth) : String());
        }

        return new JsonObject { ["L"] = list };
    }

    /// <summary>Draws a map value.</summary>
    /// <param name="depth">How deep it may nest.</param>
    /// <returns>The value.</returns>
    public JsonObject Map(int depth)
    {
        var map = new JsonObject();
        var count = random.Next(0, 4);
        for (var i = 0; i < count; i++)
        {
            map[Pick(MapKeys)] = depth > 0 ? Any(depth) : Number();
        }

        return new JsonObject { ["M"] = map };
    }

    /// <summary>Draws a value of the type of the sort key, valid, for a key condition.</summary>
    /// <returns>The value.</returns>
    public JsonObject SortKeyValue() => Value(schema.SortKeyType, SortKeyText());

    /// <summary>Draws a value of the index sort key.</summary>
    /// <returns>The value.</returns>
    public JsonObject IndexSortValue() => Value("N", Pick(IndexSorts));

    /// <summary>Draws a value of the index partition key.</summary>
    /// <returns>The value.</returns>
    public JsonObject IndexPartitionValue() => Value("S", Pick(IndexPartitions));

    private JsonObject Set(string type, string[] pool)
    {
        var elements = new JsonArray();
        if (Rare())
        {
            return new JsonObject { [type] = elements };
        }

        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var count = random.Next(1, 4);
        for (var i = 0; i < count; i++)
        {
            var element = Pick(pool);
            if (element.Length > 0 && chosen.Add(element))
            {
                elements.Add(element);
            }
        }

        if (elements.Count == 0)
        {
            elements.Add(pool[0]);
        }

        if (Rare())
        {
            elements.Add(type == "NS" ? Pick(["1", "1.0", "01"]) : elements[0]!.GetValue<string>());
        }

        return new JsonObject { [type] = elements };
    }

    private void AddAttributes(JsonObject item)
    {
        var count = random.Next(0, 6);
        for (var i = 0; i < count; i++)
        {
            var name = Pick(AttributeNames);
            item[name] = ValueFor(name);
        }
    }

    // An item at the size limit: DynamoDB counts the UTF-8 bytes of each attribute name and string value, so with a string sort key the size is exact,
    // and the item ends just below, at or just above 400 KB.
    private JsonObject BigItem(JsonObject key)
    {
        var used = 0;
        foreach (var (name, value) in key)
        {
            used += Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value!.AsObject().Single().Value!.GetValue<string>());
        }

        var target = ItemLimit + Pick([-1, 0, 1, -5000, 2000]);
        var length = Math.Max(1, target - used - "big".Length);
        key["big"] = Value("S", new string('x', length));
        return key;
    }
}
