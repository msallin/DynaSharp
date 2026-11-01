using System.Buffers;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

// Exercises the converter contract the way the source generator and custom converters use it: statically, through
// the type parameter, with the example converter from the IDynamoDbConverter<T> documentation.
public sealed class IDynamoDbConverterTests
{
    [Test]
    public async Task Write_CustomConverter_WritesItsRepresentation()
    {
        // Arrange
        var value = new Uri("https://example.com/a?b=1");

        // Act
        var json = TestJson.Write(w => Write<UriConverter, Uri>(w, value));

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"https://example.com/a?b=1"}""");
    }

    [Test]
    public async Task Read_CustomConverter_ReadsItsRepresentation()
    {
        // Arrange
        const string json = """{"S":"https://example.com/a?b=1"}""";

        // Act
        var value = TestJson.Read(json, static (ref r) => Read<UriConverter, Uri>(ref r));

        // Assert
        await Assert.That(value).IsEqualTo(new Uri("https://example.com/a?b=1"));
    }

    [Test]
    public async Task RoundTrip_ThroughThePublicConstructors_ReturnsTheValue()
    {
        // Arrange: how a user tests a converter, without any internals of the library.
        var value = new Uri("https://example.com/a?b=1");
        var output = new ArrayBufferWriter<byte>();

        // Act
        using (var json = new Utf8JsonWriter(output))
        {
            Write<UriConverter, Uri>(new DynamoDbWriter(json), value);
        }

        var reader = new DynamoDbReader(output.WrittenSpan);
        var result = Read<UriConverter, Uri>(ref reader);

        // Assert
        await Assert.That(result).IsEqualTo(value);
    }

    [Test]
    public async Task WriterConstructor_NullJsonWriter_ThrowsArgumentNullException()
    {
        // Arrange
        Utf8JsonWriter json = null!;

        // Act
        var act = () => new DynamoDbWriter(json).WriteNull();

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("json");
    }

    [Test]
    public async Task Read_CustomConverterGivenUnsupportedType_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"N":"1793448000"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => Read<UriConverter, Uri>(ref r));

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("Expected a DynamoDB attribute value of type String but found Number.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_CustomConverterGivenARelativeUri_ThrowsAFormatException()
    {
        // Arrange (UriFormatException is a FormatException, one of the failures that entity mapping reports with the attribute name)
        const string json = """{"S":"not/absolute"}""";

        // Act
        var act = () => TestJson.Read(json, static (ref r) => Read<UriConverter, Uri>(ref r));

        // Assert
        await Assert.That(act).ThrowsExactly<UriFormatException>();
    }

    [Test]
    public async Task Read_AttributeValueConverterInList_ReadsEveryElement()
    {
        // Arrange
        const string json = """{"L":[{"S":"a"},{"N":"1"},{"NULL":true}]}""";

        // Act
        var values = TestJson.Read(json, static (ref r) =>
        {
            r.ReadStartAttribute();
            var result = new List<AttributeValue>();
            while (r.TryReadStartAttribute(out var type))
            {
                result.Add(AttributeValueConverter.Read(ref r, type));
                r.ReadEndAttribute();
            }

            return result;
        });

        // Assert
        await Assert.That(AttributeValue.FromList(values)).IsEqualTo(AttributeValue.FromList(["a", AttributeValue.FromNumber(1), AttributeValue.Null]));
    }

    private static void Write<TConverter, T>(DynamoDbWriter writer, T value)
        where TConverter : IDynamoDbConverter<T>
        => TConverter.Write(writer, value);

    private static T Read<TConverter, T>(ref DynamoDbReader reader)
        where TConverter : IDynamoDbConverter<T>
    {
        var value = TConverter.Read(ref reader, reader.ReadStartAttribute());
        reader.ReadEndAttribute();
        return value;
    }
}
