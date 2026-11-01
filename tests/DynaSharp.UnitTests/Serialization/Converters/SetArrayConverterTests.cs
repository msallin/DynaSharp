using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class SetArrayConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStringSets() => AttributeSamples.Except(AttributeType.StringSet);

    [Test]
    public async Task Write_StringArray_WritesAStringSet()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<string[], SetArrayConverter<string, StringSetElement>>(["a"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a"]}""");
    }

    [Test]
    public async Task Write_NumberArray_WritesANumberSetInArrayOrder()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<int[], SetArrayConverter<int, NumberSetElement<int>>>([3, 1, 2]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"NS":["3","1","2"]}""");
    }

    [Test]
    public async Task Write_BinaryArray_WritesABinarySet()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<byte[][], SetArrayConverter<byte[], BinarySetElement>>([[1, 2, 3], []]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"BS":["AQID",""]}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string[] value = null!;

        // Act
        var act = () => ConverterRun.Write<string[], SetArrayConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Write_ElementConverterThatIsNotASetType_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<int[], SetArrayConverter<int, NotASetElement>>([1]);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Read_StringSet_ReturnsAnArrayOfExactlyTheElementsInWireOrder()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<string[], SetArrayConverter<string, StringSetElement>>("""{"SS":["b","a","c"]}""");

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual(["b", "a", "c"])).IsTrue();
    }

    [Test]
    public async Task Read_NumberSet_ReturnsTheNumbers()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<decimal[], SetArrayConverter<decimal, NumberSetElement<decimal>>>("""{"NS":["1.5","2"]}""");

        // Assert
        await Assert.That(value.AsSpan().SequenceEqual([1.5m, 2m])).IsTrue();
    }

    [Test]
    public async Task Read_StringSet_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var value = ReadWithPool("""{"SS":["a","b"]}""", pool);

        // Assert
        await Assert.That((value.Length, pool.Returned, pool.Outstanding, pool.ReturnedUncleared)).IsEqualTo((2, 1, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        const string json = """{"SS":["a",true]}""";

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    [MethodDataSource(nameof(NotStringSets))]
    public async Task Read_OtherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.StringSet, found), StringComparison.Ordinal);
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    private static string[] ReadWithPool(string json, CountingArrayPool<string> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = SetArrayConverter<string, StringSetElement>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
