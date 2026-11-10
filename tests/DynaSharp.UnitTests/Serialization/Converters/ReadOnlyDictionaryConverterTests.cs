using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ReadOnlyDictionaryConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotMaps() => AttributeSamples.Except(AttributeType.Map);

    [Test]
    public async Task Write_Dictionary_WritesAMapOfValues()
    {
        // Arrange
        IReadOnlyDictionary<string, string> value = new Dictionary<string, string> { ["a"] = "x", ["b"] = "y" };

        // Act
        var json = ConverterRun.Write<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(JsonComparer.Differences("""{"M":{"a":{"S":"x"},"b":{"S":"y"}}}""", json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Write_OtherDictionaryWithAnEmptyName_ThrowsTheRefusalOfAnEmptyName()
    {
        // Arrange
        IReadOnlyDictionary<string, string> value = new ForeignReadOnlyDictionary<string>(new Dictionary<string, string> { [string.Empty] = "x" });

        // Act
        var failure = TestClient.Catch(() => ConverterRun.Write<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>(value));

        // Assert
        await Assert.That(failure is ArgumentException refused && ConverterChecks.IsRefusedValue(refused)).IsTrue();
    }

    [Test]
    public async Task Write_OtherDictionary_WritesAMapOfValues()
    {
        // Arrange
        IReadOnlyDictionary<string, string> value = new ForeignReadOnlyDictionary<string>(new Dictionary<string, string> { ["a"] = "x" });

        // Act
        var json = ConverterRun.Write<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"M":{"a":{"S":"x"}}}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyDictionary<string, string> value = null!;

        // Act
        var act = () => ConverterRun.Write<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_MapOfStrings_ReturnsADictionary()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<IReadOnlyDictionary<string, string>, ReadOnlyDictionaryConverter<string, StringConverter>>("""{"M":{"a":{"S":"x"}}}""");

        // Assert
        await Assert.That((value is Dictionary<string, string>, value["a"], value.Count)).IsEqualTo((true, "x", 1));
    }

    [Test]
    public async Task Read_MapOfStrings_ReturnsEveryRentedArrayToTheirPools()
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<string>();

        // Act
        var value = ReadWithPools("""{"M":{"a":{"S":"x"},"b":{"S":"y"}}}""", names, values);

        // Assert
        await Assert.That((value.Count, names.Returned, names.Outstanding, values.Returned, values.Outstanding, values.ReturnedUncleared))
            .IsEqualTo((2, 1, 0, 1, 0, false));
    }

    [Test]
    public async Task Read_ValueFailsInTheMiddle_ReturnsEveryRentedArrayAndThrows()
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<string>();
        const string json = """{"M":{"a":{"S":"x"},"b":{"N":"1"}}}""";

        // Act
        var act = () => ReadWithPools(json, names, values);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((names.Outstanding, values.Outstanding)).IsEqualTo((0, 0));
    }

    [Test]
    [MethodDataSource(nameof(NotMaps))]
    public async Task Read_OtherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
    {
        // Arrange
        var names = new CountingArrayPool<string>();
        var values = new CountingArrayPool<string>();

        // Act
        var act = () => ReadWithPools(json, names, values);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Map, found), StringComparison.Ordinal);
        await Assert.That((names.RentedLengths.Count, values.RentedLengths.Count)).IsEqualTo((0, 0));
    }

    private static IReadOnlyDictionary<string, string> ReadWithPools(string json, CountingArrayPool<string> names, CountingArrayPool<string> values) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var entries = ReadOnlyDictionaryConverter<string, StringConverter>.Read(ref reader, type, names, values);
            reader.ReadEndAttribute();
            return entries;
        });
}
