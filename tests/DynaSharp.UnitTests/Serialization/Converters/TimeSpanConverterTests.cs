using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class TimeSpanConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotStrings() => AttributeSamples.Except(AttributeType.String);

    public static IEnumerable<(TimeSpan Value, string Json)> Spans() =>
    [
        (TimeSpan.Zero, """{"S":"00:00:00"}"""),
        (TimeSpan.FromSeconds(1), """{"S":"00:00:01"}"""),
        (TimeSpan.FromSeconds(-1), """{"S":"-00:00:01"}"""),
        (new TimeSpan(1, 2, 3, 4, 500), """{"S":"1.02:03:04.5000000"}"""),
        (new TimeSpan(23, 59, 59), """{"S":"23:59:59"}"""),
        (TimeSpan.FromTicks(1), """{"S":"00:00:00.0000001"}"""),
        (TimeSpan.MaxValue, """{"S":"10675199.02:48:05.4775807"}"""),
        (TimeSpan.MinValue, """{"S":"-10675199.02:48:05.4775808"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Spans))]
    public async Task Write_Span_WritesTheConstantFormat(TimeSpan value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<TimeSpan, TimeSpanConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Spans))]
    public async Task Read_Span_ReturnsTheSpan(TimeSpan expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<TimeSpan, TimeSpanConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"abc"}""")]
    [Arguments("""{"S":""}""")]
    [Arguments("""{"S":"00:00:01x"}""")]
    [Arguments("""{"S":"00:00:01.12345678"}""")]
    [Arguments("""{"S":"00:60:00"}""")]
    [Arguments("""{"S":"10675200.00:00:00"}""")]
    [Arguments("""{"S":"1 day"}""")]
    public async Task Read_TextThatIsNotASpan_ThrowsFormatExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<TimeSpan, TimeSpanConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>()
            .WithMessage("The value is not a time span in the form [-][d.]hh:mm:ss[.fffffff].", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotStrings))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<TimeSpan, TimeSpanConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.String, found), StringComparison.Ordinal);
    }
}
