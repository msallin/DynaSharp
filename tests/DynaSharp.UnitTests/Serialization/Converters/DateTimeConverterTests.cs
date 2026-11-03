using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DateTimeConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    [Test]
    public async Task Write_UtcTime_WritesItAsItIs()
    {
        // Arrange
        var value = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

        // Act
        var json = ConverterRun.Write<DateTime, DateTimeConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"2026-10-31T12:00:00.1234567Z"}""");
    }

    [Test]
    public async Task Write_LocalTime_WritesTheSameInstantInUtc()
    {
        // Arrange: noon on a summer day, whatever the offset of the time zone of the machine; the expectation is spelled from the
        // framework's own conversion, not from the converter
        var value = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);
        var expected = value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var json = ConverterRun.Write<DateTime, DateTimeConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"S":"{{expected}}"}""");
    }

    [Test]
    public async Task Write_UnspecifiedTime_ThrowsArgumentException()
    {
        // Arrange (an unspecified time names no instant)
        var value = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        var act = () => ConverterRun.Write<DateTime, DateTimeConverter>(value);

        // Assert
        await Assert.That(act).Throws<ArgumentException>()
            .WithMessage("The kind of the DateTime is Unspecified, so it names no instant. A DateTime that was never set, default(DateTime), has this kind: set it to a UTC time first. Only for times that the AWS SDK wrote, use SdkDateTimeConverter, which takes it for local time as the SDK does. (Parameter 'value')", StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_Boundaries_WriteTheFirstAndLastTick()
    {
        // Arrange
        var first = new DateTime(0, DateTimeKind.Utc);
        var last = new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Utc);

        // Act
        var json = ConverterRun.Write<DateTime, DateTimeConverter>(first) + ConverterRun.Write<DateTime, DateTimeConverter>(last);

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"0001-01-01T00:00:00.0000000Z"}{"S":"9999-12-31T23:59:59.9999999Z"}""");
    }

    [Test]
    public async Task Read_Instant_ReturnsUtcTime()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTime, DateTimeConverter>("""{"S":"2026-10-31T12:00:00.1234567Z"}""");

        // Assert
        await Assert.That((value, value.Kind)).IsEqualTo((new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567), DateTimeKind.Utc));
    }

    [Test]
    public async Task Read_InstantWithOffset_ReturnsTheSameInstantInUtc()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTime, DateTimeConverter>("""{"S":"2026-10-31T12:00:00+02:00"}""");

        // Assert
        await Assert.That((value, value.Kind)).IsEqualTo((new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc), DateTimeKind.Utc));
    }

    [Test]
    public async Task Read_Boundaries_ReturnTheFirstAndLastTick()
    {
        // Arrange
        // Act
        var first = ConverterRun.Read<DateTime, DateTimeConverter>("""{"S":"0001-01-01T00:00:00.0000000Z"}""");
        var last = ConverterRun.Read<DateTime, DateTimeConverter>("""{"S":"9999-12-31T23:59:59.9999999Z"}""");

        // Assert
        await Assert.That((first.Ticks, last.Ticks)).IsEqualTo((0L, DateTime.MaxValue.Ticks));
    }

    [Test]
    [Arguments("""{"S":"2026-10-31T12:00:00"}""")]
    [Arguments("""{"S":"nonsense"}""")]
    public async Task Read_TextThatIsNotAnInstant_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTime, DateTimeConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not an ISO 8601 date and time with a UTC offset, such as 2026-10-31T12:00:00.0000000Z.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTime, DateTimeConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
