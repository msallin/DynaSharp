using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ListConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotLists() => AttributeSamples.Except(AttributeType.List);

    [Test]
    public async Task Write_Elements_WritesAListOfValues()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<List<string>, ListConverter<string, StringConverter>>(["a", string.Empty, "c"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"S":"a"},{"S":""},{"S":"c"}]}""");
    }

    [Test]
    public async Task Write_NoElements_WritesAnEmptyList()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<List<string>, ListConverter<string, StringConverter>>([]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[]}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        List<string> value = null!;

        // Act
        var act = () => ConverterRun.Write<List<string>, ListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Write_NullElement_ThrowsArgumentNullException()
    {
        // Arrange (a string element cannot be null; the list of nullable strings has its own converter)
        List<string> value = ["a", null!];

        // Act
        var act = () => ConverterRun.Write<List<string>, ListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Read_ListOfStrings_ReturnsTheElementsInOrder()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<List<string>, ListConverter<string, StringConverter>>("""{"L":[{"S":"c"},{"S":"a"}]}""");

        // Assert
        await Assert.That(string.Join(',', value)).IsEqualTo("c,a");
    }

    [Test]
    public async Task Read_ListOfThree_HasExactlyTheCapacityOfItsElements()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<List<int>, ListConverter<int, NumberConverter<int>>>(ArrayConverterTests.ListOfNumbers(3));

        // Assert
        await Assert.That((value.Count, value.Capacity)).IsEqualTo((3, 3));
    }

    [Test]
    public async Task Read_EmptyList_ReturnsAnEmptyList()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<List<int>, ListConverter<int, NumberConverter<int>>>("""{"L":[]}""");

        // Assert
        await Assert.That((value.Count, value.Capacity)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Read_ListOfMaps_ReturnsTheMaps()
    {
        // Arrange
        const string json = """{"L":[{"M":{"x":{"N":"1"}}},{"M":{"x":{"N":"2"},"y":{"N":"3"}}}]}""";

        // Act
        var value = ConverterRun.Read<List<Dictionary<string, int>>, ListConverter<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>>(json);

        // Assert
        await Assert.That(string.Join('|', value.Select(static map => string.Join(',', map.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value}")))))
            .IsEqualTo("x=1|x=2,y=3");
    }

    [Test]
    public async Task Read_ListOfNumbers_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();

        // Act
        var value = ReadWithPool(ArrayConverterTests.ListOfNumbers(40), pool);

        // Assert
        await Assert.That((value.Count, string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding, pool.ReturnedUncleared))
            .IsEqualTo((40, "16,32,64", 3, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();
        const string json = """{"L":[{"N":"1"},{"S":"x"}]}""";

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    [MethodDataSource(nameof(NotLists))]
    public async Task Read_OtherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
    {
        // Arrange
        var pool = new CountingArrayPool<int>();

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.List, found), StringComparison.Ordinal);
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    private static List<int> ReadWithPool(string json, CountingArrayPool<int> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = ListConverter<int, NumberConverter<int>>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
