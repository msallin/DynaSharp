using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ReadOnlySetConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStringSets() => AttributeSamples.Except(AttributeType.StringSet);

    [Test]
    public async Task Write_HashSet_WritesAStringSet()
    {
        // Arrange
        IReadOnlySet<string> value = new HashSet<string> { "a" };

        // Act
        var json = ConverterRun.Write<IReadOnlySet<string>, ReadOnlySetConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"SS":["a"]}""");
    }

    [Test]
    public async Task Write_OtherSet_WritesAStringSet()
    {
        // Arrange
        IReadOnlySet<string> value = new ForeignReadOnlySet<string>("a", "b");

        // Act
        var json = ConverterRun.Write<IReadOnlySet<string>, ReadOnlySetConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(string.Join('|', ConverterRun.Members(json, "SS"))).IsEqualTo("a|b");
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlySet<string> value = null!;

        // Act
        var act = () => ConverterRun.Write<IReadOnlySet<string>, ReadOnlySetConverter<string, StringSetElement>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public async Task Write_ElementConverterThatIsNotASetType_ThrowsInvalidOperationException()
    {
        // Arrange
        IReadOnlySet<int> value = new ForeignReadOnlySet<int>(1);

        // Act
        var act = () => ConverterRun.Write<IReadOnlySet<int>, ReadOnlySetConverter<int, NotASetElement>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Read_NumberSet_ReturnsAHashSet()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<IReadOnlySet<long>, ReadOnlySetConverter<long, NumberSetElement<long>>>("""{"NS":["5","7"]}""");

        // Assert
        await Assert.That((value is HashSet<long>, value.Contains(5), value.Contains(7), value.Count)).IsEqualTo((true, true, true, 2));
    }

    [Test]
    public async Task Read_StringSet_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<string>();

        // Act
        var value = ReadWithPool("""{"SS":["a","b"]}""", pool);

        // Assert
        await Assert.That((value.Count, pool.Returned, pool.Outstanding)).IsEqualTo((2, 1, 0));
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

    private static IReadOnlySet<string> ReadWithPool(string json, CountingArrayPool<string> pool) =>
        TestJson.Read(json, (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            var elements = ReadOnlySetConverter<string, StringSetElement>.Read(ref reader, type, pool);
            reader.ReadEndAttribute();
            return elements;
        });
}
