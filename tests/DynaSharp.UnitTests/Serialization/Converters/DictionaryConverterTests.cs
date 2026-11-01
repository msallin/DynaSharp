using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DictionaryConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotMaps() => AttributeSamples.Except(AttributeType.Map);

    [Test]
    public async Task Write_Entries_WritesAMapOfValues()
    {
        // Arrange
        var value = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        // Act
        var json = ConverterRun.Write<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>(value);

        // Assert (a map has no order)
        await Assert.That(JsonComparer.Differences("""{"M":{"a":{"N":"1"},"b":{"N":"2"}}}""", json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Write_NoEntries_WritesAnEmptyMap()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>([]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{}}""");
    }

    [Test]
    public async Task Write_EmptyAndUnusualNames_WritesThemAsAttributeNames()
    {
        // Arrange: DynamoDB accepts an empty name inside a map
        var value = new Dictionary<string, string> { [string.Empty] = "x" };

        // Act
        var json = ConverterRun.Write<Dictionary<string, string>, DictionaryConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{"":{"S":"x"}}}""");
    }

    [Test]
    public async Task Write_NestedMaps_WritesMapsInMaps()
    {
        // Arrange
        var value = new Dictionary<string, Dictionary<string, bool>> { ["outer"] = new() { ["inner"] = true } };

        // Act
        var json = ConverterRun.Write<Dictionary<string, Dictionary<string, bool>>, DictionaryConverter<Dictionary<string, bool>, DictionaryConverter<bool, BoolConverter>>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{"outer":{"M":{"inner":{"BOOL":true}}}}}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        Dictionary<string, int> value = null!;

        // Act
        var act = () => ConverterRun.Write<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_MapOfNumbers_ReturnsTheEntries()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>("""{"M":{"a":{"N":"1"},"b":{"N":"2"}}}""");

        // Assert
        await Assert.That(string.Join(',', value.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))).IsEqualTo("a=1,b=2");
    }

    [Test]
    public async Task Read_EmptyMap_ReturnsAnEmptyDictionary()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>("""{"M":{}}""");

        // Assert
        await Assert.That(value.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_EscapedName_ReturnsTheUnescapedName()
    {
        // Arrange: the name "ab" with its first character escaped, built from parts so that no tool turns the escape into a character
        var json = "{\"M\":{\"\\u0061b\":{\"N\":\"1\"}}}";

        // Act
        var value = ConverterRun.Read<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>(json);

        // Assert
        await Assert.That(value["ab"]).IsEqualTo(1);
    }

    [Test]
    public async Task Read_NameThatAppearsTwice_KeepsTheLastValue()
    {
        // Arrange (DynamoDB never sends this)
        // Act
        var value = ConverterRun.Read<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>("""{"M":{"a":{"N":"1"},"a":{"N":"2"}}}""");

        // Assert
        await Assert.That((value.Count, value["a"])).IsEqualTo((1, 2));
    }

    [Test]
    public async Task Read_MapsInMaps_ReturnsNestedDictionaries()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<Dictionary<string, Dictionary<string, bool>>, DictionaryConverter<Dictionary<string, bool>, DictionaryConverter<bool, BoolConverter>>>(
            """{"M":{"outer":{"M":{"inner":{"BOOL":true}}}}}""");

        // Assert
        await Assert.That(value["outer"]["inner"]).IsTrue();
    }

    [Test]
    public async Task Read_MapOfSeventeenEntries_ReturnsEveryRentedArrayToTheirPools()
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<int>();
        var json = "{\"M\":{" + string.Join(',', Enumerable.Range(0, 17).Select(static i => $"\"k{i}\":{{\"N\":\"{i}\"}}")) + "}}";

        // Act
        var value = ReadWithPools(json, names, values);

        // Assert
        await Assert.That((value.Count, names.Returned, names.Outstanding, names.ReturnedUncleared, values.Returned, values.Outstanding))
            .IsEqualTo((17, 2, 0, false, 2, 0));
    }

    [Test]
    public async Task Read_ValueFailsInTheMiddle_ReturnsEveryRentedArrayAndThrows()
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<int>();
        const string json = """{"M":{"a":{"N":"1"},"b":{"S":"x"}}}""";

        // Act
        var act = () => ReadWithPools(json, names, values);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, AttributeType.String), StringComparison.Ordinal);
        await Assert.That((names.Returned, names.Outstanding, values.Returned, values.Outstanding)).IsEqualTo((1, 0, 1, 0));
    }

    [Test]
    [MethodDataSource(nameof(NotMaps))]
    public async Task Read_OtherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<int>();

        // Act
        var act = () => ReadWithPools(json, names, values);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Map, found), StringComparison.Ordinal);
        await Assert.That((names.RentedLengths.Count, values.RentedLengths.Count)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Read_RealMapOfSiblings_ReturnsTheStringsByName()
    {
        // Arrange (a recording from real DynamoDB: a nested map with two sibling leaves)
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("proj_get_a1").Wire, "a");

        // Act
        var value = ConverterRun.Read<Dictionary<string, string>, DictionaryConverter<string, StringConverter>>(attribute);

        // Assert
        await Assert.That((value.Count, value["b"], value["c"])).IsEqualTo((2, "bb", "cc"));
    }

    [Test]
    public async Task Read_RealListOfMapsWithDifferentKeyOrders_ReturnsEachMapByName()
    {
        // Arrange (a recording from real DynamoDB: the second map lists y before x)
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("proj_get_a3").Wire, "l");

        // Act
        var value = ConverterRun.Read<List<Dictionary<string, string>>, ListConverter<Dictionary<string, string>, DictionaryConverter<string, StringConverter>>>(attribute);

        // Assert
        await Assert.That((value[0]["x"], value[0]["y"], value[1]["x"], value[1]["y"])).IsEqualTo(("x0", "y0", "x1", "y1"));
    }

    [Test]
    public async Task Read_RealMapWithANestedMap_ReturnsTheNestedValues()
    {
        // Arrange (a recording from real DynamoDB: strings b and c and a map d beside them)
        var attribute = ConverterRun.Attribute(RealAwsResponses.Get("proj_get_a4").Wire, "a");

        // Act
        var value = ConverterRun.Read<Dictionary<string, AttributeValue>, DictionaryConverter<AttributeValue, AttributeValueConverter>>(attribute);

        // Assert
        await Assert.That((value["b"].AsString(), value["c"].AsString(), value["d"].AsMap()["e"].AsString())).IsEqualTo(("bb", "cc", "ee"));
    }

    [Test]
    public async Task Read_RealItemOfAGetItemResponse_MatchesWhatTheResponseReaderReturns()
    {
        // Arrange (cross-check against the existing untyped path on the same recording)
        var wire = RealAwsResponses.Get("proj_get_a1").Wire;
        var untyped = TestJson.Read(wire, static (ref reader) => GetItemOperation.ReadResponse(ref reader)).Item!["a"];

        // Act
        var typed = ConverterRun.Read<AttributeValue, AttributeValueConverter>(ConverterRun.Attribute(wire, "a"));

        // Assert
        await Assert.That(typed).IsEqualTo(untyped);
    }

    private static Dictionary<string, int> ReadWithPools(string json, CountingArrayPool<string> names, CountingArrayPool<int> values) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var entries = DictionaryConverter<int, NumberConverter<int>>.Read(ref reader, type, names, values);
            reader.ReadEndAttribute();
            return entries;
        });
}
