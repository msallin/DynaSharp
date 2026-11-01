using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DateTimeOffsetConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(DateTimeOffset Value, string Json)> Instants() =>
    [
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero), """{"S":"2026-10-31T12:00:00.0000000Z"}"""),
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2)), """{"S":"2026-10-31T10:00:00.0000000Z"}"""),
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromMinutes(-330)), """{"S":"2026-10-31T17:30:00.0000000Z"}"""),
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567), """{"S":"2026-10-31T12:00:00.1234567Z"}"""),
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero).AddTicks(1), """{"S":"2026-10-31T12:00:00.0000001Z"}"""),
        (new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), """{"S":"1970-01-01T00:00:00.0000000Z"}"""),
        (DateTimeOffset.MinValue, """{"S":"0001-01-01T00:00:00.0000000Z"}"""),
        (new DateTimeOffset(1, 1, 1, 14, 0, 0, TimeSpan.FromHours(14)), """{"S":"0001-01-01T00:00:00.0000000Z"}"""),
        (DateTimeOffset.MaxValue, """{"S":"9999-12-31T23:59:59.9999999Z"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Instants))]
    public async Task Write_Instant_WritesTheInstantInUtcWithSevenFractionDigits(DateTimeOffset value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateTimeOffset, DateTimeOffsetConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_LaterInstant_SortsAfterEarlierInstantAsText()
    {
        // Arrange: 23:30 at +05:00 is 18:30Z, which is before 19:00Z although its clock reads later
        var earlier = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.FromHours(5));
        var later = new DateTimeOffset(2026, 10, 31, 19, 0, 0, TimeSpan.Zero);

        // Act
        var order = string.CompareOrdinal(
            ConverterRun.Write<DateTimeOffset, DateTimeOffsetConverter>(earlier),
            ConverterRun.Write<DateTimeOffset, DateTimeOffsetConverter>(later));

        // Assert
        await Assert.That(order).IsLessThan(0);
    }

    [Test]
    [MethodDataSource(nameof(Instants))]
    public async Task Read_InstantInUtc_ReturnsTheInstantWithOffsetZero(DateTimeOffset expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(json);

        // Assert
        await Assert.That((value.UtcTicks, value.Offset)).IsEqualTo((expected.UtcTicks, TimeSpan.Zero));
    }

    [Test]
    [Arguments("""{"S":"2026-10-31T12:00:00Z"}""", "2026-10-31T12:00:00.0000000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00.123Z"}""", "2026-10-31T12:00:00.1230000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00.1Z"}""", "2026-10-31T12:00:00.1000000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00+02:00"}""", "2026-10-31T10:00:00.0000000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00.5-05:30"}""", "2026-10-31T17:30:00.5000000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00.1234567+00:00"}""", "2026-10-31T12:00:00.1234567Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00-00:00"}""", "2026-10-31T12:00:00.0000000Z")]
    [Arguments("""{"S":"2026-10-31T12:00:00+14:00"}""", "2026-10-30T22:00:00.0000000Z")]
    public async Task Read_OtherIso8601Forms_ReturnsTheSameInstantInUtc(string json, string expectedUtc)
    {
        // Arrange (the AWS SDK for .NET writes three fraction digits; other writers use an offset)
        var expected = DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

        // Act
        var value = ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(json);

        // Assert
        await Assert.That((value.UtcTicks, value.Offset)).IsEqualTo((expected.UtcTicks, TimeSpan.Zero));
    }

    [Test]
    [Arguments("""{"S":"2026-10-31T12:00:00"}""")]
    [Arguments("""{"S":"2026-10-31"}""")]
    [Arguments("""{"S":"2026-10-31 12:00:00Z"}""")]
    [Arguments("""{"S":"2026-10-31T12:00Z"}""")]
    [Arguments("""{"S":"2026-10-31t12:00:00z"}""")]
    [Arguments("""{"S":"10/31/2026 12:00:00Z"}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00.12345678Z"}""")]
    [Arguments("""{"S":"2026-13-31T12:00:00Z"}""")]
    [Arguments("""{"S":"2026-02-30T12:00:00Z"}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00+15:00"}""")]
    [Arguments("""{"S":"9999-12-31T23:59:59.9999999-01:00"}""")]
    [Arguments("""{"S":"0001-01-01T00:00:00.0000000+01:00"}""")]
    [Arguments("""{"S":" 2026-10-31T12:00:00Z"}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00Z "}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00.1234567+01:00 trailing text beyond the longest form"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"S":"yesterday"}""")]
    public async Task Read_TextThatIsNotAnInstant_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not an ISO 8601 date and time with a UTC offset, such as 2026-10-31T12:00:00.0000000Z.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NonAsciiText_ThrowsFormatException()
    {
        // Arrange: a full-width digit, which a lenient parser might accept
        var json = "{\"S\":\"2026-10-31T12:00:0０Z\"}";

        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, DateTimeOffsetConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
