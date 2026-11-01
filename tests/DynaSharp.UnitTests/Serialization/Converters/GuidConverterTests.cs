using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class GuidConverterTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(Guid Value, string Json)> Guids() =>
    [
        (Sample, """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}"""),
        (Guid.Empty, """{"S":"00000000-0000-0000-0000-000000000000"}"""),
        (new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), """{"S":"ffffffff-ffff-ffff-ffff-ffffffffffff"}"""),
        (new Guid("ABCDEF01-2345-6789-ABCD-EF0123456789"), """{"S":"abcdef01-2345-6789-abcd-ef0123456789"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Guids))]
    public async Task Write_Guid_WritesTheLowercaseHyphenatedForm(Guid value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<Guid, GuidConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Guids))]
    public async Task Read_LowercaseGuid_ReturnsTheGuid(Guid expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<Guid, GuidConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_UppercaseGuid_ReturnsTheGuid()
    {
        // Arrange
        const string json = """{"S":"0F8FAD5B-D9CB-469F-A165-70867728950E"}""";

        // Act
        var value = ConverterRun.Read<Guid, GuidConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(Sample);
    }

    [Test]
    [Arguments("""{"S":"0f8fad5bd9cb469fa16570867728950e"}""")]
    [Arguments("""{"S":"{0f8fad5b-d9cb-469f-a165-70867728950e}"}""")]
    [Arguments("""{"S":"(0f8fad5b-d9cb-469f-a165-70867728950e)"}""")]
    [Arguments("""{"S":"0f8fad5b-d9cb-469f-a165-70867728950"}""")]
    [Arguments("""{"S":"0f8fad5b-d9cb-469f-a165-70867728950ee"}""")]
    [Arguments("""{"S":"0f8fad5bd-9cb-469f-a165-70867728950e"}""")]
    [Arguments("""{"S":"0f8fad5b-d9cb-469f-a165-7086772895ge"}""")]
    [Arguments("""{"S":" 0f8fad5b-d9cb-469f-a165-70867728950e"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"S":"not a guid"}""")]
    public async Task Read_TextThatIsNotTheHyphenatedForm_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<Guid, GuidConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not a Guid in the form 00000000-0000-0000-0000-000000000000.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_EscapedGuid_ReturnsTheGuid()
    {
        // Arrange: the first digit as a JSON escape, built from parts so that no tool turns it into a character
        var json = "{\"S\":\"\\u0030f8fad5b-d9cb-469f-a165-70867728950e\"}";

        // Act
        var value = ConverterRun.Read<Guid, GuidConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(Sample);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<Guid, GuidConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
