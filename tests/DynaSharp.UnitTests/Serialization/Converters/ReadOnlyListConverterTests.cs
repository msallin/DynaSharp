using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ReadOnlyListConverterTests
{
    private const string Expected = """{"L":[{"N":"1"},{"N":"2"}]}""";

    public static IEnumerable<(string Json, AttributeType Found)> NotLists() => AttributeSamples.Except(AttributeType.List);

    [Test]
    public async Task Write_Array_WritesAListOfValues()
    {
        // Arrange
        int[] array = [1, 2];
        IReadOnlyList<int> value = array;

        // Act
        var json = ConverterRun.Write<IReadOnlyList<int>, ReadOnlyListConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_List_WritesAListOfValues()
    {
        // Arrange
        List<int> list = [1, 2];
        IReadOnlyList<int> value = list;

        // Act
        var json = ConverterRun.Write<IReadOnlyList<int>, ReadOnlyListConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_OtherList_WritesByIndexWithoutAskingForAnEnumerator()
    {
        // Arrange (the double throws if anything enumerates it, which would mean a boxed enumerator)
        IReadOnlyList<int> value = new NoEnumeratorList<int>(1, 2);

        // Act
        var json = ConverterRun.Write<IReadOnlyList<int>, ReadOnlyListConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<int> value = null!;

        // Act
        var act = () => ConverterRun.Write<IReadOnlyList<int>, ReadOnlyListConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_ListOfNumbers_ReturnsAnArrayOfExactlyTheElements()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<IReadOnlyList<int>, ReadOnlyListConverter<int, NumberConverter<int>>>(Expected);

        // Assert
        await Assert.That((value is int[] array ? array.Length : -1, value[0], value[1])).IsEqualTo((2, 1, 2));
    }

    [Test]
    public async Task Read_ListOfNumbers_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();

        // Act
        var value = ReadWithPool(ArrayConverterTests.ListOfNumbers(20), pool);

        // Assert
        await Assert.That((value.Count, pool.Returned, pool.Outstanding)).IsEqualTo((20, 2, 0));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<int>();
        const string json = """{"L":[{"N":"1"},{"BOOL":true}]}""";

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

    private static IReadOnlyList<int> ReadWithPool(string json, CountingArrayPool<int> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = ReadOnlyListConverter<int, NumberConverter<int>>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
