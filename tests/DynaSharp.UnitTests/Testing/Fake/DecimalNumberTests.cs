using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The limits and the forms are those of the Developer Guide, "Naming rules and data types", Number: up to 38 digits of precision, positive values from
// 1E-130 to 9.9999999999999999999999999999999999999E+125, negative values the same, leading and trailing zeros trimmed.
public sealed class DecimalNumberTests
{
    private const string NotANumber = "The parameter cannot be converted to a numeric value: ";
    private const string TooManyDigits = "Attempting to store more than 38 significant digits in a Number";
    private const string Overflow = "Number overflow. Attempting to store a number with magnitude larger than supported range";
    private const string Underflow = "Number underflow. Attempting to store a number with magnitude smaller than supported range";

    [Test]
    [Arguments("0", "0")]
    [Arguments("-0", "0")]
    [Arguments("0.000", "0")]
    [Arguments("007", "7")]
    [Arguments("+3", "3")]
    [Arguments("1.50", "1.5")]
    [Arguments(".5", "0.5")]
    [Arguments("5.", "5")]
    [Arguments("-0.05", "-0.05")]
    [Arguments("123.456", "123.456")]
    [Arguments("-12.500e+3", "-12500")]
    [Arguments("1E2", "100")]
    [Arguments("1e-3", "0.001")]
    [Arguments("1E+39", "1000000000000000000000000000000000000000")]
    [Arguments("0.0000000000000000000000000000000000001", "0.0000000000000000000000000000000000001")]
    [Arguments("12345678901234567890123456789012345678", "12345678901234567890123456789012345678")]
    public async Task Parse_ValidText_PrintsTheCanonicalForm(string text, string expected)
    {
        // Arrange
        // Act
        var number = DecimalNumber.Parse(text);

        // Assert
        await Assert.That(number.ToString()).IsEqualTo(expected);
    }

    // The integration suite pins these ends of the range against DynamoDB Local, Dynoxide and DynamoDB: the answer is plain digits, never an exponent.
    [Test]
    [Arguments("1E+40", "1", 40, "")]
    [Arguments("1000000000000000000000000000000000000000000000000", "1", 48, "")]
    [Arguments("9.9999999999999999999999999999999999999E+125", "99999999999999999999999999999999999999", 88, "")]
    [Arguments("-9.9999999999999999999999999999999999999E+125", "-99999999999999999999999999999999999999", 88, "")]
    [Arguments("1E-130", "0.", 129, "1")]
    [Arguments("-1.5E-40", "-0.", 39, "15")]
    public async Task Parse_EndOfTheRange_PrintsPlainDigits(string text, string head, int zeros, string tail)
    {
        // Arrange
        var expected = head + new string('0', zeros) + tail;

        // Act
        var number = DecimalNumber.Parse(text);

        // Assert
        await Assert.That(number.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(".")]
    [Arguments("-")]
    [Arguments("e5")]
    [Arguments("1e")]
    [Arguments("1e+")]
    [Arguments("1e5x")]
    [Arguments("1x")]
    [Arguments("1.2.3")]
    [Arguments(" 1")]
    [Arguments("abc")]
    [Arguments("NaN")]
    [Arguments("Infinity")]
    public async Task Parse_NoNumber_ThrowsValidationNamingTheText(string text)
    {
        // Arrange
        // Act
        var act = () => DecimalNumber.Parse(text);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(NotANumber + text, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("123456789012345678901234567890123456789", TooManyDigits)]
    [Arguments("1.23456789012345678901234567890123456789", TooManyDigits)]
    [Arguments("1E+126", Overflow)]
    [Arguments("-1E+126", Overflow)]
    [Arguments("1E+99999999999", Overflow)]
    [Arguments("1E-131", Underflow)]
    [Arguments("1E-99999999999", Underflow)]
    [Arguments("0e126", Overflow)]
    [Arguments("0e-131", Underflow)]
    [Arguments("1e2147483647", Overflow)]
    public async Task Parse_BeyondTheLimits_ThrowsValidation(string text, string message)
    {
        // Arrange
        // Act
        var act = () => DecimalNumber.Parse(text);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("0e125")]
    [Arguments("0e-130")]
    public async Task Parse_ZeroWithAnExponentInsideTheRange_IsZero(string text)
    {
        // Arrange
        // Act
        var number = DecimalNumber.Parse(text).ToString();

        // Assert
        await Assert.That(number).IsEqualTo("0");
    }

    [Test]
    [Arguments("1e9223372036854775808")]
    [Arguments("1e-9223372036854775808")]
    [Arguments("0e99999999999999999999")]
    public async Task Parse_ExponentPast64Bits_IsNoNumber(string text)
    {
        // Arrange
        // Act
        var act = () => DecimalNumber.Parse(text);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(NotANumber + text, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("1e9223372036854775807")]
    [Arguments("-1e9223372036854775807")]
    public async Task Parse_ExponentOfTheLargest64BitInteger_IsReadAndOutOfRange(string text)
    {
        // Arrange
        // Act
        var act = () => DecimalNumber.Parse(text);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Overflow, StringComparison.Ordinal);
    }

    // The literals measured against DynamoDB (Parity tier3/limits/numberByteSize, eu-west-2, 2026-08-18) and what each cost in an item.
    [Test]
    [Arguments("0", 1)]
    [Arguments("1", 2)]
    [Arguments("12", 2)]
    [Arguments("123", 3)]
    [Arguments("1234", 3)]
    [Arguments("12345678901234567890123456789012345678", 20)]
    [Arguments("0042", 2)]
    [Arguments("100", 2)]
    [Arguments("1010", 3)]
    [Arguments("0.0000001", 2)]
    [Arguments("1E125", 2)]
    [Arguments("1E-100", 2)]
    [Arguments("1.5", 3)]
    [Arguments("15", 2)]
    [Arguments("1.2", 3)]
    [Arguments("1.200", 3)]
    [Arguments("1.234", 4)]
    [Arguments("3.14159", 5)]
    [Arguments("123456", 4)]
    [Arguments("100.5", 4)]
    [Arguments("0.15", 2)]
    [Arguments("-42", 3)]
    [Arguments("-0", 1)]
    public async Task ByteSize_NumberMeasuredOnDynamoDb_IsWhatItCost(string text, int bytes)
    {
        // Arrange
        var number = DecimalNumber.Parse(text);

        // Act
        var size = number.ByteSize;

        // Assert
        await Assert.That(size).IsEqualTo(bytes);
    }

    [Test]
    public async Task Parse_Null_ThrowsArgumentNullException()
    {
        // Arrange
        string text = null!;

        // Act
        var act = () => DecimalNumber.Parse(text);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Parse_TrailingZerosOf39Digits_KeepsOneSignificantDigit()
    {
        // Arrange
        const string Text = "100000000000000000000000000000000000000";

        // Act
        var number = DecimalNumber.Parse(Text);

        // Assert
        await Assert.That(number.SignificantDigits).IsEqualTo(1);
    }

    [Test]
    [Arguments("0", 1)]
    [Arguments("7", 1)]
    [Arguments("123.45", 5)]
    [Arguments("-0.0012", 2)]
    public async Task SignificantDigits_Number_CountsWithoutLeadingAndTrailingZeros(string text, int expected)
    {
        // Arrange
        var number = DecimalNumber.Parse(text);

        // Act
        var digits = number.SignificantDigits;

        // Assert
        await Assert.That(digits).IsEqualTo(expected);
    }

    [Test]
    [Arguments("0.1", "0.2", "0.3")]
    [Arguments("99999999999999999999999999999999999999", "1", "100000000000000000000000000000000000000")]
    [Arguments("-5", "2", "-3")]
    [Arguments("1E+125", "-1E+125", "0")]
    [Arguments("12", "0", "12")]
    public async Task Add_TwoNumbers_GivesTheExactSum(string left, string right, string expected)
    {
        // Arrange
        var number = DecimalNumber.Parse(left);

        // Act
        var sum = number.Add(DecimalNumber.Parse(right));

        // Assert
        await Assert.That(sum.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("1", "1E-40", Overflow)]
    [Arguments("9E+125", "9E+125", Overflow)]
    public async Task Add_SumBeyondTheLimits_ThrowsValidation(string left, string right, string message)
    {
        // Arrange
        var number = DecimalNumber.Parse(left);

        // Act
        var act = () => number.Add(DecimalNumber.Parse(right));

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("5", "7", "-2")]
    [Arguments("1", "1", "0")]
    [Arguments("0.3", "0.1", "0.2")]
    public async Task Subtract_TwoNumbers_GivesTheExactDifference(string left, string right, string expected)
    {
        // Arrange
        var number = DecimalNumber.Parse(left);

        // Act
        var difference = number.Subtract(DecimalNumber.Parse(right));

        // Assert
        await Assert.That(difference.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("1", "1.0", 0)]
    [Arguments("-1", "1", -1)]
    [Arguments("2", "10", -1)]
    [Arguments("-2", "-10", 1)]
    [Arguments("0", "-0.001", 1)]
    [Arguments("1E+125", "9E+124", 1)]
    [Arguments("0.1", "0.10000000000000000000000000000000000001", -1)]
    public async Task CompareTo_TwoNumbers_OrdersByValue(string left, string right, int expected)
    {
        // Arrange
        var number = DecimalNumber.Parse(left);

        // Act
        var order = number.CompareTo(DecimalNumber.Parse(right));

        // Assert
        await Assert.That(Math.Sign(order)).IsEqualTo(expected);
    }

    [Test]
    public async Task Operators_SmallerAndLarger_AnswerAsTheOrder()
    {
        // Arrange
        var small = DecimalNumber.Parse("1.5");
        var large = DecimalNumber.Parse("2");

        // Act
        bool[] answers = [small < large, small <= large, large > small, large >= small, small != large, small == DecimalNumber.Parse("1.50")];

        // Assert
        await Assert.That(answers.All(static answer => answer)).IsTrue();
    }

    [Test]
    public async Task Operators_EqualNumbers_AnswerAsEqual()
    {
        // Arrange
        var one = DecimalNumber.Parse("1");
        var other = DecimalNumber.Parse("1.0");

        // Act
        bool[] answers = [one < other, one > other, one != other, !(one <= other), !(one >= other)];

        // Assert
        await Assert.That(answers.Any(static answer => answer)).IsFalse();
    }

    [Test]
    public async Task Equals_SameValueAsObject_IsTrueAndHashesAlike()
    {
        // Arrange
        object other = DecimalNumber.Parse("10.0");
        var number = DecimalNumber.Parse("1E1");

        // Act
        var equal = number.Equals(other) && number.GetHashCode() == other.GetHashCode();

        // Assert
        await Assert.That(equal).IsTrue();
    }

    [Test]
    public async Task Equals_OtherType_IsFalse()
    {
        // Arrange
        object other = "1";

        // Act
        var equal = DecimalNumber.Parse("1").Equals(other);

        // Assert
        await Assert.That(equal).IsFalse();
    }

    [Test]
    [Arguments(0L, "0")]
    [Arguments(1767225600L, "1767225600")]
    [Arguments(long.MinValue, "-9223372036854775808")]
    [Arguments(long.MaxValue, "9223372036854775807")]
    public async Task FromInt64_Integer_PrintsIt(long value, string expected)
    {
        // Arrange
        // Act
        var number = DecimalNumber.FromInt64(value);

        // Assert
        await Assert.That(number.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("-3", -1)]
    [Arguments("0", 0)]
    [Arguments("0.5", 1)]
    public async Task Sign_Number_IsItsSign(string text, int expected)
    {
        // Arrange
        var number = DecimalNumber.Parse(text);

        // Act
        var sign = number.Sign;

        // Assert
        await Assert.That(sign).IsEqualTo(expected);
    }

    [Test]
    public async Task Zero_Default_IsZero()
    {
        // Arrange
        // Act
        var zero = DecimalNumber.Zero;

        // Assert
        await Assert.That(zero).IsEqualTo(DecimalNumber.Parse("-0.0"));
    }
}
