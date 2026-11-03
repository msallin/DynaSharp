using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Serialization;

public sealed class OffsetTimestampConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(DateTimeOffset Value, string Text)> Times() =>
    [
        (new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(1)), "2026-11-20T11:00:00.0000000+01:00"),
        (new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), "2026-11-20T10:00:00.0000000+00:00"),
        (new DateTimeOffset(2026, 11, 20, 4, 30, 0, TimeSpan.FromMinutes(-330)), "2026-11-20T04:30:00.0000000-05:30"),
        (new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.FromHours(14)).AddTicks(1234567), "2026-11-20T23:59:59.1234567+14:00"),
        (new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.FromHours(-12)), "2026-11-20T00:00:00.0000000-12:00"),
        (DateTimeOffset.MinValue, "0001-01-01T00:00:00.0000000+00:00"),
        (DateTimeOffset.MaxValue, "9999-12-31T23:59:59.9999999+00:00"),
    ];

    public static IEnumerable<(string Text, long Ticks)> Fractions() =>
    [
        ("2026-11-20T11:00:00+01:00", 0),
        ("2026-11-20T11:00:00.1+01:00", 1000000),
        ("2026-11-20T11:00:00.12+01:00", 1200000),
        ("2026-11-20T11:00:00.123+01:00", 1230000),
        ("2026-11-20T11:00:00.1234+01:00", 1234000),
        ("2026-11-20T11:00:00.12345+01:00", 1234500),
        ("2026-11-20T11:00:00.123456+01:00", 1234560),
        ("2026-11-20T11:00:00.1234567+01:00", 1234567),
    ];

    [Test]
    [MethodDataSource(nameof(Times))]
    public async Task Write_Time_WritesTheRoundTripFormWithTheOffset(DateTimeOffset value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateTimeOffset, OffsetTimestampConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"S":"{{expected}}"}""");
    }

    [Test]
    [MethodDataSource(nameof(Times))]
    public async Task Read_RoundTripForm_KeepsTheOffset(DateTimeOffset expected, string text)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>($$"""{"S":"{{text}}"}""");

        // Assert
        await Assert.That((value.DateTime, value.Offset)).IsEqualTo((expected.DateTime, expected.Offset));
    }

    [Test]
    [MethodDataSource(nameof(Fractions))]
    public async Task Read_ZeroToSevenFractionDigits_KeepsTheOffset(string text, long ticks)
    {
        // Arrange
        var expected = new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(1)).AddTicks(ticks);

        // Act
        var value = ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>($$"""{"S":"{{text}}"}""");

        // Assert
        await Assert.That((value.DateTime, value.Offset)).IsEqualTo((expected.DateTime, expected.Offset));
    }

    [Test]
    [Arguments("2026-11-20T10:00:00.0000000Z")]
    [Arguments("2026-11-20T10:00:00Z")]
    public async Task Read_TextThatDateTimeOffsetConverterWrote_ReadsWithOffsetZero(string text)
    {
        // Arrange (a property that moves to this converter reads what it stored before)
        // Act
        var value = ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>($$"""{"S":"{{text}}"}""");

        // Assert
        await Assert.That((value.DateTime, value.Offset)).IsEqualTo((new DateTime(2026, 11, 20, 10, 0, 0), TimeSpan.Zero));
    }

    [Test]
    [Arguments("""{"S":"2026-11-20T11:00:00.0000000"}""")]
    [Arguments("""{"S":"2026-11-20T11:00:00.12345678+01:00"}""")]
    [Arguments("""{"S":"0001-01-01T00:00:00.0000000+01:00"}""")]
    [Arguments("""{"S":"9999-12-31T23:59:59.9999999-01:00"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"S":"nonsense"}""")]
    public async Task Read_TextThatIsNotATimeWithAnOffset_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange (an instant outside the range of DateTimeOffset is among them)
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>(json);

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
        var act = () => ConverterRun.Read<DateTimeOffset, OffsetTimestampConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Write_TimesAtTwoOffsets_SortsTheTextAgainstTheInstants()
    {
        // Arrange (what the remarks warn about: 11:00+01:00 is 10:00 in UTC, before 10:30+00:00)
        var earlier = new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(1));
        var later = new DateTimeOffset(2026, 11, 20, 10, 30, 0, TimeSpan.Zero);

        // Act
        var order = string.CompareOrdinal(ConverterRun.Write<DateTimeOffset, OffsetTimestampConverter>(earlier), ConverterRun.Write<DateTimeOffset, OffsetTimestampConverter>(later));

        // Assert
        await Assert.That((earlier < later, order > 0)).IsEqualTo((true, true));
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEntity_StoresTheOffset()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var remittance = new Remittance { Id = "r1", PaidAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), LocalTime = new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.FromHours(1)) };

        // Act
        var json = client.Table<Remittance>("Shop").Put(remittance).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "LocalTime":{"S":"2026-11-20T11:00:00.0000000+01:00"}
            """);
    }

    [Test]
    public async Task ItemAsync_EntityWithAnOffset_KeepsIt()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"_type":{"S":"REMITTANCE"},"Id":{"S":"r1"},"PaidAt":{"S":"2026-11-20T10:00:00.000Z"},"LocalTime":{"S":"2026-11-20T11:00:00.0000000+01:00"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var remittance = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc))).ItemAsync();

        // Assert
        await Assert.That((remittance!.LocalTime.DateTime, remittance.LocalTime.Offset)).IsEqualTo((new DateTime(2026, 11, 20, 11, 0, 0), TimeSpan.FromHours(1)));
    }
}
