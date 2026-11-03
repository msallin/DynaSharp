using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DynaSharp.Serialization;

/// <summary>
/// The rules for DynamoDB numbers, which travel as text: invariant culture, default (shortest round-trip) format,
/// no NaN or infinity. Every read of a number comes through here, and it returns the number or fails.
/// </summary>
/// <remarks>
/// A <see cref="decimal"/> is a whole number below 2^96 over a power of ten up to 1E+28, which is 28 to 29 significant digits, and DynamoDB holds 38. <c>decimal.Parse</c> rounds what
/// does not fit, and goes to 0 below its range, so the text decides here whether the value survived. Text that cannot lose anything skips that, and it is what nearly all numbers look like.
/// </remarks>
internal static class DynamoDbNumber
{
    // Integer types accept "1E+2" or "1.0" as long as the value is integral. No whitespace: DynamoDB numbers have none.
    private const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    // Without an exponent, text of this length with a point has at most 28 digits, and without one it is a whole number, which the range check of the parser covers.
    private const int LongestSafeDecimalText = 29;

    private const int DecimalLargestScale = 28;

    // Far beyond any exponent that can matter, and small enough that adding to it stays exact.
    private const long ExponentLimit = 1_000_000_000_000;

    private const string NoFit = "The number does not fit the target type.";

    // 2^96 - 1, the largest whole number a decimal holds, and so the one number of 29 digits that decides.
    private static ReadOnlySpan<byte> LargestDecimalCoefficient => "79228162514264337593543950335"u8;

    internal static void ThrowIfNotFinite<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumberBase<T>
    {
        if (!T.IsFinite(value))
        {
            throw new NonFiniteNumberException(paramName, value);
        }
    }

    // The arguments are locals on purpose: with a property call or a temporary among them, the compiler guards against
    // `value` changing during argument evaluation and copies it for reference types, a branch number types never take.
    internal static string Format<T>(T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return value.ToString(null, invariant);
    }

    internal static bool TryFormat<T>(T value, Span<byte> utf8Destination, out int bytesWritten)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        ReadOnlySpan<char> format = default;
        return value.TryFormat(utf8Destination, out bytesWritten, format, invariant);
    }

    internal static T Parse<T>(ReadOnlySpan<byte> utf8Text)
        where T : INumberBase<T>
    {
        var value = T.Parse(utf8Text, Styles, CultureInfo.InvariantCulture);
        return Fits(value, utf8Text) ? value : throw new OverflowException(NoFit);
    }

    internal static T Parse<T>(ReadOnlySpan<char> text)
        where T : INumberBase<T>
    {
        var value = T.Parse(text, Styles, CultureInfo.InvariantCulture);
        return Fits(value, text) ? value : throw new OverflowException(NoFit);
    }

    // Parse throws a FormatException that quotes the text; converters must not leak values into messages, so they ask this instead.
    internal static bool TryParse<T>(ReadOnlySpan<byte> utf8Text, [MaybeNullWhen(false)] out T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return T.TryParse(utf8Text, Styles, invariant, out value) && Fits(value, utf8Text);
    }

    internal static bool TryParse<T>(ReadOnlySpan<char> text, [MaybeNullWhen(false)] out T value)
        where T : INumberBase<T>
    {
        var invariant = CultureInfo.InvariantCulture;
        return T.TryParse(text, Styles, invariant, out value) && Fits(value, text);
    }

    // Whether the value of text the parser accepted survived it. A binary float saturates at infinity and reads the text "NaN", and rounding and an underflow to 0 are what it is. The test of the type folds away in the code of each number type.
    private static bool Fits<T, TChar>(T value, ReadOnlySpan<TChar> text)
        where T : INumberBase<T>
        where TChar : IBinaryInteger<TChar>
    {
        if (typeof(T) != typeof(decimal))
        {
            return T.IsFinite(value);
        }

        var cannotLoseDigits = text.Length <= LongestSafeDecimalText && !text.ContainsAny(TChar.CreateTruncating('E'), TChar.CreateTruncating('e'));
        return cannotLoseDigits || DecimalHoldsExactly(text);
    }

    // Takes text the parser accepted. Input: "1200.50E-3". Digits that count: 12005, the last of them at the power of ten -1 + -3 = -4, which is a scale of 4.
    private static bool DecimalHoldsExactly<TChar>(ReadOnlySpan<TChar> text)
        where TChar : IBinaryInteger<TChar>
    {
        var exponentAt = text.IndexOfAny(TChar.CreateTruncating('E'), TChar.CreateTruncating('e'));
        var mantissa = exponentAt < 0 ? text : text[..exponentAt];
        var first = mantissa.IndexOfAnyInRange(TChar.CreateTruncating('1'), TChar.CreateTruncating('9'));
        if (first < 0)
        {
            return true;
        }

        var last = mantissa.LastIndexOfAnyInRange(TChar.CreateTruncating('1'), TChar.CreateTruncating('9'));
        var point = mantissa.IndexOf(TChar.CreateTruncating('.'));
        var afterUnits = point < 0 ? mantissa.Length : point;
        long powerOfLast = last < afterUnits ? afterUnits - 1 - last : afterUnits - last;
        if (exponentAt >= 0)
        {
            powerOfLast += ReadExponent(text[(exponentAt + 1)..]);
        }

        if (powerOfLast < -DecimalLargestScale)
        {
            return false;
        }

        var digits = mantissa[first..(last + 1)];
        var count = first < point && point < last ? digits.Length - 1 : digits.Length;
        return count < LargestDecimalCoefficient.Length || (count == LargestDecimalCoefficient.Length && FitsDecimalCoefficient(digits));
    }

    private static long ReadExponent<TChar>(ReadOnlySpan<TChar> text)
        where TChar : IBinaryInteger<TChar>
    {
        var negative = text[0] == TChar.CreateTruncating('-');
        var exponent = 0L;
        foreach (var character in text)
        {
            if (character >= TChar.CreateTruncating('0'))
            {
                exponent = Math.Min((exponent * 10) + (int.CreateTruncating(character) - '0'), ExponentLimit);
            }
        }

        return negative ? -exponent : exponent;
    }

    private static bool FitsDecimalCoefficient<TChar>(ReadOnlySpan<TChar> digits)
        where TChar : IBinaryInteger<TChar>
    {
        var position = 0;
        foreach (var digit in digits)
        {
            if (digit == TChar.CreateTruncating('.'))
            {
                continue;
            }

            var difference = int.CreateTruncating(digit) - LargestDecimalCoefficient[position++];
            if (difference != 0)
            {
                return difference < 0;
            }
        }

        return true;
    }
}
