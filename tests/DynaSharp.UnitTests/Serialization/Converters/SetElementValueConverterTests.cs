using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>One element of a set as a value of its own, which is the operand of <c>contains</c> on a set.</summary>
public sealed class SetElementValueConverterTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(string Json, AttributeType Found)> NotNumbers() => AttributeSamples.Except(AttributeType.Number);

    public static IEnumerable<(string Json, AttributeType Found)> NotBinaries() => AttributeSamples.Except(AttributeType.Binary);

    [Test]
    [Arguments("a")]
    [Arguments("")]
    [Arguments("ünïcode \"quoted\"")]
    public async Task Write_ElementOfAStringSet_WritesAString(string element)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<string, SetElementValueConverter<string, StringSetElement>>(element);

        // Assert
        await Assert.That(json).IsEqualTo(ConverterRun.Write<string, StringConverter>(element));
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(42)]
    [Arguments(int.MaxValue)]
    public async Task Write_ElementOfANumberSet_WritesANumber(int element)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<int, SetElementValueConverter<int, NumberSetElement<int>>>(element);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"N":"{{element}}"}""");
    }

    [Test]
    public async Task Write_ElementOfANumberSetWithAFraction_WritesTheNumberAsTheSetDoes()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<decimal, SetElementValueConverter<decimal, NumberSetElement<decimal>>>(1.50m);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"1.50"}""");
    }

    [Test]
    public async Task Write_ElementOfABinarySet_WritesABinaryValue()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<byte[], SetElementValueConverter<byte[], BinarySetElement>>([1, 2, 3]);

        // Assert
        await Assert.That(json).IsEqualTo("""{"B":"AQID"}""");
    }

    [Test]
    public async Task Write_ElementOfAGuidSet_WritesTheStringTheSetHolds()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<Guid, SetElementValueConverter<Guid, GuidSetElement>>(Sample);

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");
    }

    [Test]
    public async Task Write_ElementConverterThatIsNotASetType_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<int, SetElementValueConverter<int, NotASetElement>>(1);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>()
            .WithMessage("String is not a set type: a set element converter must return StringSet, NumberSet or BinarySet.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_String_ReturnsTheElement()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<string, SetElementValueConverter<string, StringSetElement>>("""{"S":"a"}""");

        // Assert
        await Assert.That(value).IsEqualTo("a");
    }

    [Test]
    public async Task Read_Number_ReturnsTheElement()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<decimal, SetElementValueConverter<decimal, NumberSetElement<decimal>>>("""{"N":"-1.5"}""");

        // Assert
        await Assert.That(value).IsEqualTo(-1.5m);
    }

    [Test]
    public async Task Read_Binary_ReturnsTheElement()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<byte[], SetElementValueConverter<byte[], BinarySetElement>>("""{"B":"AQID"}""");

        // Assert
        await Assert.That(value).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task Read_GuidAsAString_ReturnsTheElement()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<Guid, SetElementValueConverter<Guid, GuidSetElement>>("""{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");

        // Assert
        await Assert.That(value).IsEqualTo(Sample);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_ElementOfAStringSetGivenAnotherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<string, SetElementValueConverter<string, StringSetElement>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotNumbers))]
    public async Task Read_ElementOfANumberSetGivenAnotherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<int, SetElementValueConverter<int, NumberSetElement<int>>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, found), StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotBinaries))]
    public async Task Read_ElementOfABinarySetGivenAnotherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<byte[], SetElementValueConverter<byte[], BinarySetElement>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Binary, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_ElementConverterThatIsNotASetType_ThrowsInvalidOperationException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<int, SetElementValueConverter<int, NotASetElement>>("""{"S":"1"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }
}
