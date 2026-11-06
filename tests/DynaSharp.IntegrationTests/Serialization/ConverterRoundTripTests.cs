using System.Numerics;
using System.Text.Json;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Serialization;

/// <summary>
/// Every built-in converter against both emulators. Each value is written with the converter, stored with a raw PutItem, read back raw
/// with a strongly consistent GetItem, and then compared in two ways: what the emulator holds must be the DynamoDB JSON written down in the
/// test (numbers by value, sets without order), and reading it with the converter must return the value that was stored. The first is the
/// point: it is what DynamoDB will see and what a query or a TTL sweep works on.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ConverterRoundTripTests(Backend backend)
{
    private static readonly Guid SampleGuid = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Test]
    public async Task Strings_StoreAsStringValues()
    {
        // Arrange
        const string text = "say \"hi\" back\\slash\nline ä € \U0001F600";
        var cases = new[]
        {
            ConverterCase.Of<string, StringConverter>("string", "Alice", """{"S":"Alice"}"""),
            ConverterCase.Of<string, StringConverter>("empty", string.Empty, """{"S":""}"""),
            ConverterCase.Of<string, StringConverter>("escapes", text, $$"""{"S":{{JsonSerializer.Serialize(text)}}}"""),
            ConverterCase.Of<bool, BoolConverter>("true", true, """{"BOOL":true}"""),
            ConverterCase.Of<bool, BoolConverter>("false", false, """{"BOOL":false}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Numbers_StoreAsNumberValues()
    {
        // Arrange: DynamoDB keeps the value, not the spelling: 12.50 comes back as 12.5, 1E+21 as digits
        var cases = new[]
        {
            ConverterCase.Of<int, NumberConverter<int>>("int-min", int.MinValue, """{"N":"-2147483648"}"""),
            ConverterCase.Of<int, NumberConverter<int>>("int-max", int.MaxValue, """{"N":"2147483647"}"""),
            ConverterCase.Of<long, NumberConverter<long>>("long-min", long.MinValue, """{"N":"-9223372036854775808"}"""),
            ConverterCase.Of<long, NumberConverter<long>>("long-max", long.MaxValue, """{"N":"9223372036854775807"}"""),
            ConverterCase.Of<ulong, NumberConverter<ulong>>("ulong-max", ulong.MaxValue, """{"N":"18446744073709551615"}"""),
            ConverterCase.Of<byte, NumberConverter<byte>>("byte-max", byte.MaxValue, """{"N":"255"}"""),
            ConverterCase.Of<sbyte, NumberConverter<sbyte>>("sbyte-min", sbyte.MinValue, """{"N":"-128"}"""),
            ConverterCase.Of<decimal, NumberConverter<decimal>>("decimal-scale", 12.50m, """{"N":"12.5"}"""),
            ConverterCase.Of<decimal, NumberConverter<decimal>>("decimal-28-digits", 0.1234567890123456789012345678m, """{"N":"0.1234567890123456789012345678"}"""),
            ConverterCase.Of<decimal, NumberConverter<decimal>>("decimal-max", decimal.MaxValue, """{"N":"79228162514264337593543950335"}"""),
            ConverterCase.Of<decimal, NumberConverter<decimal>>("decimal-min", decimal.MinValue, """{"N":"-79228162514264337593543950335"}"""),
            ConverterCase.Of<double, NumberConverter<double>>("double-repeating", 0.1 + 0.2, """{"N":"0.30000000000000004"}"""),
            ConverterCase.Of<double, NumberConverter<double>>("double-large", 1e21, """{"N":"1000000000000000000000"}"""),
            ConverterCase.Of<double, NumberConverter<double>>("double-small", 1e-7, """{"N":"0.0000001"}"""),
            ConverterCase.Of<float, NumberConverter<float>>("float", 1.1f, """{"N":"1.1"}"""),
            ConverterCase.Of<Half, NumberConverter<Half>>("half", (Half)1.5f, """{"N":"1.5"}"""),
            ConverterCase.Of<BigInteger, NumberConverter<BigInteger>>("big-integer", BigInteger.Pow(10, 30), """{"N":"1000000000000000000000000000000"}"""),
            ConverterCase.Of<Int128, NumberConverter<Int128>>("int128", Int128.Parse("10000000000000000000000000000000000000", System.Globalization.CultureInfo.InvariantCulture), """{"N":"10000000000000000000000000000000000000"}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DatesAndIdentifiers_StoreAsStringValues()
    {
        // Arrange: instants are stored in UTC with seven fraction digits, so they sort as text
        var cases = new[]
        {
            ConverterCase.Of<Guid, GuidConverter>("guid", SampleGuid, """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}"""),
            ConverterCase.Of<DateTimeOffset, DateTimeOffsetConverter>(
                "instant-with-offset",
                new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234567),
                """{"S":"2026-10-31T10:00:00.1234567Z"}""",
                static (stored, read) => stored.UtcTicks == read.UtcTicks && read.Offset == TimeSpan.Zero),
            ConverterCase.Of<DateTimeOffset, DateTimeOffsetConverter>("instant-min", DateTimeOffset.MinValue, """{"S":"0001-01-01T00:00:00.0000000Z"}"""),
            ConverterCase.Of<DateTimeOffset, DateTimeOffsetConverter>("instant-max", DateTimeOffset.MaxValue, """{"S":"9999-12-31T23:59:59.9999999Z"}"""),
            ConverterCase.Of<DateTime, DateTimeConverter>(
                "utc-time",
                new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
                """{"S":"2026-10-31T12:00:00.0000000Z"}""",
                static (stored, read) => stored == read && read.Kind == DateTimeKind.Utc),
            ConverterCase.Of<DateOnly, DateOnlyConverter>("date", new DateOnly(2024, 2, 29), """{"S":"2024-02-29"}"""),
            ConverterCase.Of<TimeOnly, TimeOnlyConverter>("time", new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(7890123)), """{"S":"12:34:56.7890123"}"""),
            ConverterCase.Of<TimeSpan, TimeSpanConverter>("span", new TimeSpan(-1, -2, -3, -4, -500), """{"S":"-1.02:03:04.5000000"}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EpochSeconds_StoreAsANumberValue()
    {
        // Arrange (a TTL attribute must be a number; a string is ignored by the sweep)
        var instant = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2));
        var cases = new[]
        {
            ConverterCase.Of<DateTimeOffset, EpochSecondsConverter>(
                "ttl",
                instant,
                """{"N":"1793440800"}""",
                static (stored, read) => stored.UtcTicks == read.UtcTicks),
            ConverterCase.Of<DateTimeOffset, EpochSecondsConverter>("ttl-epoch", DateTimeOffset.UnixEpoch, """{"N":"0"}"""),
            ConverterCase.Of<DateTimeOffset, EpochSecondsConverter>("ttl-before-epoch", DateTimeOffset.UnixEpoch.AddSeconds(-1), """{"N":"-1"}"""),
            ConverterCase.Of<DateTimeOffset?, NullableConverter<DateTimeOffset, EpochSecondsConverter>>("ttl-null", null, """{"NULL":true}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task OptInConverters_StoreTheFormsOtherWritersUse()
    {
        // Arrange: the AWS SDK's booleans and times, a time with its offset, and the wrapper, which writes what it wraps
        var cases = new[]
        {
            ConverterCase.Of<bool, BoolAsNumberConverter>("bool-as-number-true", true, """{"N":"1"}"""),
            ConverterCase.Of<bool, BoolAsNumberConverter>("bool-as-number-false", false, """{"N":"0"}"""),
            ConverterCase.Of<DateTime, SdkDateTimeConverter>(
                "sdk-time",
                new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567),
                """{"S":"2026-10-31T12:00:00.123Z"}""",
                static (stored, read) => read == stored.AddTicks(-4567) && read.Kind == DateTimeKind.Utc),
            ConverterCase.Of<DateTimeOffset, OffsetTimestampConverter>(
                "offset-time",
                new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(1)),
                """{"S":"2026-11-20T11:00:00.0000000+01:00"}""",
                static (stored, read) => stored.DateTime == read.DateTime && stored.Offset == read.Offset),
            ConverterCase.Of<DateTimeOffset, EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>>("empty-as-missing", DateTimeOffset.UnixEpoch, """{"S":"1970-01-01T00:00:00.0000000Z"}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task BinaryValues_StoreAsBinaryValues()
    {
        // Arrange
        var everyByte = Enumerable.Range(0, 256).Select(static i => (byte)i).ToArray();
        var cases = new[]
        {
            ConverterCase.Of<byte[], BinaryConverter>("every-byte", everyByte, $$"""{"B":"{{Convert.ToBase64String(everyByte)}}"}""", SameBytes),
            ConverterCase.Of<byte[], BinaryConverter>("empty", [], """{"B":""}""", SameBytes),
            ConverterCase.Of<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>(
                "memory",
                new byte[] { 9, 1, 2, 3, 9 }.AsMemory(1, 3),
                """{"B":"AQID"}""",
                static (stored, read) => stored.Span.SequenceEqual(read.Span)),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task NullableValues_StoreTheValueOrTheNullValue()
    {
        // Arrange
        var cases = new[]
        {
            ConverterCase.Of<int?, NullableConverter<int, NumberConverter<int>>>("int-null", null, """{"NULL":true}"""),
            ConverterCase.Of<int?, NullableConverter<int, NumberConverter<int>>>("int-value", 5, """{"N":"5"}"""),
            ConverterCase.Of<Guid?, NullableConverter<Guid, GuidConverter>>("guid-value", SampleGuid, """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}"""),
            ConverterCase.Of<string?, NullableReferenceConverter<string, StringConverter>>("string-null", null, """{"NULL":true}"""),
            ConverterCase.Of<string?, NullableReferenceConverter<string, StringConverter>>("string-value", "x", """{"S":"x"}"""),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Lists_StoreAsListValuesInOrder()
    {
        // Arrange
        int[][] nested = [[1], [], [2, 3]];
        var cases = new[]
        {
            ConverterCase.Of<int[], ArrayConverter<int, NumberConverter<int>>>("array", [3, 1, 2], """{"L":[{"N":"3"},{"N":"1"},{"N":"2"}]}""", SameSequence),
            ConverterCase.Of<int[], ArrayConverter<int, NumberConverter<int>>>("array-empty", [], """{"L":[]}""", SameSequence),
            ConverterCase.Of<List<string>, ListConverter<string, StringConverter>>("list", ["a", string.Empty, "c"], """{"L":[{"S":"a"},{"S":""},{"S":"c"}]}""", SameSequence),
            ConverterCase.Of<IReadOnlyList<decimal>, ReadOnlyListConverter<decimal, NumberConverter<decimal>>>("read-only-list", [1.5m, 2m], """{"L":[{"N":"1.5"},{"N":"2"}]}""", SameSequence),
            ConverterCase.Of<IList<bool>, MutableListConverter<bool, BoolConverter>>("mutable-list", [true, false], """{"L":[{"BOOL":true},{"BOOL":false}]}""", SameSequence),
            ConverterCase.Of<int?[], ArrayConverter<int?, NullableConverter<int, NumberConverter<int>>>>("array-with-null", [1, null, 3], """{"L":[{"N":"1"},{"NULL":true},{"N":"3"}]}""", SameSequence),
            ConverterCase.Of<int[][], ArrayConverter<int[], ArrayConverter<int, NumberConverter<int>>>>(
                "nested",
                nested,
                """{"L":[{"L":[{"N":"1"}]},{"L":[]},{"L":[{"N":"2"},{"N":"3"}]}]}""",
                static (stored, read) => stored.Length == read.Length && stored.Zip(read).All(static pair => pair.First.AsSpan().SequenceEqual(pair.Second))),
            ConverterCase.Of<List<Guid>, ListConverter<Guid, GuidConverter>>("guids", [SampleGuid], """{"L":[{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}]}""", SameSequence),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Sets_StoreAsStringNumberAndBinarySets()
    {
        // Arrange (a set has no order; DynamoDB accepts the empty string and zero-length binary as members)
        var cases = new[]
        {
            ConverterCase.Of<HashSet<string>, HashSetConverter<string, StringSetElement>>("string-set", ["b", string.Empty, "a"], """{"SS":["a","b",""]}""", SameSet),
            ConverterCase.Of<HashSet<int>, HashSetConverter<int, NumberSetElement<int>>>("number-set", [int.MinValue, 0, int.MaxValue], """{"NS":["0","2147483647","-2147483648"]}""", SameSet),
            ConverterCase.Of<HashSet<decimal>, HashSetConverter<decimal, NumberSetElement<decimal>>>("decimal-set", [1.50m, 2m], """{"NS":["2","1.5"]}""", SameSet),
            ConverterCase.Of<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>(
                "binary-set",
                [[1, 2, 3], []],
                """{"BS":["","AQID"]}""",
                static (stored, read) => stored.Count == read.Count && stored.Select(Convert.ToHexString).Order(StringComparer.Ordinal).SequenceEqual(read.Select(Convert.ToHexString).Order(StringComparer.Ordinal))),
            ConverterCase.Of<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>("guid-set", [SampleGuid], """{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e"]}""", SameSet),
            ConverterCase.Of<IReadOnlySet<long>, ReadOnlySetConverter<long, NumberSetElement<long>>>("read-only-set", new HashSet<long> { 5, 7 }, """{"NS":["7","5"]}""", SameSet),
            ConverterCase.Of<string[], SetArrayConverter<string, StringSetElement>>("set-array", ["x", "y"], """{"SS":["y","x"]}""", SameSet),
            ConverterCase.Of<int[], SetArrayConverter<int, NumberSetElement<int>>>("number-set-array", [3, 1, 2], """{"NS":["1","2","3"]}""", SameSet),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Dictionaries_StoreAsMapValues()
    {
        // Arrange
        var cases = new[]
        {
            ConverterCase.Of<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>(
                "dictionary",
                new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
                """{"M":{"b":{"N":"2"},"a":{"N":"1"}}}""",
                SameEntries),
            ConverterCase.Of<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>("dictionary-empty", [], """{"M":{}}""", SameEntries),
            ConverterCase.Of<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>(
                "read-only-dictionary",
                new Dictionary<string, string> { ["k"] = "v" },
                """{"M":{"k":{"S":"v"}}}""",
                SameEntries),
            ConverterCase.Of<Dictionary<string, List<int>>, DictionaryConverter<List<int>, ListConverter<int, NumberConverter<int>>>>(
                "dictionary-of-lists",
                new Dictionary<string, List<int>> { ["x"] = [1, 2], ["y"] = [] },
                """{"M":{"x":{"L":[{"N":"1"},{"N":"2"}]},"y":{"L":[]}}}""",
                static (stored, read) => stored.Count == read.Count && stored.All(pair => read.TryGetValue(pair.Key, out var other) && pair.Value.SequenceEqual(other))),
            ConverterCase.Of<Dictionary<string, Dictionary<string, bool>>, DictionaryConverter<Dictionary<string, bool>, DictionaryConverter<bool, BoolConverter>>>(
                "dictionary-of-dictionaries",
                new Dictionary<string, Dictionary<string, bool>> { ["outer"] = new() { ["inner"] = true } },
                """{"M":{"outer":{"M":{"inner":{"BOOL":true}}}}}""",
                static (stored, read) => read.Count == 1 && read["outer"].Count == 1 && read["outer"]["inner"]),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task JsonElements_StoreAsTheNativeValueOfTheirJson()
    {
        // Arrange (DynamoDB keeps a number in a canonical form, which JsonElement.DeepEquals compares by value)
        var document = JsonElement.Parse("""{"s":"x","n":1.50,"t":true,"skipped":null,"l":["a",2,null,{"deep":[]}],"m":{}}""");
        var cases = new[]
        {
            ConverterCase.Of<JsonElement, JsonElementConverter>(
                "json-object",
                document,
                """{"M":{"s":{"S":"x"},"n":{"N":"1.5"},"t":{"BOOL":true},"l":{"L":[{"S":"a"},{"N":"2"},{"NULL":true},{"M":{"deep":{"L":[]}}}]},"m":{"M":{}}}}""",
                static (stored, read) => JsonElement.DeepEquals(JsonElement.Parse("""{"s":"x","n":1.5,"t":true,"l":["a",2,null,{"deep":[]}],"m":{}}"""), read) && stored.ValueKind == JsonValueKind.Object),
            ConverterCase.Of<JsonElement, JsonElementConverter>("json-string", JsonElement.Parse("\"2026-11-06T10:00:00+01:00\""), """{"S":"2026-11-06T10:00:00+01:00"}""", JsonElement.DeepEquals),
            ConverterCase.Of<JsonElement, JsonElementConverter>("json-null", JsonElement.Parse("null"), """{"NULL":true}""", JsonElement.DeepEquals),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AttributeValues_StoreAsTheirOwnType()
    {
        // Arrange: a value of every type, nested, read from the JSON that describes it
        const string everything = """
            {"M":{"s":{"S":"x"},"n":{"N":"1.5"},"b":{"B":"AQID"},"t":{"BOOL":true},"z":{"NULL":true},
                  "l":{"L":[{"S":"a"},{"N":"2"},{"M":{"deep":{"L":[]}}}]},"ss":{"SS":["a","b"]},"ns":{"NS":["1","2"]},"bs":{"BS":["AQID","BAUG"]}}}
            """;
        var value = ConverterCase.Deserialize<AttributeValue, AttributeValueConverter>(everything);
        var cases = new[]
        {
            ConverterCase.Of<AttributeValue, AttributeValueConverter>("everything", value, everything, static (stored, read) => StoredJson.Differences(ConverterCase.Serialize<AttributeValue, AttributeValueConverter>(stored), ConverterCase.Serialize<AttributeValue, AttributeValueConverter>(read)).Length == 0),
        };

        // Act
        var problems = await StoreAndCompareAsync(cases);

        // Assert
        await Assert.That(problems).IsEqualTo(string.Empty);
    }

    private static bool SameBytes(byte[] stored, byte[] read) => stored.AsSpan().SequenceEqual(read);

    private static bool SameSequence<T>(IEnumerable<T> stored, IEnumerable<T> read) => stored.SequenceEqual(read);

    private static bool SameSet<T>(IEnumerable<T> stored, IEnumerable<T> read)
    {
        var expected = new HashSet<T>(stored);
        return expected.SetEquals(read) && expected.Count == read.Count();
    }

    private static bool SameEntries<T>(IReadOnlyDictionary<string, T> stored, IReadOnlyDictionary<string, T> read) =>
        stored.Count == read.Count && stored.All(pair => read.TryGetValue(pair.Key, out var other) && EqualityComparer<T>.Default.Equals(pair.Value, other));

    private static async Task<string> PutAndGetAsync(RawDynamoDb raw, string table, string key, string valueJson)
    {
        var keyJson = JsonSerializer.Serialize(key);
        (await raw.SendAsync("PutItem", $$$"""{"TableName":"{{{table}}}","Item":{"pk":{"S":{{{keyJson}}}},"v":{{{valueJson}}}}}""")).EnsureSuccess();
        var response = (await raw.SendAsync("GetItem", $$$"""{"TableName":"{{{table}}}","Key":{"pk":{"S":{{{keyJson}}}}},"ConsistentRead":true}""")).EnsureSuccess();
        using var document = JsonDocument.Parse(response.Body);
        return document.RootElement.GetProperty("Item").GetProperty("v").GetRawText();
    }

    // Stores each case in an item of its own, reads it back raw and collects what differs, so one run reports every converter that is off.
    private async Task<string> StoreAndCompareAsync(IReadOnlyList<ConverterCase> cases)
    {
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var problems = new List<string>();
        foreach (var testCase in cases)
        {
            var stored = await PutAndGetAsync(sandbox.Emulator.Raw, table, testCase.Name, testCase.Write());
            var difference = StoredJson.Differences(testCase.Expected, stored);
            if (difference.Length > 0)
            {
                problems.Add($"{testCase.Name}: the emulator stored {stored}; {difference}");
            }
            else if (!testCase.ReadsBack(stored))
            {
                problems.Add($"{testCase.Name}: the converter did not read {stored} back as the value that was written");
            }
        }

        return string.Join(Environment.NewLine, problems);
    }
}
