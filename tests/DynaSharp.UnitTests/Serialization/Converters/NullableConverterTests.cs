using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class NullableConverterTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Test]
    public async Task Write_NullValueType_WritesTheNullValue()
    {
        // Arrange
        int? value = null;

        // Act
        var json = ConverterRun.Write<int?, NullableConverter<int, NumberConverter<int>>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"NULL":true}""");
    }

    [Test]
    [Arguments(0, """{"N":"0"}""")]
    [Arguments(5, """{"N":"5"}""")]
    [Arguments(int.MinValue, """{"N":"-2147483648"}""")]
    public async Task Write_ValueTypeWithValue_WritesTheValueOfTheInnerConverter(int value, string expected)
    {
        // Arrange
        int? nullable = value;

        // Act
        var json = ConverterRun.Write<int?, NullableConverter<int, NumberConverter<int>>>(nullable);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_NullableGuid_WritesTheGuidOrTheNullValue()
    {
        // Arrange
        // Act
        var present = ConverterRun.Write<Guid?, NullableConverter<Guid, GuidConverter>>(Sample);
        var missing = ConverterRun.Write<Guid?, NullableConverter<Guid, GuidConverter>>(null);

        // Assert
        await Assert.That(present + missing).IsEqualTo("""{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}{"NULL":true}""");
    }

    [Test]
    public async Task Read_NullValueForValueType_ReturnsNull()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int?, NullableConverter<int, NumberConverter<int>>>("""{"NULL":true}""");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Read_NumberForValueType_ReturnsTheNumber()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int?, NullableConverter<int, NumberConverter<int>>>("""{"N":"5"}""");

        // Assert
        await Assert.That(value).IsEqualTo(5);
    }

    [Test]
    public async Task Read_OtherTypeForValueType_ThrowsTheExceptionOfTheInnerConverter()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<int?, NullableConverter<int, NumberConverter<int>>>("""{"S":"5"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, AttributeType.String), StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_NullReference_WritesTheNullValue()
    {
        // Arrange
        string? value = null;

        // Act
        var json = ConverterRun.Write<string?, NullableReferenceConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"NULL":true}""");
    }

    [Test]
    [Arguments("", """{"S":""}""")]
    [Arguments("a", """{"S":"a"}""")]
    public async Task Write_ReferenceWithValue_WritesTheValueOfTheInnerConverter(string value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<string?, NullableReferenceConverter<string, StringConverter>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_NullValueForReference_ReturnsNull()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<string?, NullableReferenceConverter<string, StringConverter>>("""{"NULL":true}""");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Read_StringForReference_ReturnsTheString()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<string?, NullableReferenceConverter<string, StringConverter>>("""{"S":"a"}""");

        // Assert
        await Assert.That(value).IsEqualTo("a");
    }

    [Test]
    public async Task Read_OtherTypeForReference_ThrowsTheExceptionOfTheInnerConverter()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<string?, NullableReferenceConverter<string, StringConverter>>("""{"N":"1"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, AttributeType.Number), StringComparison.Ordinal);
    }
}
