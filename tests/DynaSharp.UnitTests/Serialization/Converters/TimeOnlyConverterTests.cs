using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class TimeOnlyConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(TimeOnly Value, string Json)> Times() =>
    [
        (TimeOnly.MinValue, """{"S":"00:00:00.0000000"}"""),
        (new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(7890123)), """{"S":"12:34:56.7890123"}"""),
        (new TimeOnly(9, 5, 3), """{"S":"09:05:03.0000000"}"""),
        (new TimeOnly(0, 0, 0).Add(TimeSpan.FromTicks(1)), """{"S":"00:00:00.0000001"}"""),
        (TimeOnly.MaxValue, """{"S":"23:59:59.9999999"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Times))]
    public async Task Write_Time_WritesSevenFractionDigits(TimeOnly value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<TimeOnly, TimeOnlyConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Times))]
    public async Task Read_Time_ReturnsTheTime(TimeOnly expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<TimeOnly, TimeOnlyConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"12:00:00"}""", 12, 0, 0, 0)]
    [Arguments("""{"S":"12:00:00.5"}""", 12, 0, 0, 5000000)]
    [Arguments("""{"S":"01:02:03.123"}""", 1, 2, 3, 1230000)]
    public async Task Read_FewerFractionDigits_ReturnsTheTime(string json, int hour, int minute, int second, int fractionTicks)
    {
        // Arrange
        var expected = new TimeOnly(hour, minute, second).Add(TimeSpan.FromTicks(fractionTicks));

        // Act
        var value = ConverterRun.Read<TimeOnly, TimeOnlyConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"12:00"}""")]
    [Arguments("""{"S":"1:00:00"}""")]
    [Arguments("""{"S":"12:00:60"}""")]
    [Arguments("""{"S":"12:60:00"}""")]
    [Arguments("""{"S":"25:00:00"}""")]
    [Arguments("""{"S":"12:00:00.12345678"}""")]
    [Arguments("""{"S":"12:00:00 PM"}""")]
    [Arguments("""{"S":"2026-10-31T12:00:00"}""")]
    [Arguments("""{"S":" 12:00:00"}""")]
    [Arguments("""{"S":""}""")]
    public async Task Read_TextThatIsNotATime_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<TimeOnly, TimeOnlyConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not a time in the form HH:mm:ss.fffffff.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<TimeOnly, TimeOnlyConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
