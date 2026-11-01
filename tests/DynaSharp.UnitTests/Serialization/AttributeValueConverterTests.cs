using System.Collections.ObjectModel;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class AttributeValueConverterTests
{
    // The examples of every type from the DynamoDB API reference (AttributeValue, via botocore service-2.json).
    public static IEnumerable<Func<(string Json, AttributeValue Expected)>> DocumentationSamples()
    {
        yield return () => ("""{"S": "Hello"}""", "Hello");
        yield return () => ("""{"N": "123.45"}""", AttributeValue.FromNumber("123.45"));
        yield return () => ("""{"B": "dGhpcyB0ZXh0IGlzIGJhc2U2NC1lbmNvZGVk"}""", AttributeValue.FromBinary("this text is base64-encoded"u8));
        yield return () => ("""{"SS": ["Giraffe", "Hippo" ,"Zebra"]}""", AttributeValue.FromStringSet(["Giraffe", "Hippo", "Zebra"]));
        yield return () => ("""{"NS": ["42.2", "-19", "7.5", "3.14"]}""", AttributeValue.FromNumberSet(["42.2", "-19", "7.5", "3.14"]));
        yield return () => ("""{"BS": ["U3Vubnk=", "UmFpbnk=", "U25vd3k="]}""", AttributeValue.FromBinarySet(["Sunny"u8.ToArray(), "Rainy"u8.ToArray(), "Snowy"u8.ToArray()]));
        yield return () => ("""{"M": {"Name": {"S": "Joe"}, "Age": {"N": "35"}}}""", AttributeValue.FromMap([new("Name", "Joe"), new("Age", AttributeValue.FromNumber(35))]));
        yield return () => ("""{"L": [ {"S": "Cookies"} , {"S": "Coffee"}, {"N": "3.14159"}]}""", AttributeValue.FromList(["Cookies", "Coffee", AttributeValue.FromNumber("3.14159")]));
        yield return () => ("""{"NULL": true}""", AttributeValue.Null);
        yield return () => ("""{"BOOL": true}""", AttributeValue.FromBool(true));
    }

    public static IEnumerable<Func<AttributeValue>> RoundTripValues()
    {
        yield return () => string.Empty;
        yield return () => "café \"quoted\" \\ \u0001 😀 <html>";
        yield return () => AttributeValue.FromNumber("0");
        yield return () => AttributeValue.FromNumber("-9.9999999999999999999999999999999999999E+125");
        yield return () => AttributeValue.FromNumber("1.50");
        yield return () => AttributeValue.FromBinary([]);
        yield return () => AttributeValue.FromBinary(AllBytes());
        yield return () => AttributeValue.FromBool(false);
        yield return () => AttributeValue.FromBool(true);
        yield return () => AttributeValue.Null;
        yield return () => AttributeValue.FromList([]);
        yield return () => AttributeValue.FromList(EveryType());
        yield return () => AttributeValue.FromList([AttributeValue.FromList([AttributeValue.FromList(["deep"])])]);
        yield return () => AttributeValue.FromList(Enumerable.Range(0, 100).Select(i => AttributeValue.FromNumber(i)));
        yield return () => AttributeValue.FromMap([]);
        yield return () => AttributeValue.FromMap(EveryType().Select((value, i) => KeyValuePair.Create($"attribute{i}", value)));
        yield return () => AttributeValue.FromStringSet([string.Empty, "a", "é"]);
        yield return () => AttributeValue.FromStringSet(Enumerable.Range(0, 40).Select(i => $"s{i}"));
        yield return () => AttributeValue.FromNumberSet(["1", "1.0", "-1E-130"]);
        yield return () => AttributeValue.FromBinarySet([[], [0], [0, 0]]);
        yield return () => AttributeValue.FromBinarySet(Enumerable.Range(0, 40).Select(i => new[] { (byte)i }));
    }

    public static IEnumerable<Func<ValueSample>> DocumentationCases() => DocumentationSamples().Select(static sample => new Func<ValueSample>(() =>
    {
        var (json, value) = sample();
        return new ValueSample(json, value);
    }));

    public static IEnumerable<Func<ValueSample>> RoundTripCases() => RoundTripValues().Select(static value => new Func<ValueSample>(() => new ValueSample(string.Empty, value())));

    [Test]
    [MethodDataSource(nameof(DocumentationCases))]
    public async Task Read_DocumentationSample_ReturnsEqualValue(ValueSample sample)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(sample);

        // Act
        var value = TestJson.ReadValue(sample.Json);

        // Assert
        await Assert.That(value).IsEqualTo(sample.Value);
    }

    [Test]
    [MethodDataSource(nameof(DocumentationCases))]
    public async Task Write_DocumentationSample_WritesSameJsonWithoutWhitespace(ValueSample sample)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(sample);
        var expected = Minify(sample.Json);

        // Act
        var written = TestJson.WriteValue(sample.Value);

        // Assert
        await Assert.That(written).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(RoundTripCases))]
    public async Task Read_WrittenValue_ReturnsEqualValue(ValueSample sample)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(sample);
        var json = TestJson.WriteValue(sample.Value);

        // Act
        var read = TestJson.ReadValue(json);

        // Assert
        await Assert.That(read).IsEqualTo(sample.Value);
    }

    [Test]
    public async Task Read_EscapedNamesAndValues_ReturnsUnescapedValue()
    {
        // Arrange
        const string json = """{"\u004D":{"n\u0061me":{"S":"caf\u00e9"},"bytes":{"B":"AQ\u003d\u003d"},"n":{"N":"4\u0032"}}}""";
        var expected = AttributeValue.FromMap([new("name", "café"), new("bytes", AttributeValue.FromBinary([1])), new("n", AttributeValue.FromNumber("42"))]);

        // Act
        var value = TestJson.ReadValue(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_Number_KeepsTextAsTransferred()
    {
        // Arrange
        const string json = """{"N":"1.50"}""";

        // Act
        var value = TestJson.ReadValue(json);

        // Assert
        await Assert.That(value.AsNumber()).IsEqualTo("1.50");
    }

    [Test]
    [Arguments("""{"SS":[]}""")]
    [Arguments("""{"NS":[]}""")]
    [Arguments("""{"BS":[]}""")]
    public async Task Read_EmptySet_ThrowsJsonException(string json)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => TestJson.ReadValue(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("A DynamoDB set must contain at least one element.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_MapWithDuplicateName_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"M":{"a":{"S":"1"},"a":{"S":"2"}}}""";

        // Act
        var act = () => TestJson.ReadValue(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The attribute name \"a\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(AttributeType.None)]
    [Arguments((AttributeType)99)]
    public async Task Read_NotAnAttributeType_ThrowsArgumentOutOfRangeException(AttributeType type)
    {
        // Arrange
        const string json = """{"S":"x"}""";

        // Act
        var act = () => TestJson.Read(json, (ref r) =>
        {
            r.ReadStartAttribute();
            return AttributeValueConverter.Read(ref r, type);
        });

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>()
            .WithMessageContaining("Not a DynamoDB attribute type.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_DefaultValue_ThrowsArgumentException()
    {
        // Arrange
        var value = default(AttributeValue);

        // Act
        var act = () => TestJson.WriteValue(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>()
            .WithMessageContaining("A default AttributeValue has no type and cannot be sent to DynamoDB.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_ThroughConverterInterface_WritesValue()
    {
        // Arrange
        var value = AttributeValue.FromNumber(7);

        // Act
        var json = WriteWith<AttributeValueConverter, AttributeValue>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"7"}""");
    }

    [Test]
    public async Task WriteItem_Dictionary_WritesAttributesWithoutTypeDescriptor()
    {
        // Arrange
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = "USER#42",
            ["age"] = AttributeValue.FromNumber(35),
            ["roles"] = AttributeValue.FromStringSet(["admin"]),
        };

        // Act
        var json = TestJson.WriteItem(item);

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"USER#42"},"age":{"N":"35"},"roles":{"SS":["admin"]}}""");
    }

    [Test]
    public async Task WriteItem_OtherReadOnlyDictionary_WritesAttributes()
    {
        // Arrange
        var item = new ReadOnlyDictionary<string, AttributeValue>(new Dictionary<string, AttributeValue>
        {
            ["pk"] = "USER#42",
            ["active"] = AttributeValue.FromBool(true),
        });

        // Act
        var json = TestJson.WriteItem(item);

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"USER#42"},"active":{"BOOL":true}}""");
    }

    [Test]
    public async Task WriteItem_NoAttributes_WritesEmptyObject()
    {
        // Arrange
        var item = new Dictionary<string, AttributeValue>();

        // Act
        var json = TestJson.WriteItem(item);

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    public async Task ReadItem_UpdateItemSample_ReturnsAttributes()
    {
        // Arrange: "Attributes" of the UpdateItem example in the DynamoDB API reference (botocore examples-1.json).
        const string json = """
            {
              "AlbumTitle": {"S": "Louder Than Ever"},
              "Artist": {"S": "Acme Band"},
              "SongTitle": {"S": "Happy Day"},
              "Year": {"N": "2015"}
            }
            """;
        var expected = AttributeValue.FromMap([
            new("AlbumTitle", "Louder Than Ever"),
            new("Artist", "Acme Band"),
            new("SongTitle", "Happy Day"),
            new("Year", AttributeValue.FromNumber(2015)),
        ]);

        // Act
        var item = TestJson.ReadItem(json);

        // Assert
        await Assert.That(AttributeValue.FromMap(item)).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadItem_ScanSampleItems_ReadsEveryItemOfTheArray()
    {
        // Arrange: Scan response from the DynamoDB API reference (botocore examples-1.json).
        const string json = """
            {
              "ConsumedCapacity": {},
              "Count": 2,
              "Items": [
                {"AlbumTitle": {"S": "Somewhat Famous"}, "SongTitle": {"S": "Call Me Today"}},
                {"AlbumTitle": {"S": "Blue Sky Blues"}, "SongTitle": {"S": "Scared of My Shadow"}}
              ],
              "ScannedCount": 3
            }
            """;

        // Act
        var titles = TestJson.Read(json, static (ref r) =>
        {
            var result = new List<string>();
            r.Json.Read();
            while (r.Json.Read() && !r.Json.ValueTextEquals("Items"u8))
            {
                r.Json.Skip();
            }

            r.Json.Read();
            var lookahead = r.Json;
            while (lookahead.Read() && lookahead.TokenType == JsonTokenType.StartObject)
            {
                result.Add(AttributeValueConverter.ReadItem(ref r)["SongTitle"].AsString());
                lookahead = r.Json;
            }

            return result;
        });

        // Assert
        await Assert.That(string.Join('|', titles)).IsEqualTo("Call Me Today|Scared of My Shadow");
    }

    [Test]
    public async Task ReadAttributes_ItemsOfAnArray_ReadsEveryItemOnTheOneReader()
    {
        // Arrange: the Items array of the Scan sample above, walked without a copy of the reader.
        const string json = """
            [
              {"AlbumTitle": {"S": "Somewhat Famous"}, "SongTitle": {"S": "Call Me Today"}},
              {"AlbumTitle": {"S": "Blue Sky Blues"}, "SongTitle": {"S": "Scared of My Shadow"}}
            ]
            """;

        // Act
        var titles = TestJson.Read(json, static (ref r) =>
        {
            var result = new List<string>();
            r.Advance();
            while (r.TryReadStartItem())
            {
                result.Add(AttributeValueConverter.ReadAttributes(ref r)["SongTitle"].AsString());
            }

            return result;
        });

        // Assert
        await Assert.That(string.Join('|', titles)).IsEqualTo("Call Me Today|Scared of My Shadow");
    }

    [Test]
    public async Task ReadAttributes_EmptyItemInAnArray_ReturnsNoAttributes()
    {
        // Arrange
        const string json = "[{}]";

        // Act
        var count = TestJson.Read(json, static (ref r) =>
        {
            r.Advance();
            return r.TryReadStartItem() ? AttributeValueConverter.ReadAttributes(ref r).Count : -1;
        });

        // Assert
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadItem_NoAttributes_ReturnsEmptyDictionary()
    {
        // Arrange
        const string json = "{}";

        // Act
        var item = TestJson.ReadItem(json);

        // Assert
        await Assert.That(item.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadItem_DuplicateName_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"pk":{"S":"1"},"pk":{"N":"2"}}""";

        // Act
        var act = () => TestJson.ReadItem(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The attribute name \"pk\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItem_MapsNestedThirtyTwoLevels_ReadsAllLevels()
    {
        // Arrange: DynamoDB allows 32 levels of nesting; each takes two JSON levels.
        AttributeValue nested = "leaf";
        for (var i = 0; i < 32; i++)
        {
            nested = AttributeValue.FromMap([new("level", nested)]);
        }

        var json = TestJson.WriteItem(new Dictionary<string, AttributeValue> { ["root"] = nested });

        // Act
        var item = TestJson.ReadItem(json);

        // Assert
        await Assert.That(item["root"]).IsEqualTo(nested);
    }

    [Test]
    public async Task ReadItem_WrittenItem_ReturnsEqualItem()
    {
        // Arrange
        var item = EveryType().Select((value, i) => KeyValuePair.Create($"a{i}", value)).ToDictionary();
        var json = TestJson.WriteItem(item);

        // Act
        var read = TestJson.ReadItem(json);

        // Assert
        await Assert.That(AttributeValue.FromMap(read)).IsEqualTo(AttributeValue.FromMap(item));
    }

    [Test]
    [Arguments("""{"a":{"L":[{"S":"x"}]}}""", 1)]
    [Arguments("""{"a":{"L":[]}}""", 1)]
    [Arguments("""{"a":{"L":[{"L":[{"S":"x"}]},{"M":{"b":{"L":[]}}}]}}""", 3)]
    public async Task ReadItem_Lists_ReturnsEveryRentedArrayOfTheValuesPool(string json, int lists)
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        TestJson.Read(json, (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));

        // Assert
        await Assert.That(Describe(pools.Values)).IsEqualTo($"{lists} rented, {lists} returned");
    }

    [Test]
    [Arguments("""{"a":{"SS":["x","y"]}}""", 1)]
    [Arguments("""{"a":{"NS":["1"]}}""", 1)]
    [Arguments("""{"a":{"SS":["x"]},"b":{"NS":["1"]},"c":{"L":[{"SS":["z"]}]}}""", 3)]
    public async Task ReadItem_StringAndNumberSets_ReturnsEveryRentedArrayOfTheStringsPool(string json, int sets)
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        TestJson.Read(json, (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));

        // Assert
        await Assert.That(Describe(pools.Strings)).IsEqualTo($"{sets} rented, {sets} returned");
    }

    [Test]
    public async Task ReadItem_BinarySet_ReturnsTheRentedArrayOfTheBinariesPool()
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        TestJson.Read("""{"a":{"BS":["AA==","AQ=="]}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));

        // Assert
        await Assert.That(Describe(pools.Binaries)).IsEqualTo("1 rented, 1 returned");
    }

    [Test]
    public async Task ReadItem_ItemWithoutListsOrSets_RentsNothing()
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        TestJson.Read("""{"a":{"S":"x"},"b":{"M":{"c":{"N":"1"}}}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));

        // Assert
        await Assert.That(pools.Values.RentedLengths.Count + pools.Strings.RentedLengths.Count + pools.Binaries.RentedLengths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadItem_ListWithUnknownElementType_StillReturnsTheRentedArray()
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        var act = () => TestJson.Read("""{"a":{"L":[{"S":"x"},{"Q":"y"}]}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));
        await Assert.That(act).Throws<Exception>();

        // Assert
        await Assert.That(Describe(pools.Values)).IsEqualTo("1 rented, 1 returned");
    }

    [Test]
    public async Task ReadItem_EmptyStringSet_StillReturnsTheRentedArray()
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        var act = () => TestJson.Read("""{"a":{"SS":[]}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));
        await Assert.That(act).ThrowsExactly<JsonException>();

        // Assert
        await Assert.That(Describe(pools.Strings)).IsEqualTo("1 rented, 1 returned");
    }

    [Test]
    public async Task ReadItem_EmptyBinarySet_StillReturnsTheRentedArray()
    {
        // Arrange
        var pools = CountingPools.Create();

        // Act
        var act = () => TestJson.Read("""{"a":{"BS":[]}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));
        await Assert.That(act).ThrowsExactly<JsonException>();

        // Assert
        await Assert.That(Describe(pools.Binaries)).IsEqualTo("1 rented, 1 returned");
    }

    [Test]
    public async Task ReadItem_ListOfStrings_ClearsTheReferencesBeforeReturningTheArray()
    {
        // Arrange (a pooled array that keeps its elements would keep their values alive)
        var pools = CountingPools.Create();

        // Act
        TestJson.Read("""{"a":{"L":[{"S":"x"},{"S":"y"}]}}""", (ref r) => AttributeValueConverter.ReadItem(ref r, pools.Pools));

        // Assert
        await Assert.That(pools.Values.ReturnedUncleared).IsFalse();
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(16, 0)]
    [Arguments(17, 1)]
    [Arguments(33, 2)]
    [Arguments(65, 3)]
    public async Task ReadItem_AttributesBeyondTheStackBuffer_RentsAndReturnsArraysCleared(int attributes, int rented)
    {
        // Arrange (16 attributes fit on the stack, then the builders double)
        var pools = TestPools.Create();
        var json = "{" + string.Join(',', Enumerable.Range(0, attributes).Select(static i => $"\"a{i}\":{{\"N\":\"{i}\"}}")) + "}";

        // Act
        var item = pools.Read(json);

        // Assert
        await Assert.That((item.Count, pools.Strings.Returned, pools.Values.Returned, pools.Outstanding, pools.Strings.ReturnedUncleared, pools.Values.ReturnedUncleared)).IsEqualTo((attributes, rented, rented, 0, false, false));
    }

    [Test]
    public async Task ReadItem_NestedMapsBeyondTheStackBuffer_ReturnsTheArraysOfEveryLevel()
    {
        // Arrange
        var pools = TestPools.Create();
        var inner = "{" + string.Join(',', Enumerable.Range(0, 20).Select(static i => $"\"a{i}\":{{\"N\":\"{i}\"}}")) + "}";
        var json = "{" + string.Join(',', Enumerable.Range(0, 20).Select(i => $"\"b{i}\":{{\"M\":{inner}}}")) + "}";

        // Act
        var item = pools.Read(json);

        // Assert
        await Assert.That((item.GetMap("b19")!["a19"].AsNumber(), pools.Strings.Returned, pools.Values.Returned, pools.Outstanding)).IsEqualTo(("19", 21, 21, 0));
    }

    [Test]
    public async Task ReadItem_NestedMapsAndTopLevel_ShareTheTableOfThePools()
    {
        // Arrange (nested maps with the same names as an earlier item get its shape, from any depth)
        var pools = TestPools.Create();

        // Act
        var item = pools.Read("""{"m":{"M":{"a":{"N":"1"}}},"l":{"L":[{"M":{"a":{"N":"2"}}}]}}""");

        // Assert
        await Assert.That(ReferenceEquals(item.GetMap("m")!.Shape, ((Item)item.GetList("l")![0].AsMap()).Shape)).IsTrue();
    }

    [Test]
    public async Task ReadItem_ItemWithAMap_OnlyTheMapRefusesWrites()
    {
        // Arrange
        var item = TestJson.ReadItem("""{"m":{"M":{"a":{"N":"1"}}}}""");
        var map = item.GetMap("m")!;

        // Act
        item["b"] = 1;
        Action act = () => map["b"] = 1;

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task WriteItem_ReadItem_WritesItsAttributes()
    {
        // Arrange
        const string json = """{"a":{"S":"x"},"m":{"M":{"b":{"N":"1"}}}}""";

        // Act
        var written = TestJson.WriteItem(TestJson.ReadItem(json));

        // Assert
        await Assert.That(written).IsEqualTo(json);
    }

    [Test]
    public async Task WriteItem_HandBuiltItem_WritesItsAttributesInOrder()
    {
        // Arrange
        var item = new Item { ["b"] = 1, ["a"] = "x" };

        // Act
        var written = TestJson.WriteItem(item);

        // Assert
        await Assert.That(written).IsEqualTo("""{"b":{"N":"1"},"a":{"S":"x"}}""");
    }

    private static string WriteWith<TConverter, T>(T value)
        where TConverter : IDynamoDbConverter<T>
        => TestJson.Write(w => TConverter.Write(w, value));

    private static List<AttributeValue> EveryType() =>
    [
        "s",
        AttributeValue.FromNumber("-1.5E-10"),
        AttributeValue.FromBinary([1, 2, 3]),
        AttributeValue.FromBool(true),
        AttributeValue.Null,
        AttributeValue.FromList(["nested", AttributeValue.Null]),
        AttributeValue.FromMap([new("inner", AttributeValue.FromNumberSet(["1"]))]),
        AttributeValue.FromStringSet(["a", "b"]),
        AttributeValue.FromNumberSet(["1", "2"]),
        AttributeValue.FromBinarySet([[1], [2]]),
    ];

    private static byte[] AllBytes() => [.. Enumerable.Range(0, 256).Select(i => (byte)i)];

    // The documentation samples contain no spaces inside strings.
    private static string Minify(string json) => json.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string Describe<T>(CountingArrayPool<T> pool) =>
        $"{pool.RentedLengths.Count} rented, {pool.Returned} returned";

    private sealed record CountingPools(
        CountingArrayPool<AttributeValue> Values,
        CountingArrayPool<string> Strings,
        CountingArrayPool<ReadOnlyMemory<byte>> Binaries)
    {
        public BuilderPools Pools => new(Values, Strings, Binaries, new NameTable(64), new ShapeTable(64));

        public static CountingPools Create() => new(new(), new(), new());
    }
}
