using System.Globalization;
using System.Numerics;
using System.Text;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A DynamoDB number with every digit it has: a coefficient without trailing zeros and a power of ten, so <c>12.50</c> is <c>125 × 10^-1</c>. It is how the fake compares, adds and
/// prints numbers, with the limits of DynamoDB: at most 38 significant digits, and a magnitude from <c>1E-130</c> to just below <c>1E+126</c>.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>DynamoDbNumber</c> of the client, which parses into CLR types and loses what <see cref="decimal"/> cannot hold. The form is unique, because the coefficient keeps no trailing
/// zero and zero has the exponent 0, so equality is the equality of the two fields.
/// </para>
/// <para>
/// <see cref="ToString"/> is the canonical text the fake stores and answers with: no leading zeros, no trailing zeros after the point, no <c>+</c>, and <c>0</c> for every zero.
/// The text is always plain digits, never an exponent, also at the ends of the range: <c>1E-130</c> prints as <c>0.</c>, 129 zeros and <c>1</c>, as DynamoDB answers it.
/// </para>
/// </remarks>
internal readonly struct DecimalNumber : IEquatable<DecimalNumber>, IComparable<DecimalNumber>
{
    /// <summary>The most significant digits DynamoDB keeps.</summary>
    public const int MaxDigits = 38;

    // The exponent of the leading digit: 9.99...E+125 is the largest magnitude, 1E-130 the smallest.
    private const int MaxLeadingExponent = 125;
    private const int MinLeadingExponent = -130;

    private const string Overflow = "Number overflow. Attempting to store a number with magnitude larger than supported range";

    // An exponent beyond this cannot be brought back into range by any coefficient DynamoDB accepts, so parsing stops counting there.
    private const long ExponentCap = 100_000;

    private DecimalNumber(BigInteger coefficient, int exponent)
    {
        Coefficient = coefficient;
        Exponent = exponent;
    }

    /// <summary>Gets the zero.</summary>
    public static DecimalNumber Zero => default;

    /// <summary>Gets the coefficient: no trailing zero, and 0 for zero.</summary>
    public BigInteger Coefficient { get; }

    /// <summary>Gets the power of ten the coefficient is multiplied by, 0 for zero.</summary>
    public int Exponent { get; }

    /// <summary>Gets -1, 0 or 1.</summary>
    public int Sign => Coefficient.Sign;

    /// <summary>Gets the number of significant digits, 1 for zero, which is what the size of a number in an item counts.</summary>
    public int SignificantDigits => Coefficient.IsZero ? 1 : BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture).Length;

    public static bool operator ==(DecimalNumber left, DecimalNumber right) => left.Equals(right);

    public static bool operator !=(DecimalNumber left, DecimalNumber right) => !left.Equals(right);

    public static bool operator <(DecimalNumber left, DecimalNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(DecimalNumber left, DecimalNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(DecimalNumber left, DecimalNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(DecimalNumber left, DecimalNumber right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Parses the text of a number as DynamoDB takes it: an optional sign, digits with an optional point, and an optional exponent.
    /// </summary>
    /// <remarks>
    /// Input <c>"-0012.500e+3"</c> gives the coefficient -125 and the exponent 2, which prints as <c>-12500</c>. <c>".5"</c> and <c>"5."</c> are numbers, <c>""</c>, <c>"."</c>, <c>"1e"</c>,
    /// <c>"NaN"</c>, <c>"Infinity"</c> and text with spaces are not.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: the text is no number, has more than 38 significant digits, or is out of range.</exception>
    public static DecimalNumber Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!TryParseParts(text, out var digits, out var exponent))
        {
            throw FakeError.Validation($"The parameter cannot be converted to a numeric value: {text}");
        }

        var negative = text[0] == '-';
        var coefficient = digits.Length == 0 ? BigInteger.Zero : BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return Create(negative ? -coefficient : coefficient, exponent, "Attempting to store more than 38 significant digits in a Number");
    }

    /// <summary>Makes a number from an integer.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The number.</returns>
    public static DecimalNumber FromInt64(long value) => Create(value, 0, Overflow);

    /// <summary>Adds two numbers exactly.</summary>
    /// <param name="other">The other addend.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="FakeError">
    /// A <c>ValidationException</c>: the sum is out of range, or has more than 38 significant digits, which real DynamoDB reports as a number overflow too (<c>FidelityTests</c>, run 42356511937).
    /// </exception>
    public DecimalNumber Add(DecimalNumber other)
    {
        var exponent = Math.Min(Exponent, other.Exponent);
        var sum = Scale(Coefficient, Exponent - exponent) + Scale(other.Coefficient, other.Exponent - exponent);
        return Create(sum, exponent, Overflow);
    }

    /// <summary>Subtracts a number exactly.</summary>
    /// <param name="other">The subtrahend.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: the difference is out of range or has more than 38 significant digits, see <see cref="Add"/>.</exception>
    public DecimalNumber Subtract(DecimalNumber other) => Add(new DecimalNumber(-other.Coefficient, other.Exponent));

    /// <inheritdoc/>
    public int CompareTo(DecimalNumber other)
    {
        if (Sign != other.Sign)
        {
            return Sign.CompareTo(other.Sign);
        }

        var exponent = Math.Min(Exponent, other.Exponent);
        return Scale(Coefficient, Exponent - exponent).CompareTo(Scale(other.Coefficient, other.Exponent - exponent));
    }

    /// <inheritdoc/>
    public bool Equals(DecimalNumber other) => Exponent == other.Exponent && Coefficient == other.Coefficient;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DecimalNumber other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Coefficient, Exponent);

    /// <summary>Gets the canonical text, see the remarks of the type.</summary>
    /// <returns>The text: <c>0</c>, <c>-12.5</c>, <c>12500</c>, <c>0.001</c>.</returns>
    public override string ToString()
    {
        if (Coefficient.IsZero)
        {
            return "0";
        }

        var digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
        var sign = Coefficient.Sign < 0 ? "-" : string.Empty;
        var count = digits.Length;
        if (Exponent >= 0)
        {
            return sign + digits + new string('0', Exponent);
        }

        var point = count + Exponent;
        return point > 0
            ? sign + digits[..point] + "." + digits[point..]
            : sign + "0." + new string('0', -point) + digits;
    }

    // Normalizes and checks the limits of DynamoDB. The exponent is a long, so that a sum or a parsed exponent cannot wrap before the check.
    private static DecimalNumber Create(BigInteger coefficient, long exponent, string tooManyDigits)
    {
        if (coefficient.IsZero)
        {
            return Zero;
        }

        while (coefficient % 10 == 0)
        {
            coefficient /= 10;
            exponent++;
        }

        var count = BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture).Length;
        if (count > MaxDigits)
        {
            throw FakeError.Validation(tooManyDigits);
        }

        var leading = exponent + count - 1;
        if (leading > MaxLeadingExponent)
        {
            throw FakeError.Validation(Overflow);
        }

        if (leading < MinLeadingExponent)
        {
            throw FakeError.Validation("Number underflow. Attempting to store a number with magnitude smaller than supported range");
        }

        return new DecimalNumber(coefficient, (int)exponent);
    }

    private static BigInteger Scale(BigInteger coefficient, int powerOfTen) => powerOfTen == 0 ? coefficient : coefficient * BigInteger.Pow(10, powerOfTen);

    // Splits "-0012.500e+3" into the digits without leading zeros, "12500", and the exponent of the last of them, 3 - 3 = 0.
    private static bool TryParseParts(string text, out string digits, out long exponent)
    {
        digits = string.Empty;
        exponent = 0;
        var position = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0;
        var builder = new StringBuilder(text.Length);
        var sawDigit = false;
        var fraction = 0;
        var sawPoint = false;
        for (; position < text.Length; position++)
        {
            var c = text[position];
            if (char.IsAsciiDigit(c))
            {
                sawDigit = true;
                if (builder.Length > 0 || c != '0')
                {
                    builder.Append(c);
                }

                fraction += sawPoint ? 1 : 0;
            }
            else if (c == '.' && !sawPoint)
            {
                sawPoint = true;
            }
            else
            {
                break;
            }
        }

        if (!sawDigit)
        {
            return false;
        }

        long written = 0;
        if (position < text.Length)
        {
            if (text[position] is not ('e' or 'E') || !TryParseExponent(text.AsSpan(position + 1), out written))
            {
                return false;
            }
        }

        digits = builder.ToString();
        exponent = written - fraction;
        return true;
    }

    // "+3", "-12", "7"; at least one digit and nothing after them. A value past the cap stays at the cap, which is out of range either way.
    private static bool TryParseExponent(ReadOnlySpan<char> text, out long exponent)
    {
        exponent = 0;
        var negative = text.Length > 0 && text[0] == '-';
        var start = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0;
        if (start == text.Length)
        {
            return false;
        }

        foreach (var c in text[start..])
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            exponent = Math.Min(ExponentCap, (exponent * 10) + (c - '0'));
        }

        exponent = negative ? -exponent : exponent;
        return true;
    }
}
