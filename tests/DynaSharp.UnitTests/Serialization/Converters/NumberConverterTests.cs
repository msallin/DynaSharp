using System.Numerics;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class NumberConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotNumbers() => AttributeSamples.Except(AttributeType.Number);

    public static IEnumerable<(decimal Value, string Json)> Decimals() =>
    [
        (decimal.MinValue, """{"N":"-79228162514264337593543950335"}"""),
        (decimal.MaxValue, """{"N":"79228162514264337593543950335"}"""),
        (0m, """{"N":"0"}"""),
        (12.50m, """{"N":"12.50"}"""),
        (-0.0000000000000000000000000001m, """{"N":"-0.0000000000000000000000000001"}"""),
        (0.1234567890123456789012345678m, """{"N":"0.1234567890123456789012345678"}"""),
        (1.0000000000000000000000000001m, """{"N":"1.0000000000000000000000000001"}"""),
    ];

    [Test]
    [Arguments(int.MinValue, """{"N":"-2147483648"}""")]
    [Arguments(-1, """{"N":"-1"}""")]
    [Arguments(0, """{"N":"0"}""")]
    [Arguments(1, """{"N":"1"}""")]
    [Arguments(1234567, """{"N":"1234567"}""")]
    [Arguments(int.MaxValue, """{"N":"2147483647"}""")]
    public async Task Write_Int32_WritesTheDigits(int value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<int, NumberConverter<int>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [Arguments(long.MinValue, """{"N":"-9223372036854775808"}""")]
    [Arguments(long.MaxValue, """{"N":"9223372036854775807"}""")]
    public async Task Write_Int64Limits_WritesTheDigits(long value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<long, NumberConverter<long>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_UInt64Maximum_WritesTheDigits()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<ulong, NumberConverter<ulong>>(ulong.MaxValue);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"18446744073709551615"}""");
    }

    [Test]
    public async Task Write_ByteAndSByteLimits_WriteTheDigits()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<byte, NumberConverter<byte>>(byte.MaxValue)
            + ConverterRun.Write<sbyte, NumberConverter<sbyte>>(sbyte.MinValue);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"255"}{"N":"-128"}""");
    }

    [Test]
    [MethodDataSource(nameof(Decimals))]
    public async Task Write_Decimal_KeepsEveryDigitAndTheScale(decimal value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<decimal, NumberConverter<decimal>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0.30000000000000004, """{"N":"0.30000000000000004"}""")]
    [Arguments(1.5, """{"N":"1.5"}""")]
    [Arguments(-0.0, """{"N":"-0"}""")]
    [Arguments(1e21, """{"N":"1E+21"}""")]
    [Arguments(double.MaxValue, """{"N":"1.7976931348623157E+308"}""")]
    [Arguments(double.Epsilon, """{"N":"5E-324"}""")]
    public async Task Write_Double_WritesTheShortestRoundTripText(double value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<double, NumberConverter<double>>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_SingleAndHalf_WriteTheShortestRoundTripText()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<float, NumberConverter<float>>(1.1f)
            + ConverterRun.Write<Half, NumberConverter<Half>>((Half)1.5f);

        // Assert
        await Assert.That(json).IsEqualTo("""{"N":"1.1"}{"N":"1.5"}""");
    }

    [Test]
    public async Task Write_Int128AndUInt128Limits_WriteTheDigits()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<Int128, NumberConverter<Int128>>(Int128.MaxValue)
            + ConverterRun.Write<UInt128, NumberConverter<UInt128>>(UInt128.MaxValue);

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"N":"170141183460469231731687303715884105727"}{"N":"340282366920938463463374607431768211455"}""");
    }

    [Test]
    public async Task Write_BigIntegerOfSeventyOneDigits_WritesAllDigits()
    {
        // Arrange: longer than the stack buffer of the writer
        var value = BigInteger.Pow(10, 70);

        // Act
        var json = ConverterRun.Write<BigInteger, NumberConverter<BigInteger>>(value);

        // Assert
        await Assert.That(json).IsEqualTo("{\"N\":\"1" + new string('0', 70) + "\"}");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async Task Write_NotFinite_ThrowsArgumentOutOfRangeException(double value)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Write<double, NumberConverter<double>>(value);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments("""{"N":"-2147483648"}""", int.MinValue)]
    [Arguments("""{"N":"-1"}""", -1)]
    [Arguments("""{"N":"0"}""", 0)]
    [Arguments("""{"N":"2147483647"}""", int.MaxValue)]
    [Arguments("""{"N":"1E+2"}""", 100)]
    [Arguments("""{"N":"100.0"}""", 100)]
    [Arguments("""{"N":"+5"}""", 5)]
    [Arguments("""{"N":"0005"}""", 5)]
    public async Task Read_Int32_ReturnsTheNumber(string json, int expected)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<int, NumberConverter<int>>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_Int64Limits_ReturnTheNumbers()
    {
        // Arrange
        // Act
        var minimum = ConverterRun.Read<long, NumberConverter<long>>("""{"N":"-9223372036854775808"}""");
        var maximum = ConverterRun.Read<long, NumberConverter<long>>("""{"N":"9223372036854775807"}""");

        // Assert
        await Assert.That((minimum, maximum)).IsEqualTo((long.MinValue, long.MaxValue));
    }

    [Test]
    public async Task Read_UInt64MaximumAndByteLimits_ReturnTheNumbers()
    {
        // Arrange
        // Act
        var unsigned = ConverterRun.Read<ulong, NumberConverter<ulong>>("""{"N":"18446744073709551615"}""");
        var small = ConverterRun.Read<byte, NumberConverter<byte>>("""{"N":"255"}""");
        var signed = ConverterRun.Read<sbyte, NumberConverter<sbyte>>("""{"N":"-128"}""");

        // Assert
        await Assert.That((unsigned, small, signed)).IsEqualTo((ulong.MaxValue, byte.MaxValue, sbyte.MinValue));
    }

    [Test]
    [MethodDataSource(nameof(Decimals))]
    public async Task Read_Decimal_ReturnsEveryDigit(decimal expected, string json)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<decimal, NumberConverter<decimal>>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_DecimalAsDynamoDbNormalizesIt_EqualsTheValueWritten()
    {
        // Arrange: DynamoDB stores 12.50 as 12.5, and 1E+2 as 100; compared by value they are the numbers that were written
        // Act
        var normalized = ConverterRun.Read<decimal, NumberConverter<decimal>>("""{"N":"12.5"}""");
        var exponent = ConverterRun.Read<decimal, NumberConverter<decimal>>("""{"N":"1E+2"}""");

        // Assert
        await Assert.That((normalized, exponent)).IsEqualTo((12.50m, 100m));
    }

    [Test]
    [Arguments("""{"N":"0.30000000000000004"}""", 0.30000000000000004)]
    [Arguments("""{"N":"1E+21"}""", 1e21)]
    [Arguments("""{"N":"0.0000001"}""", 1e-7)]
    [Arguments("""{"N":"-0"}""", 0.0)]
    [Arguments("""{"N":"1.7976931348623157E+308"}""", double.MaxValue)]
    [Arguments("""{"N":"5E-324"}""", double.Epsilon)]
    public async Task Read_Double_ReturnsTheNumber(string json, double expected)
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<double, NumberConverter<double>>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    public async Task Read_SingleAndHalf_ReturnTheNumbers()
    {
        // Arrange
        // Act
        var single = ConverterRun.Read<float, NumberConverter<float>>("""{"N":"1.1"}""");
        var half = ConverterRun.Read<Half, NumberConverter<Half>>("""{"N":"65504"}""");

        // Assert
        await Assert.That((single, half)).IsEqualTo((1.1f, Half.MaxValue));
    }

    [Test]
    public async Task Read_Int128AndUInt128Limits_ReturnTheNumbers()
    {
        // Arrange
        // Act
        var signed = ConverterRun.Read<Int128, NumberConverter<Int128>>("""{"N":"170141183460469231731687303715884105727"}""");
        var unsigned = ConverterRun.Read<UInt128, NumberConverter<UInt128>>("""{"N":"340282366920938463463374607431768211455"}""");

        // Assert
        await Assert.That((signed, unsigned)).IsEqualTo((Int128.MaxValue, UInt128.MaxValue));
    }

    [Test]
    public async Task Read_BigIntegerOfSeventyOneDigits_ReturnsTheNumber()
    {
        // Arrange
        var json = "{\"N\":\"1" + new string('0', 70) + "\"}";

        // Act
        var value = ConverterRun.Read<BigInteger, NumberConverter<BigInteger>>(json);

        // Assert
        await Assert.That(value).IsEqualTo(BigInteger.Pow(10, 70));
    }

    [Test]
    public async Task Read_NumberWithEscapedDigits_ReturnsTheNumber()
    {
        // Arrange: the digits 1 and 2 as JSON escapes, built from parts so that no tool turns them into characters
        var json = "{\"N\":\"\\u0031\\u0032\"}";

        // Act
        var value = ConverterRun.Read<int, NumberConverter<int>>(json);

        // Assert
        await Assert.That(value).IsEqualTo(12);
    }

    [Test]
    [Arguments("""{"N":"2147483648"}""")]
    [Arguments("""{"N":"-2147483649"}""")]
    [Arguments("""{"N":"1.5"}""")]
    [Arguments("""{"N":"abc"}""")]
    [Arguments("""{"N":""}""")]
    [Arguments("""{"N":" 5"}""")]
    public async Task Read_Int32ThatDoesNotFit_ThrowsOverflowExceptionWithoutTheNumber(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<int, NumberConverter<int>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>()
            .WithMessage("The number is malformed or does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NegativeForAnUnsignedType_ThrowsOverflowException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<byte, NumberConverter<byte>>("""{"N":"-1"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Read_DecimalBeyondTheType_ThrowsOverflowException()
    {
        // Arrange: 30 digits
        var json = "{\"N\":\"" + new string('9', 30) + "\"}";

        // Act
        var act = () => ConverterRun.Read<decimal, NumberConverter<decimal>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    [Arguments("""{"N":"1E39"}""")]
    [Arguments("""{"N":"-1E39"}""")]
    public async Task Read_SingleThatRoundsToInfinity_ThrowsOverflowException(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<float, NumberConverter<float>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Read_DoubleBeyondTheType_ThrowsOverflowException()
    {
        // Arrange (DynamoDB stops at 1E+126, so only a corrupt response has this)
        // Act
        var act = () => ConverterRun.Read<double, NumberConverter<double>>("""{"N":"1E999"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Read_HalfBeyondTheType_ThrowsOverflowException()
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<Half, NumberConverter<Half>>("""{"N":"100000"}""");

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    [MethodDataSource(nameof(NotNumbers))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<int, NumberConverter<int>>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NumbersOfKnownType_AllocateNothing()
    {
        // Arrange (a first pass compiles what the second one measures)
        var json = """{"N":"1234567"}"""u8.ToArray();
        ReadMany(json);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        ReadMany(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static void ReadMany(byte[] json)
    {
        for (var i = 0; i < 100; i++)
        {
            var reader = new DynamoDbReader(json);
            _ = reader.ReadAttribute<int, NumberConverter<int>>();
            reader = new DynamoDbReader(json);
            _ = reader.ReadAttribute<decimal, NumberConverter<decimal>>();
            reader = new DynamoDbReader(json);
            _ = reader.ReadAttribute<double, NumberConverter<double>>();
        }
    }
}
