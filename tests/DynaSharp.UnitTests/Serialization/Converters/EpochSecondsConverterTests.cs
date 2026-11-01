using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class EpochSecondsConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotNumbers() => AttributeSamples.Except(AttributeType.Number);

    public static IEnumerable<(DateTimeOffset Value, string Json)> Instants() =>
    [
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero), """{"N":"1793448000"}"""),
        (new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2)), """{"N":"1793440800"}"""),
        (new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), """{"N":"0"}"""),
        (new DateTimeOffset(1969, 12, 31, 23, 59, 59, TimeSpan.Zero), """{"N":"-1"}"""),
        (DateTimeOffset.MinValue, """{"N":"-62135596800"}"""),
        (new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero), """{"N":"253402300799"}"""),
    ];

    [Test]
    [MethodDataSource(nameof(Instants))]
    public async Task Write_Instant_WritesWholeSecondsSinceTheEpoch(DateTimeOffset value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateTimeOffset, EpochSecondsConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_InstantWithFraction_DropsTheFraction()
    {
        // Arrange
        var value = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero).AddTicks(TimeSpan.TicksPerSecond - 1);

        // Act
        var json = ConverterRun.Write<DateTimeOffset, EpochSecondsConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"1793448000"}""");
    }

    [Test]
    public async Task Write_MaximumInstant_WritesTheLastSecond()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateTimeOffset, EpochSecondsConverter>(DateTimeOffset.MaxValue);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"253402300799"}""");
    }

    [Test]
    [MethodDataSource(nameof(Instants))]
    public async Task Read_Seconds_ReturnsTheInstantInUtc(DateTimeOffset expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(json);

        // Assert
        await Assert.That((value.UtcTicks, value.Offset)).IsEqualTo((expected.UtcTicks, TimeSpan.Zero));
    }

    [Test]
    [Arguments("""{"N":"1E+3"}""", 1000L)]
    [Arguments("""{"N":"1000.0"}""", 1000L)]
    public async Task Read_WholeSecondsInOtherSpellings_ReturnsTheInstant(string json, long seconds)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(json);

        // Assert
        await Assert.That(value.ToUnixTimeSeconds()).IsEqualTo(seconds);
    }

    [Test]
    [Arguments("""{"N":"253402300800"}""")]
    [Arguments("""{"N":"-62135596801"}""")]
    [Arguments("""{"N":"9223372036854775807"}""")]
    [Arguments("""{"N":"-9223372036854775808"}""")]
    public async Task Read_SecondsOutsideTheRangeOfDateTimeOffset_ThrowsOverflowExceptionWithoutTheNumber(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>()
            .WithMessage("The number of seconds is outside the range of DateTimeOffset.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"N":"1.5"}""")]
    [Arguments("""{"N":"9223372036854775808"}""")]
    [Arguments("""{"N":"abc"}""")]
    public async Task Read_NumberThatIsNotWholeSeconds_ThrowsOverflowException(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>()
            .WithMessage("The number is malformed or does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotNumbers))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, EpochSecondsConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NullableOfTheConverter_ReadsNullForTheNullValue()
    {
        // Arrange (a nullable TTL property uses the converter as the inner converter of the nullable one)
        // Act
        var value = ConverterRun.Read<DateTimeOffset?, NullableConverter<DateTimeOffset, EpochSecondsConverter>>("""{"NULL":true}""");

        // Assert
        await Assert.That(value).IsNull();
    }
}
