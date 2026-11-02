using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DynamoDbNumberTests
{
    /// <summary>Texts whose value a decimal holds, however they spell it: at most 2^96 - 1 over a power of ten up to 1E+28, once the zeros that say nothing are gone.</summary>
    /// <returns>The text and the value.</returns>
    public static IEnumerable<(string Text, decimal Expected)> ExactDecimals() =>
    [
        ("1.10", 1.10m),
        ("+1.5", 1.5m),
        ("1E+2", 100m),
        ("1e2", 100m),
        ("0E+5", 0m),
        ("-0", 0m),
        ("0E-200", 0m),
        ("0.00000000000000000000000000000000000000", 0m),
        ("1E-28", 0.0000000000000000000000000001m),
        ("100E-30", 0.0000000000000000000000000001m),
        ("0.0000000000000000000000000001", 0.0000000000000000000000000001m),
        ("0.000000000000000000000000000000000000001E+39", 1m),
        ("1.0000000000000000000000000000", 1m),
        ("00000000000000000000000000000000001.5", 1.5m),
        ("1200.000000000000000000000000000000", 1200m),
        ("1234567890123456789012345678", 1234567890123456789012345678m),
        ("0.1234567890123456789012345678", 0.1234567890123456789012345678m),
        ("12345678901234567890123456789", 12345678901234567890123456789m),
        ("1.2345678901234567890123456789", 1.2345678901234567890123456789m),
        ("79228162514264337593543950335", decimal.MaxValue),
        ("-79228162514264337593543950335", decimal.MinValue),
        ("79228162514264337593543950335.000", decimal.MaxValue),
        ("792281625142643375935439503.35E+2", decimal.MaxValue),
        ("7.9228162514264337593543950335", 7.9228162514264337593543950335m),
        ("1E+28", 10000000000000000000000000000m),
    ];

    [Test]
    [MethodDataSource(nameof(ExactDecimals))]
    public async Task Parse_DecimalThatHoldsTheValue_ReturnsIt(string text, decimal expected)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(text);

        // Act
        var fromChars = DynamoDbNumber.Parse<decimal>(text.AsSpan());
        var fromBytes = DynamoDbNumber.Parse<decimal>(utf8);

        // Assert
        await Assert.That((fromChars, fromBytes)).IsEqualTo((expected, expected));
    }

    [Test]
    [Arguments("1E-29")]
    [Arguments("-1E-29")]
    [Arguments("1E-130")]
    [Arguments("1E-99999999999999999999")]
    [Arguments("0.00000000000000000000000000001")]
    [Arguments("0.12345678901234567890123456789012345678")]
    [Arguments("1.00000000000000000000000000000001")]
    [Arguments("7.9228162514264337593543950336")]
    [Arguments("8.0000000000000000000000000001")]
    [Arguments("0.79228162514264337593543950335")]
    [Arguments("7.9228162514264337593543950335E-1")]
    [Arguments("79228162514264337593543950334.5")]
    public async Task Parse_DecimalThatCannotHoldTheValue_ThrowsOverflowExceptionWithoutTheNumber(string text)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(text);

        // Act
        var fromChars = () => DynamoDbNumber.Parse<decimal>(text.AsSpan());
        var fromBytes = () => DynamoDbNumber.Parse<decimal>(utf8);

        // Assert
        await Assert.That(fromChars).ThrowsExactly<OverflowException>().WithMessage("The number does not fit the target type.", StringComparison.Ordinal);
        await Assert.That(fromBytes).ThrowsExactly<OverflowException>().WithMessage("The number does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("1E-29")]
    [Arguments("0.12345678901234567890123456789012345678")]
    public async Task TryParse_DecimalThatCannotHoldTheValue_ReturnsFalse(string text)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(text);

        // Act
        var fromChars = DynamoDbNumber.TryParse<decimal>(text.AsSpan(), out _);
        var fromBytes = DynamoDbNumber.TryParse<decimal>(utf8, out _);

        // Assert
        await Assert.That((fromChars, fromBytes)).IsEqualTo((false, false));
    }

    [Test]
    public async Task Parse_DecimalOfTheCommonForm_AllocatesNothing()
    {
        // Arrange (the check of what a decimal holds runs on every read, so it has to stay free in code the JIT has not optimized yet too)
        var utf8 = "1249.95"u8.ToArray();
        const string Text = "1249.95";

        // Act
        var fromBytes = Allocated(() => DynamoDbNumber.Parse<decimal>(utf8));
        var fromChars = Allocated(static () => DynamoDbNumber.Parse<decimal>(Text.AsSpan()));

        // Assert
        await Assert.That((fromBytes, fromChars)).IsEqualTo((0L, 0L));
    }

    [Test]
    public async Task TryParse_DecimalThatHoldsTheValue_ReturnsItsValue()
    {
        // Arrange
        var utf8 = "12.50"u8.ToArray();

        // Act
        var parsed = DynamoDbNumber.TryParse<decimal>(utf8, out var value);

        // Assert
        await Assert.That((parsed, value)).IsEqualTo((true, 12.5m));
    }

    [Test]
    [Arguments("1E400")]
    [Arguments("-1E400")]
    [Arguments("NaN")]
    [Arguments("Infinity")]
    [Arguments("-Infinity")]
    public async Task Parse_DoubleThatIsNotFinite_ThrowsOverflowExceptionWithoutTheNumber(string text)
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(text);

        // Act
        var fromChars = () => DynamoDbNumber.Parse<double>(text.AsSpan());
        var fromBytes = () => DynamoDbNumber.Parse<double>(utf8);

        // Assert
        await Assert.That(fromChars).ThrowsExactly<OverflowException>().WithMessage("The number does not fit the target type.", StringComparison.Ordinal);
        await Assert.That(fromBytes).ThrowsExactly<OverflowException>().WithMessage("The number does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Parse_SingleAndHalfThatRoundToInfinity_ThrowOverflowException()
    {
        // Arrange
        // Act
        var single = () => DynamoDbNumber.Parse<float>("1E39".AsSpan());
        var half = () => DynamoDbNumber.Parse<Half>("70000".AsSpan());

        // Assert
        await Assert.That(single).ThrowsExactly<OverflowException>();
        await Assert.That(half).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Parse_DoubleBelowItsRange_UnderflowsToZero()
    {
        // Arrange (what a binary float cannot tell from 0 is no failure, as the docs say)
        // Act
        var value = DynamoDbNumber.Parse<double>("1E-400".AsSpan());

        // Assert
        await Assert.That(value).IsEqualTo(0d);
    }

    [Test]
    public async Task Parse_DoubleOfMoreDigitsThanItHolds_RoundsToTheNearest()
    {
        // Arrange (DynamoDB keeps 38 digits, and a binary float rounds them, as the docs say)
        // Act
        var value = DynamoDbNumber.Parse<double>("0.12345678901234567890123456789012345678".AsSpan());

        // Assert
        await Assert.That(value).IsEqualTo(0.12345678901234568d);
    }

    [Test]
    public async Task TryParse_DoubleThatIsNotFinite_ReturnsFalse()
    {
        // Arrange
        var utf8 = "1E400"u8.ToArray();

        // Act
        var fromChars = DynamoDbNumber.TryParse<double>("NaN".AsSpan(), out _);
        var fromBytes = DynamoDbNumber.TryParse<double>(utf8, out _);

        // Assert
        await Assert.That((fromChars, fromBytes)).IsEqualTo((false, false));
    }

    [Test]
    public async Task TryFormat_ValueThatFits_WritesItsDigitsAsUtf8()
    {
        // Arrange
        var destination = new byte[16];

        // Act
        var formatted = DynamoDbNumber.TryFormat(-1234.5, destination, out var written);

        // Assert
        await Assert.That((formatted, Encoding.UTF8.GetString(destination, 0, written))).IsEqualTo((true, "-1234.5"));
    }

    [Test]
    public async Task TryFormat_DestinationTooSmall_ReturnsFalse()
    {
        // Arrange
        var destination = new byte[3];

        // Act
        var formatted = DynamoDbNumber.TryFormat(123456, destination, out var written);

        // Assert
        await Assert.That((formatted, written)).IsEqualTo((false, 0));
    }

    private static long Allocated(Func<decimal> act)
    {
        for (var i = 0; i < 64; i++)
        {
            act();
        }

        var smallest = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            act();
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }
}
