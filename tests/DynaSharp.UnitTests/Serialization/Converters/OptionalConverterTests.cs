using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization.Converters;

/// <summary>What a handle of a nullable property has for a converter: the converter of the property, and nothing else, whichever way it is asked.</summary>
public sealed class OptionalConverterTests
{
    [Test]
    public async Task Write_ValueType_WritesWhatItsConverterWrites()
    {
        // Arrange, Act
        var json = ConverterRun.Write<int, OptionalConverter<int, NumberConverter<int>>>(7);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"7"}""");
    }

    [Test]
    public async Task Read_ValueType_ReadsWhatItsConverterReads()
    {
        // Arrange, Act
        var value = ConverterRun.Read<int, OptionalConverter<int, NumberConverter<int>>>("""{"N":"7"}""");

        // Assert
        await Assert.That(value).IsEqualTo(7);
    }

    [Test]
    public async Task IsOmitted_ValueType_AsksItsConverter()
    {
        // Arrange, Act
        var omitted = OptionalConverter<int, NumberConverter<int>>.IsOmitted(0);

        // Assert
        await Assert.That(omitted).IsFalse();
    }

    [Test]
    public async Task Write_ReferenceType_WritesWhatItsConverterWrites()
    {
        // Arrange, Act
        var json = ConverterRun.Write<string, OptionalReferenceConverter<string, StringConverter>>("a");

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"a"}""");
    }

    [Test]
    public async Task Read_ReferenceType_ReadsWhatItsConverterReads()
    {
        // Arrange, Act
        var value = ConverterRun.Read<string, OptionalReferenceConverter<string, StringConverter>>("""{"S":"a"}""");

        // Assert
        await Assert.That(value).IsEqualTo("a");
    }

    [Test]
    [Arguments("", true)]
    [Arguments("a", false)]
    public async Task IsOmitted_ReferenceTypeWithOmitEmpty_AsksItsConverter(string value, bool expected)
    {
        // Arrange, Act
        var omitted = OptionalReferenceConverter<string, OmitEmptyConverter<StringConverter>>.IsOmitted(value);

        // Assert
        await Assert.That(omitted).IsEqualTo(expected);
    }
}
