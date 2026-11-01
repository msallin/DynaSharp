using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class BinaryConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotBinaries() => AttributeSamples.Except(AttributeType.Binary);

    public static IEnumerable<(string Hex, string Json)> Binaries() =>
    [
        (string.Empty, """{"B":""}"""),
        ("010203", """{"B":"AQID"}"""),
        ("FFFE", """{"B":"//4="}"""),
        ("00", """{"B":"AA=="}"""),
        ("4D61", """{"B":"TWE="}"""),
        ("4D616E", """{"B":"TWFu"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Binaries))]
    public async Task Write_Bytes_WritesBase64(string hex, string expected)
    {
        // Arrange
        var value = Convert.FromHexString(hex);

        // Act
        var json = ConverterRun.Write<byte[], BinaryConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        byte[] value = null!;

        // Act
        var act = () => ConverterRun.Write<byte[], BinaryConverter>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    [MethodDataSource(nameof(Binaries))]
    public async Task Read_BinaryValue_ReturnsTheBytes(string hex, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<byte[], BinaryConverter>(json);

        // Assert
        await Assert.That(Convert.ToHexString(value)).IsEqualTo(hex);
    }

    [Test]
    [Arguments("""{"B":"!!!"}""")]
    [Arguments("""{"B":"AQI"}""")]
    public async Task Read_TextThatIsNotBase64_ThrowsJsonException(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<byte[], BinaryConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The binary value is not valid base64.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotBinaries))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<byte[], BinaryConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Binary, found), StringComparison.Ordinal);
    }
}
