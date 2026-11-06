using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class JsonElementConverterTests
{
    public static IEnumerable<(string Json, string Stored)> Elements() =>
    [
        ("""{"to":{"city":"Bern","zip":"3000"},"weight":1.50,"fragile":true,"tags":["a",null],"note":null,"lines":[]}""",
            """{"M":{"to":{"M":{"city":{"S":"Bern"},"zip":{"S":"3000"}}},"weight":{"N":"1.50"},"fragile":{"BOOL":true},"tags":{"L":[{"S":"a"},{"NULL":true}]},"lines":{"L":[]}}}"""),
        ("[1,2]", """{"L":[{"N":"1"},{"N":"2"}]}"""),
        ("\"2026-10-31T12:00:00+01:00\"", """{"S":"2026-10-31T12:00:00+01:00"}"""),
        ("null", """{"NULL":true}"""),
    ];

    public static IEnumerable<(string Stored, string Json)> StoredValues() =>
    [
        ("""{"M":{"a":{"N":"1.5"},"b":{"NULL":true},"c":{"L":[{"BOOL":false}]}}}""", """{"a":1.5,"b":null,"c":[false]}"""),
        ("""{"SS":["x","y"]}""", """["x","y"]"""),
        ("""{"NS":["1","2E+3"]}""", "[1,2E+3]"),
        ("""{"B":"AQID"}""", "\"AQID\""),
        ("""{"BS":["AQ=="]}""", """["AQ=="]"""),
        ("""{"S":"é"}""", "\"é\""),
        ("""{"NULL":true}""", "null"),
    ];

    [Test]
    [MethodDataSource(nameof(Elements))]
    public async Task Write_Element_WritesTheNativeValue(string json, string stored)
    {
        // Arrange
        var element = JsonElement.Parse(json);

        // Act
        var written = ConverterRun.Write<JsonElement, JsonElementConverter>(element);

        // Assert
        await Assert.That(written).IsEqualTo(stored);
    }

    [Test]
    [MethodDataSource(nameof(StoredValues))]
    public async Task Read_StoredValue_IsThePlainJson(string stored, string json)
    {
        // Arrange
        // Act
        var element = ConverterRun.Read<JsonElement, JsonElementConverter>(stored);

        // Assert
        await Assert.That(element.GetRawText()).IsEqualTo(json);
    }

    [Test]
    public async Task Write_DefaultElement_ThrowsArgumentException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<JsonElement, JsonElementConverter>(default);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>()
            .WithMessage("A default JsonElement holds no JSON. Leave the attribute out with a JsonElement? that is null. (Parameter 'value')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_NumberBeyondTheLimits_ThrowsArgumentExceptionNamingValue()
    {
        // Arrange
        var element = JsonElement.Parse("""{"n":1E+126}""");

        // Act
        var act = () => ConverterRun.Write<JsonElement, JsonElementConverter>(element);

        // Assert
        await Assert.That(act).ThrowsExactly<UnstorableJsonException>().WithParameterName("value");
    }

    [Test]
    public async Task Read_NumberThatIsNoJsonNumber_ThrowsFormatException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<JsonElement, JsonElementConverter>("""{"N":"+1"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }
}
