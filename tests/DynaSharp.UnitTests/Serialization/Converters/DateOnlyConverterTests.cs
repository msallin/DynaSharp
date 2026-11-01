using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DateOnlyConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(DateOnly Value, string Json)> Dates() =>
    [
        (new DateOnly(2026, 10, 31), """{"S":"2026-10-31"}"""),
        (new DateOnly(2024, 2, 29), """{"S":"2024-02-29"}"""),
        (new DateOnly(1970, 1, 1), """{"S":"1970-01-01"}"""),
        (DateOnly.MinValue, """{"S":"0001-01-01"}"""),
        (DateOnly.MaxValue, """{"S":"9999-12-31"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Dates))]
    public async Task Write_Date_WritesYearMonthDay(DateOnly value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateOnly, DateOnlyConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Dates))]
    public async Task Read_Date_ReturnsTheDate(DateOnly expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateOnly, DateOnlyConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"2026-1-1"}""")]
    [Arguments("""{"S":"20261031"}""")]
    [Arguments("""{"S":"2026-10-31T00:00:00Z"}""")]
    [Arguments("""{"S":"2026-02-30"}""")]
    [Arguments("""{"S":"2025-02-29"}""")]
    [Arguments("""{"S":"2026-13-01"}""")]
    [Arguments("""{"S":"2026-10-31 "}""")]
    [Arguments("""{"S":"31.10.2026"}""")]
    [Arguments("""{"S":"2026-10-31abcd"}""")]
    [Arguments("""{"S":""}""")]
    public async Task Read_TextThatIsNotADate_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateOnly, DateOnlyConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not a date in the form yyyy-MM-dd.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NonAsciiDigits_ThrowsFormatException()
    {
        // Arrange: Arabic-Indic digits, which a lenient parser might accept
        var json = "{\"S\":\"٢٠٢٦-10-31\"}";

        // Act
        var act = () => ConverterRun.Read<DateOnly, DateOnlyConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateOnly, DateOnlyConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
