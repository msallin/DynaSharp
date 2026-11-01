using System.Buffers.Binary;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Writes a DynamoDB number in a form whose bytes compare the way the numbers do, so that a merge has one comparison for strings, binary values and numbers: their bytes.
/// DynamoDB numbers have 38 significant digits and exponents beyond any .NET number, so the digits are taken as digits; a <see cref="double"/> would make neighbours equal.
/// </summary>
/// <remarks>
/// <para>
/// The form is a sign, the exponent of the number as <c>0.ddd × 10^e</c>, and its digits without the zeros at either end, which makes <c>1</c>, <c>1.0</c> and <c>10E-1</c> one form.
/// A negative number has all of that inverted, so that a larger magnitude sorts first, and ends in a byte above every digit, so that <c>-0.12</c> sorts above <c>-0.123</c>, whose
/// digits it is a prefix of.
/// </para>
/// <code>
/// "0"       02 is not used: 01
/// "12.50"   02 80000002 "125"
/// "-12.50"  00 7FFFFFFD ~"125" FF
/// </code>
/// </remarks>
internal static class NumberOrder
{
    private const byte Negative = 0;
    private const byte Zero = 1;
    private const byte Positive = 2;

    // A sign and an exponent before the digits, and the end of a negative number after them.
    private const int HeaderLength = 1 + sizeof(uint);

    // An exponent of more digits does not fit an int, and DynamoDB has none above three.
    private const int MaxExponentDigits = 9;

    /// <summary>Writes the form of a number.</summary>
    /// <param name="text">The number as DynamoDB transfers it, for example <c>-1.5E-10</c>.</param>
    /// <param name="destination">The buffer to append to.</param>
    /// <returns><see langword="true"/> if the text is a number. Otherwise nothing was written.</returns>
    public static bool TryAppend(ReadOnlySpan<byte> text, PooledByteBuffer destination)
    {
        var form = destination.GetSpan(HeaderLength + text.Length + 1);
        var digits = form[HeaderLength..];
        var negative = !text.IsEmpty && text[0] == (byte)'-';
        var rest = negative || (!text.IsEmpty && text[0] == (byte)'+') ? text[1..] : text;
        if (!TryReadMantissa(ref rest, digits, out var written, out var point) || !TryReadExponent(rest, out var exponent))
        {
            return false;
        }

        while (written > 0 && digits[written - 1] == (byte)'0')
        {
            written--;
        }

        if (written == 0)
        {
            form[0] = Zero;
            destination.Advance(1);
            return true;
        }

        // The bias makes an unsigned comparison of the bytes order the exponents.
        var biased = (uint)(point + exponent) ^ 0x8000_0000;
        form[0] = negative ? Negative : Positive;
        BinaryPrimitives.WriteUInt32BigEndian(form[1..], negative ? ~biased : biased);
        var length = HeaderLength + written;
        if (negative)
        {
            foreach (ref var digit in digits[..written])
            {
                digit = (byte)~digit;
            }

            form[length++] = byte.MaxValue;
        }

        destination.Advance(length);
        return true;
    }

    // "012.50E3": digits gets "1250", point is 2 and rest "E3". "0.00123": digits gets "123", point is -2. Leading zeros are no digits, they only move the point.
    private static bool TryReadMantissa(ref ReadOnlySpan<byte> rest, Span<byte> digits, out int written, out int point)
    {
        written = 0;
        point = 0;
        var seenPoint = false;
        var seenDigit = false;
        var index = 0;
        for (; index < rest.Length; index++)
        {
            var character = rest[index];
            if (character == (byte)'.' && !seenPoint)
            {
                seenPoint = true;
            }
            else if (character is >= (byte)'0' and <= (byte)'9')
            {
                seenDigit = true;
                if (written == 0 && character == (byte)'0')
                {
                    point -= seenPoint ? 1 : 0;
                }
                else
                {
                    point += seenPoint ? 0 : 1;
                    digits[written++] = character;
                }
            }
            else
            {
                break;
            }
        }

        rest = rest[index..];
        return seenDigit;
    }

    // "E+3" gives 3 and "" gives 0. The only thing that may follow a mantissa is an exponent.
    private static bool TryReadExponent(ReadOnlySpan<byte> rest, out int exponent)
    {
        exponent = 0;
        if (rest.IsEmpty)
        {
            return true;
        }

        if (rest[0] is not ((byte)'e' or (byte)'E'))
        {
            return false;
        }

        rest = rest[1..];
        var negative = !rest.IsEmpty && rest[0] == (byte)'-';
        rest = negative || (!rest.IsEmpty && rest[0] == (byte)'+') ? rest[1..] : rest;
        if (rest.IsEmpty || rest.Length > MaxExponentDigits || rest.ContainsAnyExceptInRange((byte)'0', (byte)'9'))
        {
            return false;
        }

        foreach (var digit in rest)
        {
            exponent = (exponent * 10) + (digit - '0');
        }

        exponent = negative ? -exponent : exponent;
        return true;
    }
}
