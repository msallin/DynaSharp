using System.Globalization;
using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Serialization;

public sealed class SdkDateTimeConverterTests
{
    private const string SdkFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(string Text, long Ticks)> Fractions() =>
    [
        ("2026-10-31T12:00:00Z", 0),
        ("2026-10-31T12:00:00.1Z", 1000000),
        ("2026-10-31T12:00:00.12Z", 1200000),
        ("2026-10-31T12:00:00.123Z", 1230000),
        ("2026-10-31T12:00:00.1234Z", 1234000),
        ("2026-10-31T12:00:00.12345Z", 1234500),
        ("2026-10-31T12:00:00.123456Z", 1234560),
        ("2026-10-31T12:00:00.1234567Z", 1234567),
    ];

    [Test]
    [Arguments(0L, "2026-10-31T12:00:00.000Z")]
    [Arguments(1234567L, "2026-10-31T12:00:00.123Z")]
    [Arguments(1239999L, "2026-10-31T12:00:00.123Z")]
    [Arguments(9999999L, "2026-10-31T12:00:00.999Z")]
    public async Task Write_UtcTime_WritesThreeDigitsAndCutsOffTheRest(long ticks, string expected)
    {
        // Arrange (the SDK formats with fff, which cuts off and does not round)
        var value = Noon.AddTicks(ticks);

        // Act
        var json = ConverterRun.Write<DateTime, SdkDateTimeConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"S":"{{expected}}"}""");
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task Write_LocalOrUnspecifiedTime_WritesTheLocalTimeInUtc(DateTimeKind kind)
    {
        // Arrange: the SDK takes an unspecified time for local time, whatever the time zone of the machine; the expectation is spelled from
        // the framework's own conversion, not from the converter
        var value = new DateTime(2026, 7, 1, 12, 0, 0, kind).AddTicks(1234567);
        var expected = DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime().ToString(SdkFormat, CultureInfo.InvariantCulture);

        // Act
        var json = ConverterRun.Write<DateTime, SdkDateTimeConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"S":"{{expected}}"}""");
    }

    [Test]
    public async Task Write_Boundaries_WriteTheFirstAndLastMillisecond()
    {
        // Arrange
        var first = new DateTime(0, DateTimeKind.Utc);
        var last = new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Utc);

        // Act
        var json = ConverterRun.Write<DateTime, SdkDateTimeConverter>(first) + ConverterRun.Write<DateTime, SdkDateTimeConverter>(last);

        // Assert
        await Assert.That(json).IsEqualTo("""{"S":"0001-01-01T00:00:00.000Z"}{"S":"9999-12-31T23:59:59.999Z"}""");
    }

    [Test]
    [MethodDataSource(nameof(Fractions))]
    public async Task Read_ZeroToSevenFractionDigits_ReturnsUtcTime(string text, long ticks)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTime, SdkDateTimeConverter>($$"""{"S":"{{text}}"}""");

        // Assert
        await Assert.That((value, value.Kind)).IsEqualTo((Noon.AddTicks(ticks), DateTimeKind.Utc));
    }

    [Test]
    [Arguments("2026-10-31T14:00:00.123+02:00")]
    [Arguments("2026-10-31T06:30:00.123-05:30")]
    [Arguments("2026-10-31T12:00:00.123+00:00")]
    public async Task Read_Offset_ReturnsTheSameInstantInUtc(string text)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTime, SdkDateTimeConverter>($$"""{"S":"{{text}}"}""");

        // Assert
        await Assert.That((value, value.Kind)).IsEqualTo((Noon.AddMilliseconds(123), DateTimeKind.Utc));
    }

    [Test]
    public async Task ReadOfWrite_LocalTime_GivesTheInstantInUtcToTheMillisecond()
    {
        // Arrange (DynamoDBContext of SDK 4 reads in UTC under its default, RetrieveDateTimeInUtc = true)
        var value = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local).AddTicks(1234567);

        // Act
        var read = ConverterRun.Read<DateTime, SdkDateTimeConverter>(ConverterRun.Write<DateTime, SdkDateTimeConverter>(value));

        // Assert
        await Assert.That((read, read.Kind)).IsEqualTo((value.ToUniversalTime().AddTicks(-4567), DateTimeKind.Utc));
    }

    [Test]
    [Arguments("""{"S":"2026-10-31T12:00:00.123"}""")]
    [Arguments("""{"S":"2026-10-31"}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00.12345678Z"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00.123Zé"}""")]
    [Arguments("""{"S":"nonsense"}""")]
    public async Task Read_TextThatIsNotAnInstant_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange (the SDK reads text without an offset as UTC, and never writes it)
        // Act
        var act = () => ConverterRun.Read<DateTime, SdkDateTimeConverter>(json);

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
        var act = () => ConverterRun.Read<DateTime, SdkDateTimeConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEntity_StoresTheKeyAndTheNullableInTheFormOfTheSdk()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var remittance = new Remittance { Id = "r1", PaidAt = Noon.AddTicks(1234567), RefundedAt = Noon };

        // Act
        var json = client.Table<Remittance>("Shop").Put(remittance).ToRequestJson();

        // Assert
        await Assert.That((json.Contains("\"PaidAt\":{\"S\":\"2026-10-31T12:00:00.123Z\"}", StringComparison.Ordinal), json.Contains("\"RefundedAt\":{\"S\":\"2026-10-31T12:00:00.000Z\"}", StringComparison.Ordinal)))
            .IsEqualTo((true, true));
    }

    [Test]
    public async Task ToRequestJson_RangeOnAPlainSortKey_BoundsInTheFormOfTheSdk()
    {
        // Arrange (the text has a fixed width, so it orders chronologically, and a plain sort key has the ranges)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Remittance>("Shop").Query(Remittance.Keys.Pk.Of("r1"), Remittance.Keys.Sk.Between(Noon, Noon.AddDays(1))).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            {"S":"2026-10-31T12:00:00.000Z"},":v2":{"S":"2026-11-01T12:00:00.000Z"}
            """);
    }
}
