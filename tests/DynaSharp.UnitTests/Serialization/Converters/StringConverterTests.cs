using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Operations;

namespace DynaSharp.UnitTests.Serialization;

public sealed class StringConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    [Test]
    [Arguments("Alice", """{"S":"Alice"}""")]
    [Arguments("", """{"S":""}""")]
    [Arguments(" ", """{"S":" "}""")]
    public async Task Write_Text_WritesAStringValue(string text, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<string, StringConverter>(text);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_QuotesBackslashLineBreakAndNonAsciiText_WritesTheSameTextBack()
    {
        // Arrange: a quote, a backslash, a line break, umlauts, the euro sign and a character outside the basic plane
        var text = "say \"hi\" back\\slash\nline ä € \U0001F600";
        var expected = "{\"S\":\"say \\\"hi\\\" back\\\\slash\\nline ä € \U0001F600\"}";

        // Act
        var json = ConverterRun.Write<string, StringConverter>(text);

        // Assert
        await Assert.That(JsonComparer.Differences(expected, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Write_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string text = null!;

        // Act
        var act = () => ConverterRun.Write<string, StringConverter>(text);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    [Arguments("""{"S":"Alice"}""", "Alice")]
    [Arguments("""{"S":""}""", "")]
    [Arguments("""{"S":"ä € 😀"}""", "ä € 😀")]
    public async Task Read_StringValue_ReturnsTheText(string json, string expected)
    {
        // Arrange
        // Act
        var text = ConverterRun.Read<string, StringConverter>(json);

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_EscapedText_ReturnsTheUnescapedText()
    {
        // Arrange: built from parts, so that no tool turns the escapes into characters
        var json = "{\"S\":\"A\\u0042C\\n\\\"\"}";

        // Act
        var text = ConverterRun.Read<string, StringConverter>(json);

        // Assert
        await Assert.That(text).IsEqualTo("ABC\n\"");
    }

    [Test]
    public async Task Read_LargeString_ReturnsTheText()
    {
        // Arrange
        var expected = new string('x', 300_000);
        var json = new StringBuilder("{\"S\":\"").Append(expected).Append("\"}").ToString();

        // Act
        var text = ConverterRun.Read<string, StringConverter>(json);

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<string, StringConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
