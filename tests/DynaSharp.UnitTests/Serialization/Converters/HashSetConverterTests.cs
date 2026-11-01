using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class HashSetConverterTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static IEnumerable<(string Json, AttributeType Found)> NotStringSets() => AttributeSamples.Except(AttributeType.StringSet);

    public static IEnumerable<(string Json, AttributeType Found)> NotNumberSets() => AttributeSamples.Except(AttributeType.NumberSet);

    public static IEnumerable<(string Json, AttributeType Found)> NotBinarySets() => AttributeSamples.Except(AttributeType.BinarySet);

    [Test]
    public async Task Write_StringSet_WritesAStringSet()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>(["a"]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a"]}""");
    }

    [Test]
    public async Task Write_StringSetOfSeveralElements_WritesEachElementOnce()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>(["b", string.Empty, "a"]);

        // Assert (a set has no order)
        await Assert.That(string.Join('|', ConverterRun.Members(json, "SS"))).IsEqualTo("|a|b");
    }

    [Test]
    public async Task Write_NumberSet_WritesANumberSet()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<decimal>, HashSetConverter<decimal, NumberSetElement<decimal>>>([1.5m]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"NS":["1.5"]}""");
    }

    [Test]
    public async Task Write_NumberSetOfSeveralElements_WritesEachNumber()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<int>, HashSetConverter<int, NumberSetElement<int>>>([int.MinValue, 0, int.MaxValue]);

        // Assert
        await Assert.That(string.Join('|', ConverterRun.Members(json, "NS"))).IsEqualTo("-2147483648|0|2147483647");
    }

    [Test]
    public async Task Write_BinarySet_WritesABinarySet()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>([[1, 2, 3]]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"BS":["AQID"]}""");
    }

    [Test]
    public async Task Write_BinarySetWithEmptyElement_WritesTheEmptyElement()
    {
        // Arrange (real DynamoDB accepts a zero-length member)
        // Act
        var json = ConverterRun.Write<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>([[]]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"BS":[""]}""");
    }

    [Test]
    public async Task Write_GuidSet_WritesAStringSetOfLowercaseGuids()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>([Sample]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e"]}""");
    }

    [Test]
    public async Task Write_EmptySet_WritesAnEmptySetThatDynamoDbWillReject()
    {
        // Arrange (the mapping code leaves an empty set out; the converter writes what it is given)
        // Act
        var json = ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>([]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":[]}""");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        HashSet<string> value = null!;

        // Act
        var act = () => ConverterRun.Write<HashSet<string>, HashSetConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Write_ElementConverterThatIsNotASetType_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<HashSet<int>, HashSetConverter<int, NotASetElement>>([1]);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("String is not a set type: a set element converter must return StringSet, NumberSet or BinarySet.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_StringSet_ReturnsTheElements()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>("""{"SS":["b","a"]}""");

        // Assert
        await Assert.That(string.Join('|', value.Order(StringComparer.Ordinal))).IsEqualTo("a|b");
    }

    [Test]
    public async Task Read_NumberSet_ReturnsTheNumbers()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<HashSet<decimal>, HashSetConverter<decimal, NumberSetElement<decimal>>>("""{"NS":["1.5","-2","1E+2"]}""");

        // Assert
        await Assert.That(string.Join('|', value.Order())).IsEqualTo("-2|1.5|100");
    }

    [Test]
    public async Task Read_BinarySet_ReturnsTheByteArrays()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>("""{"BS":["AQID","","//4="]}""");

        // Assert
        await Assert.That(string.Join('|', value.Select(static bytes => Convert.ToHexString(bytes)).Order(StringComparer.Ordinal))).IsEqualTo("|010203|FFFE");
    }

    [Test]
    public async Task Read_GuidSet_ReturnsTheGuids()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>("""{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e"]}""");

        // Assert
        await Assert.That(value.Single()).IsEqualTo(Sample);
    }

    [Test]
    public async Task Read_EmptyStringSet_ReturnsAnEmptySet()
    {
        // Arrange (DynamoDB never sends one; an empty set is not an error for the mapping code, which reads a missing set as empty)
        // Act
        var value = ConverterRun.Read<HashSet<string>, HashSetConverter<string, StringSetElement>>("""{"SS":[]}""");

        // Assert
        await Assert.That(value.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_StringSetWithMalformedGuid_ThrowsFormatException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<HashSet<Guid>, HashSetConverter<Guid, GuidSetElement>>("""{"SS":["nope"]}""");

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task Read_NumberSetWithNumberBeyondTheType_ThrowsOverflowException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<HashSet<byte>, HashSetConverter<byte, NumberSetElement<byte>>>("""{"NS":["1","256"]}""");

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Read_SetOfSixtyElements_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();
        var json = "{\"SS\":[" + string.Join(',', Enumerable.Range(0, 60).Select(static i => $"\"e{i}\"")) + "]}";

        // Act
        var value = ReadWithPool(json, pool);

        // Assert
        await Assert.That((value.Count, string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding, pool.ReturnedUncleared))
            .IsEqualTo((60, "16,32,64", 3, 0, false));
    }

    [Test]
    public async Task Read_ElementFailsInTheMiddle_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange: a JSON number where a set element belongs
        var pool = new CountingArrayPool<string>();
        const string json = """{"SS":["a",1,"c"]}""";

        // Act
        var act = () => ReadWithPool(json, pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task Read_ConverterFailsOnAnElement_ReturnsTheRentedArrayAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<Guid>();

        // Act
        var act = () => TestJson.Read("""{"SS":["0f8fad5b-d9cb-469f-a165-70867728950e","x"]}""", (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = HashSetConverter<Guid, GuidSetElement>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    [MethodDataSource(nameof(NotStringSets))]
    public async Task Read_StringSetConverterGivenAnotherType_ThrowsJsonExceptionAndRentsNothing(string json, AttributeType found)
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

    [Test]
    [MethodDataSource(nameof(NotNumberSets))]
    public async Task Read_NumberSetConverterGivenAnotherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<HashSet<int>, HashSetConverter<int, NumberSetElement<int>>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.NumberSet, found), StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotBinarySets))]
    public async Task Read_BinarySetConverterGivenAnotherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<HashSet<byte[]>, HashSetConverter<byte[], BinarySetElement>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.BinarySet, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_ElementConverterThatIsNotASetType_ThrowsJsonException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<HashSet<int>, HashSetConverter<int, NotASetElement>>("""{"NS":["1"]}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, AttributeType.NumberSet), StringComparison.Ordinal);
    }

    private static HashSet<string> ReadWithPool(string json, CountingArrayPool<string> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = HashSetConverter<string, StringSetElement>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
