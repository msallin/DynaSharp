using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Properties;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// The rules of <see cref="DynamoDbNumber"/> against an oracle in exact arithmetic over <see cref="BigInteger"/>: a decimal is read only if it holds the number exactly,
/// a JSON number fits DynamoDB by its significant digits and the power of its first one, and the grammar of JSON is the one of RFC 8259.
/// </summary>
public sealed partial class DynamoDbNumberPropertyTests
{
    private static readonly BigInteger LargestCoefficient = (BigInteger.One << 96) - 1;

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(NumberTexts)])]
    public async Task TryParseDecimal_AnyNumberText_ReadsExactlyTheNumberOrRefusesIt(ParsableNumber number)
    {
        // Arrange (up to 45 digits, leading and trailing zeros, a sign of either kind and an exponent of any size)
        var exact = ExactDecimal(number.Text);

        // Act
        decimal? fromUtf8 = DynamoDbNumber.TryParse<decimal>(Encoding.UTF8.GetBytes(number.Text), out var utf8Value) ? utf8Value : null;
        decimal? fromChars = DynamoDbNumber.TryParse<decimal>(number.Text.AsSpan(), out var charValue) ? charValue : null;

        // Assert
        await Assert.That((fromUtf8, fromChars)).IsEqualTo((exact, exact));
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(NumberTexts)])]
    public async Task FitsDynamoDb_AnyJsonNumber_CountsDigitsAndMagnitudeAsDynamoDbDoes(JsonNumber number)
    {
        // Arrange
        var expected = ExpectedFit(number.Text);

        // Act
        var fit = DynamoDbNumber.FitsDynamoDb(Encoding.UTF8.GetBytes(number.Text));

        // Assert
        await Assert.That(fit).IsEqualTo(expected);
    }

    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(NumberTexts)])]
    public async Task IsJsonNumber_AnyTextOfNumberCharacters_AgreesWithTheGrammarOfJson(NumberLikeText text)
    {
        // Arrange
        var expected = JsonNumberGrammar().IsMatch(text.Text);

        // Act
        var isNumber = DynamoDbNumber.IsJsonNumber(Encoding.UTF8.GetBytes(text.Text));

        // Assert
        await Assert.That(isNumber).IsEqualTo(expected);
    }

    // The value of the text if a decimal holds it exactly: a coefficient below 2^96 at a scale up to 28, once trailing zeros are gone.
    private static decimal? ExactDecimal(string text)
    {
        var (negative, coefficient, exponent) = Split(text);
        if (coefficient.IsZero)
        {
            return 0m;
        }

        while ((coefficient % 10).IsZero)
        {
            coefficient /= 10;
            exponent++;
        }

        if (exponent > 0)
        {
            if (exponent > 29)
            {
                return null;
            }

            coefficient *= BigInteger.Pow(10, (int)exponent);
            exponent = 0;
        }

        if (exponent < -28 || coefficient > LargestCoefficient)
        {
            return null;
        }

        var bytes = coefficient.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Resize(ref bytes, 12);
        return new decimal(BitConverter.ToInt32(bytes, 0), BitConverter.ToInt32(bytes, 4), BitConverter.ToInt32(bytes, 8), negative, (byte)(int)-exponent);
    }

    private static NumberFit ExpectedFit(string text)
    {
        var (_, coefficient, exponent) = Split(text);
        if (coefficient.IsZero)
        {
            return NumberFit.Fits;
        }

        var digits = coefficient.ToString(CultureInfo.InvariantCulture);
        var significant = digits.TrimEnd('0').Length;
        var powerOfFirst = digits.Length - 1 + exponent;
        return significant > 38 ? NumberFit.TooManyDigits : powerOfFirst > 125 ? NumberFit.TooLarge : powerOfFirst < -130 ? NumberFit.TooSmall : NumberFit.Fits;
    }

    // [+-]units[.fraction][e[+-]exponent] as sign, coefficient and power of ten.
    private static (bool Negative, BigInteger Coefficient, BigInteger Exponent) Split(string text)
    {
        var negative = text[0] == '-';
        var rest = text.TrimStart('-', '+');
        var exponentAt = rest.IndexOfAny(['e', 'E']);
        var exponent = exponentAt < 0 ? BigInteger.Zero : BigInteger.Parse(rest[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = exponentAt < 0 ? rest : rest[..exponentAt];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var fraction = point < 0 ? 0 : mantissa.Length - point - 1;
        return (negative, BigInteger.Parse("0" + digits, CultureInfo.InvariantCulture), exponent - fraction);
    }

    [GeneratedRegex(@"\A-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z")]
    private static partial Regex JsonNumberGrammar();

    /// <summary>The text of a number in the grammar <c>decimal.Parse</c> takes with the styles of DynamoDB numbers.</summary>
    /// <param name="Text">The text.</param>
    public readonly record struct ParsableNumber(string Text);

    /// <summary>The text of a number in the grammar of JSON.</summary>
    /// <param name="Text">The text.</param>
    public readonly record struct JsonNumber(string Text);

    /// <summary>Text of the characters numbers are made of, in any order.</summary>
    /// <param name="Text">The text.</param>
    public readonly record struct NumberLikeText(string Text);

    internal static class NumberTexts
    {
        // Half of the numbers lie around the limits of a decimal, 28 to 30 digits at a scale of 27 to 29, where a digit more or less decides.
        public static Arbitrary<ParsableNumber> Parsable() => (
            from sign in Gen.Elements(string.Empty, "-", "+")
            from units in Gen.Frequency((1, Generate.Digits(1, 45)), (1, Gen.Elements("0", "1", "7", "00")), (1, Generate.Digits(27, 31)))
            from fraction in Gen.Frequency((1, Gen.Constant(string.Empty)), (1, Generate.Digits(1, 45).Select(static digits => "." + digits)), (2, Generate.Digits(26, 31).Select(static digits => "." + digits)))
            from exponent in Gen.Frequency((2, Exponent()), (1, Gen.Choose(-3, 3).Select(static power => "e" + power.ToString(CultureInfo.InvariantCulture))))
            select new ParsableNumber(sign + units + fraction + exponent)).ToArbitrary();

        // Many numbers have 36 to 40 significant digits, around the 38 DynamoDB stores.
        public static Arbitrary<JsonNumber> Json() => (
            from sign in Gen.Elements(string.Empty, "-")
            from units in Gen.Frequency((1, Gen.Constant("0")), (3, from first in Gen.Choose('1', '9') from rest in Gen.OneOf(Generate.Digits(0, 45), Generate.Digits(35, 39)) select (char)first + rest))
            from fraction in Gen.Frequency((1, Gen.Constant(string.Empty)), (2, Generate.Digits(1, 45).Select(static digits => "." + digits)), (1, from zeros in Generate.Digits(0, 3) from digits in Generate.Digits(36, 40) select "." + zeros + digits))
            from exponent in Exponent()
            select new JsonNumber(sign + units + fraction + exponent)).ToArbitrary();

        public static Arbitrary<NumberLikeText> NumberLike() =>
            Gen.ListOf(Gen.Elements('0', '1', '9', '-', '+', '.', 'e', 'E', ' ', 'x'), 10).Select(static characters => new NumberLikeText(new string([.. characters]))).ToArbitrary();

        // Exponents around the limits of decimal and DynamoDB, and some far beyond any limit, which must not overflow a count.
        private static Gen<string> Exponent() => Gen.Frequency(
            (2, Gen.Constant(string.Empty)),
            (4, from letter in Gen.Elements("e", "E")
                from sign in Gen.Elements(string.Empty, "+", "-")
                from digits in Gen.Choose(0, 140).Select(static power => power.ToString(CultureInfo.InvariantCulture))
                select letter + sign + digits),
            (1, from sign in Gen.Elements("+", "-") from digits in Generate.Digits(13, 30) select "E" + sign + digits));
    }
}
