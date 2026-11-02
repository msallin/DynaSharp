using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace DynaSharp.Keys;

/// <summary>
/// Reads a key that <see cref="KeyWriter"/> wrote, strictly: a read succeeds exactly when writing the value it returns gives back
/// the same bytes. So widths are exact, Guids are lowercase, a number has no padding or sign it would not be written with, a
/// timestamp is a real date in exactly its format, and no segment contains a delimiter. Text that is not the output of
/// the writer for some value is never accepted.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Try</c> method that returns <see langword="false"/> leaves the reader where it was. Generated parsers chain the calls
/// with <c>&amp;&amp;</c> and finish with <see cref="IsAtEnd"/>.
/// </para>
/// <para>
/// A variable-width segment ends at the first byte of <c>delimiters</c> or at the end of the text. The segment before a
/// literal therefore stops at the literal, and a delimiter inside a segment makes the next literal read fail. Pass the same
/// delimiters as to <see cref="KeyWriter"/>, so an empty span for format <c>raw</c>, whose segment is the rest of the text.
/// </para>
/// <para>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct KeyReader
{
    private ReadOnlySpan<byte> _remaining;

    /// <summary>Initializes a new instance of the <see cref="KeyReader"/> struct.</summary>
    /// <param name="utf8Text">The key as UTF-8, for example an attribute value.</param>
    public KeyReader(ReadOnlySpan<byte> utf8Text)
    {
        _remaining = utf8Text;
    }

    /// <summary>Gets a value indicating whether all of the text has been read.</summary>
    public readonly bool IsAtEnd => _remaining.IsEmpty;

    /// <summary>Reads a literal of the template.</summary>
    /// <param name="utf8Literal">The literal.</param>
    /// <returns><see langword="true"/> if the text continues with the literal.</returns>
    public bool TryReadLiteral(scoped ReadOnlySpan<byte> utf8Literal)
    {
        if (!_remaining.StartsWith(utf8Literal))
        {
            return false;
        }

        _remaining = _remaining[utf8Literal.Length..];
        return true;
    }

    /// <summary>Reads a string segment: not empty, well-formed UTF-8, no control character, no delimiter, no U+10FFFF.</summary>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The string.</param>
    /// <returns><see langword="true"/> if there is a valid segment.</returns>
    public bool TryReadString(scoped ReadOnlySpan<byte> delimiters, [NotNullWhen(true)] out string? value)
    {
        if (!TryReadUtf8Value(delimiters, out var utf8))
        {
            value = null;
            return false;
        }

        value = Encoding.UTF8.GetString(utf8);
        return true;
    }

    /// <summary>Reads a string segment without decoding it, for example to compare it with enum member names.</summary>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The bytes, a slice of the text.</param>
    /// <returns><see langword="true"/> if there is a valid segment.</returns>
    public bool TryReadUtf8Value(scoped ReadOnlySpan<byte> delimiters, out ReadOnlySpan<byte> value)
    {
        var segment = Segment(delimiters);
        if (KeyText.Inspect(segment, delimiters) != SegmentProblem.None)
        {
            value = default;
            return false;
        }

        value = segment;
        _remaining = _remaining[segment.Length..];
        return true;
    }

    /// <summary>Reads a <see cref="Guid"/>: 36 lowercase hexadecimal digits with hyphens.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the next 36 bytes are the lowercase <c>D</c> form of a Guid.</returns>
    public bool TryReadGuid(out Guid value)
    {
        value = default;
        if (_remaining.Length < KeyText.GuidText)
        {
            return false;
        }

        var text = _remaining[..KeyText.GuidText];
        for (var i = 0; i < text.Length; i++)
        {
            if ((i is 8 or 13 or 18 or 23) ? text[i] != (byte)'-' : !IsLowerHexDigit(text[i]))
            {
                return false;
            }
        }

        // The pattern is the D format, so this cannot fail.
        value = Guid.Parse(text, CultureInfo.InvariantCulture);
        _remaining = _remaining[KeyText.GuidText..];
        return true;
    }

    /// <summary>Reads an integer in plain digits: no plus sign, no leading zeros, no negative zero.</summary>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if there is a segment in the form that <see cref="KeyWriter.WriteInteger(long, ReadOnlySpan{byte}, string)"/> writes.</returns>
    public bool TryReadInt64(scoped ReadOnlySpan<byte> delimiters, out long value) => TryReadInteger(delimiters, out value);

    /// <summary>Reads a non-negative integer in plain digits: no plus sign, no leading zeros.</summary>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if there is a segment in the form that <see cref="KeyWriter.WriteInteger(ulong, ReadOnlySpan{byte}, string)"/> writes.</returns>
    public bool TryReadUInt64(scoped ReadOnlySpan<byte> delimiters, out ulong value) => TryReadInteger(delimiters, out value);

    /// <summary>Reads exactly <paramref name="width"/> ASCII digits (format <c>d&lt;w&gt;</c>).</summary>
    /// <param name="width">The number of digits.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the next <paramref name="width"/> bytes are digits and the number fits.</returns>
    public bool TryReadDigits(int width, out ulong value)
    {
        value = 0;
        if (width < 1 || _remaining.Length < width)
        {
            return false;
        }

        ulong result = 0;
        foreach (var digit in _remaining[..width])
        {
            if (!KeyText.IsDigit(digit) || result > (ulong.MaxValue - (ulong)(digit - '0')) / 10)
            {
                return false;
            }

            result = (result * 10) + (ulong)(digit - '0');
        }

        value = result;
        _remaining = _remaining[width..];
        return true;
    }

    /// <summary>Reads exactly <paramref name="width"/> ASCII digits (format <c>d&lt;w&gt;</c>) into a signed integer.</summary>
    /// <param name="width">The number of digits.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the next <paramref name="width"/> bytes are digits and the number is at most <see cref="long.MaxValue"/>.</returns>
    public bool TryReadDigits(int width, out long value)
    {
        var rest = this;
        if (!rest.TryReadDigits(width, out ulong unsigned) || unsigned > long.MaxValue)
        {
            value = 0;
            return false;
        }

        value = (long)unsigned;
        this = rest;
        return true;
    }

    /// <summary>
    /// Reads a signed fixed-point number as <see cref="KeyWriter.WriteFixedPoint"/> writes it. The all-nines <c>N</c> form, which
    /// the writer never produces, is rejected.
    /// </summary>
    /// <param name="integerDigits">The number of integer digits, at least 1.</param>
    /// <param name="fractionDigits">The number of fraction digits, at least 0, with <c>integerDigits + fractionDigits &lt;= 28</c>.</param>
    /// <param name="value">The value, with <paramref name="fractionDigits"/> decimal places.</param>
    /// <returns><see langword="true"/> if the text is exactly one value of the format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not valid.</exception>
    public bool TryReadFixedPoint(int integerDigits, int fractionDigits, out decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(integerDigits, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(fractionDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)integerDigits + fractionDigits, (long)KeyText.MaxFixedPointDigits, nameof(fractionDigits));

        value = 0m;
        var total = 1 + integerDigits + (fractionDigits == 0 ? 0 : 1 + fractionDigits);
        if (_remaining.Length < total || (_remaining[0] != (byte)'P' && _remaining[0] != (byte)'N'))
        {
            return false;
        }

        var negative = _remaining[0] == (byte)'N';
        var point = fractionDigits == 0 ? -1 : 1 + integerDigits;

        // At most 28 digits, so the mantissa fits the 96 bits of a decimal.
        UInt128 mantissa = 0;
        for (var i = 1; i < total; i++)
        {
            var current = _remaining[i];
            if (i == point ? current != (byte)'.' : !KeyText.IsDigit(current))
            {
                return false;
            }

            if (i != point)
            {
                var digit = current - '0';
                mantissa = (mantissa * 10) + (uint)(negative ? 9 - digit : digit);
            }
        }

        if (negative && mantissa == 0)
        {
            // All nines: the complement of zero, which is never written because negative zero is P.
            return false;
        }

        value = new decimal(
            (int)(uint)(mantissa & uint.MaxValue),
            (int)(uint)((mantissa >> 32) & uint.MaxValue),
            (int)(uint)((mantissa >> 64) & uint.MaxValue),
            negative,
            (byte)fractionDigits);
        _remaining = _remaining[total..];
        return true;
    }

    /// <summary>Reads a decimal in its plain form: no trailing fraction zeros, no negative zero, no plus sign, no leading zeros.</summary>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if there is a segment in the form that <see cref="KeyWriter.WriteDecimal"/> writes.</returns>
    public bool TryReadDecimal(scoped ReadOnlySpan<byte> delimiters, out decimal value)
    {
        value = 0m;
        var segment = Segment(delimiters);
        Span<byte> scratch = stackalloc byte[KeyText.MaxDecimalText];
        if (!decimal.TryParse(segment, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || !segment.SequenceEqual(KeyText.PlainDecimal(parsed, scratch)))
        {
            return false;
        }

        value = parsed;
        _remaining = _remaining[segment.Length..];
        return true;
    }

    /// <summary>Reads an instant in UTC of exactly the width of <paramref name="format"/>.</summary>
    /// <param name="format">The format.</param>
    /// <param name="value">The instant, with offset zero. Fields that the format does not have are the lowest ones.</param>
    /// <returns><see langword="true"/> if the text is a real date and time in exactly this format.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    public bool TryReadTimestamp(KeyTimeFormat format, out DateTimeOffset value)
    {
        if (!TryReadTimestamp(format, out DateTime utc))
        {
            value = default;
            return false;
        }

        value = new DateTimeOffset(utc);
        return true;
    }

    /// <summary>Reads a time of exactly the width of <paramref name="format"/>.</summary>
    /// <param name="format">The format.</param>
    /// <param name="value">The time, with <see cref="DateTimeKind.Utc"/>. Fields that the format does not have are the lowest ones.</param>
    /// <returns><see langword="true"/> if the text is a real date and time in exactly this format.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    public bool TryReadTimestamp(KeyTimeFormat format, out DateTime value)
    {
        var width = KeyText.TimestampWidth(format);
        if (_remaining.Length < width || !KeyText.TryParseTimestamp(_remaining[..width], format, out value))
        {
            value = default;
            return false;
        }

        _remaining = _remaining[width..];
        return true;
    }

    /// <summary>Reads a date written as <c>yyyy-MM-dd</c>.</summary>
    /// <param name="value">The date.</param>
    /// <returns><see langword="true"/> if the next 10 bytes are a real date in that format.</returns>
    public bool TryReadDate(out DateOnly value)
    {
        if (!TryReadTimestamp(KeyTimeFormat.Day, out DateTime utc))
        {
            value = default;
            return false;
        }

        value = DateOnly.FromDateTime(utc);
        return true;
    }

    /// <summary>
    /// Reads a custom type from its invariant UTF-8 text. The value must format back to exactly the bytes read, so a
    /// spelling the type accepts but does not write, such as <c>010.0.0.1</c> for an address, is rejected.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="delimiters">The delimiters of the template, which end the segment.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if there is a valid segment that <typeparamref name="T"/> parses and writes back unchanged.</returns>
    public bool TryReadParsable<T>(scoped ReadOnlySpan<byte> delimiters, [MaybeNullWhen(false)] out T value)
        where T : IUtf8SpanParsable<T>, IUtf8SpanFormattable
    {
        value = default;
        var segment = Segment(delimiters);
        if (KeyText.Inspect(segment, delimiters) != SegmentProblem.None
            || !T.TryParse(segment, CultureInfo.InvariantCulture, out var parsed)
            || !FormatsAs(parsed, segment))
        {
            return false;
        }

        value = parsed;
        _remaining = _remaining[segment.Length..];
        return true;
    }

    private static bool IsLowerHexDigit(byte value) => value is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f');

    // The text that writing the value would give is exactly as long as the segment, so a destination of that length is enough.
    private static bool FormatsAs<T>(T value, ReadOnlySpan<byte> segment)
        where T : IUtf8SpanFormattable
    {
        const int StackLimit = 256;
        Span<byte> stack = stackalloc byte[StackLimit];
        var destination = (segment.Length <= StackLimit ? stack : new byte[segment.Length])[..segment.Length];
        return value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture)
            && destination[..written].SequenceEqual(segment);
    }

    private bool TryReadInteger<T>(scoped ReadOnlySpan<byte> delimiters, out T value)
        where T : IBinaryInteger<T>
    {
        value = T.Zero;
        var segment = Segment(delimiters);
        Span<byte> scratch = stackalloc byte[KeyText.MaxIntegerText];

        // The parser is lenient (plus sign, leading zeros, negative zero); formatting back and comparing makes it exact.
        if (!T.TryParse(segment, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var invariant = CultureInfo.InvariantCulture;
        ReadOnlySpan<char> format = default;
        _ = parsed.TryFormat(scratch, out var length, format, invariant);
        if (!scratch[..length].SequenceEqual(segment))
        {
            return false;
        }

        value = parsed;
        _remaining = _remaining[segment.Length..];
        return true;
    }

    // Everything up to the first delimiter, or to the end.
    private readonly ReadOnlySpan<byte> Segment(scoped ReadOnlySpan<byte> delimiters)
    {
        var end = _remaining.IndexOfAny(delimiters);
        return end < 0 ? _remaining : _remaining[..end];
    }
}
