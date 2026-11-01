using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ReadOnlyMemoryConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotBinaries() => AttributeSamples.Except(AttributeType.Binary);

    [Test]
    public async Task Write_DefaultMemory_WritesAnEmptyBinaryValue()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>(default);

        // Assert
        await Assert.That(json).IsEqualTo("""{"B":""}""");
    }

    [Test]
    public async Task Write_SliceOfAnArray_WritesOnlyTheSlice()
    {
        // Arrange
        ReadOnlyMemory<byte> slice = new byte[] { 9, 1, 2, 3, 9 }.AsMemory(1, 3);

        // Act
        var json = ConverterRun.Write<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>(slice);

        // Assert
        await Assert.That(json).IsEqualTo("""{"B":"AQID"}""");
    }

    [Test]
    public async Task Read_BinaryValue_ReturnsTheBytes()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("""{"B":"AQID"}""");

        // Assert
        await Assert.That(value.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Read_EmptyBinaryValue_ReturnsEmptyMemory()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("""{"B":""}""");

        // Assert
        await Assert.That(value.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Read_TextThatIsNotBase64_ThrowsJsonException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>("""{"B":"!!!"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The binary value is not valid base64.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotBinaries))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Binary, found), StringComparison.Ordinal);
    }
}
