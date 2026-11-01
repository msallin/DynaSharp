using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class BoolConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotBooleans() => AttributeSamples.Except(AttributeType.Boolean);

    [Test]
    [Arguments(true, """{"BOOL":true}""")]
    [Arguments(false, """{"BOOL":false}""")]
    public async Task Write_Boolean_WritesABooleanValue(bool value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<bool, BoolConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"BOOL":true}""", true)]
    [Arguments("""{"BOOL":false}""", false)]
    public async Task Read_BooleanValue_ReturnsTheBoolean(string json, bool expected)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<bool, BoolConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(NotBooleans))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<bool, BoolConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Boolean, found), StringComparison.Ordinal);
    }
}
