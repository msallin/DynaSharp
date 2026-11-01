using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ArrayConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotLists() => AttributeSamples.Except(AttributeType.List);

    [Test]
    public async Task Write_Elements_WritesAListOfValues()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<int[], ArrayConverter<int, NumberConverter<int>>>([1, 2, 3]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"N":"1"},{"N":"2"},{"N":"3"}]}""");
    }

    [Test]
    public async Task Write_NoElements_WritesAnEmptyList()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<int[], ArrayConverter<int, NumberConverter<int>>>([]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[]}""");
    }

    [Test]
    public async Task Write_NullElement_WritesTheNullValueForIt()
    {
        // Arrange
        int?[] elements = [1, null, 3];

        // Act
        var json = ConverterRun.Write<int?[], ArrayConverter<int?, NullableConverter<int, NumberConverter<int>>>>(elements);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"N":"1"},{"NULL":true},{"N":"3"}]}""");
    }

    [Test]
    public async Task Write_NestedArrays_WritesListsInLists()
    {
        // Arrange
        int[][] elements = [[1], [], [2, 3]];

        // Act
        var json = ConverterRun.Write<int[][], ArrayConverter<int[], ArrayConverter<int, NumberConverter<int>>>>(elements);

        // Assert
        await Assert.That(json).IsEqualTo("""{"L":[{"L":[{"N":"1"}]},{"L":[]},{"L":[{"N":"2"},{"N":"3"}]}]}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        int[] value = null!;

        // Act
        var act = () => ConverterRun.Write<int[], ArrayConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_ListOfNumbers_ReturnsTheElementsInOrder()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int[], ArrayConverter<int, NumberConverter<int>>>("""{"L":[{"N":"3"},{"N":"1"},{"N":"2"}]}""");

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual([3, 1, 2])).IsTrue();
    }

    [Test]
    public async Task Read_EmptyList_ReturnsAnEmptyArray()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<string[], ArrayConverter<string, StringConverter>>("""{"L":[]}""");

        // Assert
        await Assert.That(value.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Read_ListWithNullElement_ReturnsNullForIt()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int?[], ArrayConverter<int?, NullableConverter<int, NumberConverter<int>>>>("""{"L":[{"N":"1"},{"NULL":true},{"N":"3"}]}""");

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual([1, null, 3])).IsTrue();
    }

    [Test]
    public async Task Read_NestedLists_ReturnsListsInArrays()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int[][], ArrayConverter<int[], ArrayConverter<int, NumberConverter<int>>>>("""{"L":[{"L":[{"N":"1"}]},{"L":[]},{"L":[{"N":"2"},{"N":"3"}]}]}""");

        // Assert
        await Assert.That(string.Join('|', value.Select(static inner => string.Join(',', inner)))).IsEqualTo("1||2,3");
    }

    [Test]
    public async Task Read_MoreElementsThanTheBuilderStartsWith_ReturnsAllAtTheirExactCount()
    {
        // Arrange
        var json = ListOfNumbers(40);

        // Act
        var value = ConverterRun.Read<int[], ArrayConverter<int, NumberConverter<int>>>(json);

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual(Enumerable.Range(0, 40).ToArray())).IsTrue();
    }

    [Test]
    public async Task Read_ListOfNumbers_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();

        // Act
        var value = ReadWithPool(ListOfNumbers(40), pool);

        // Assert: the builder starts with 16 elements and doubles, so 16, 32 and 64 are rented; all come back, cleared
        await Assert.That((value.Length, string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding, pool.ReturnedUncleared))
            .IsEqualTo((40, "16,32,64", 3, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();
        const string json = """{"L":[{"N":"1"},{"S":"x"},{"N":"3"}]}""";

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, AttributeType.String), StringComparison.Ordinal);
        await Assert.That((pool.Returned, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo((1, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsAfterTheBuilderGrew_ReturnsEveryRentedArrayAndThrows()
    {
        // Arrange: the 20th element is a string, after the builder moved from 16 to 32 elements
        var pool = new CountingArrayPool<int>();
        var json = ListOfNumbers(19).Replace("]}", ",{\"S\":\"x\"}]}", StringComparison.Ordinal);

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task Read_StringElements_ReturnsRentedArraysClearedOfReferences()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var value = TestJson.Read("""{"L":[{"S":"a"},{"S":"b"}]}""", (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = ArrayConverter<string, StringConverter>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });

        // Assert
        await Assert.That((string.Join(',', value), pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo(("a,b", 0, false));
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

    [Test]
    public async Task Read_ListFromTheSharedPool_ReturnsTheElements()
    {
        // Arrange (the public overload uses the shared pool; no assertion about the pool, which other tests share)
        var json = ListOfNumbers(3);

        // Act
        var value = ConverterRun.Read<int[], ArrayConverter<int, NumberConverter<int>>>(json);

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual([0, 1, 2])).IsTrue();
    }

    internal static string ListOfNumbers(int count) =>
        "{\"L\":[" + string.Join(',', Enumerable.Range(0, count).Select(static i => $"{{\"N\":\"{i}\"}}")) + "]}";

    private static int[] ReadWithPool(string json, CountingArrayPool<int> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = ArrayConverter<int, NumberConverter<int>>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
