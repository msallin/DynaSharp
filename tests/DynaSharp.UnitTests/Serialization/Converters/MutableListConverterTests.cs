using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class MutableListConverterTests
{
    private const string Expected = """{"L":[{"S":"a"},{"S":"b"}]}""";

    public static IEnumerable<(string Json, AttributeType Found)> NotLists() => AttributeSamples.Except(AttributeType.List);

    [Test]
    public async Task Write_Array_WritesAListOfValues()
    {
        // Arrange
        string[] array = ["a", "b"];
        IList<string> value = array;

        // Act
        var json = ConverterRun.Write<IList<string>, MutableListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_List_WritesAListOfValues()
    {
        // Arrange
        List<string> list = ["a", "b"];
        IList<string> value = list;

        // Act
        var json = ConverterRun.Write<IList<string>, MutableListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_OtherList_WritesByIndexWithoutAskingForAnEnumerator()
    {
        // Arrange
        IList<string> value = new NoEnumeratorList<string>("a", "b");

        // Act
        var json = ConverterRun.Write<IList<string>, MutableListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(Expected);
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IList<string> value = null!;

        // Act
        var act = () => ConverterRun.Write<IList<string>, MutableListConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_ListOfStrings_ReturnsAListThatCanGrow()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<IList<string>, MutableListConverter<string, StringConverter>>(Expected);
        value.Add("c");

        // Assert
        await Assert.That((value is List<string>, string.Join(',', value))).IsEqualTo((true, "a,b,c"));
    }

    [Test]
    public async Task Read_ListOfStrings_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var value = ReadWithPool(Expected, pool);

        // Assert
        await Assert.That((value.Count, pool.Returned, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo((2, 1, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        const string json = """{"L":[{"S":"a"},{"N":"1"}]}""";

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((pool.Returned, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo((1, 0, false));
    }

    [Test]
    [MethodDataSource(nameof(NotLists))]
    public async Task Read_OtherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.List, found), StringComparison.Ordinal);
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    private static IList<string> ReadWithPool(string json, CountingArrayPool<string> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = MutableListConverter<string, StringConverter>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
