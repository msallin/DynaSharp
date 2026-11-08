using System.Globalization;
using System.Text.Unicode;

namespace DynaSharp.Keys;

/// <summary>What is wrong with the text of one key segment. Shared by the writer, which throws, and the reader, which fails.</summary>
internal enum SegmentProblem
{
    None,
    Empty,
    NotWellFormed,
    ControlCharacter,
    Delimiter,
    HighestScalar,
}

/// <summary>The rules that <see cref="KeyWriter"/> and <see cref="KeyReader"/> share, so that both sides validate alike.</summary>
internal static class KeyText
{
    // "-" and 19 digits for long.MinValue; ulong.MaxValue has 20 digits.
    internal const int MaxIntegerText = 20;

    // 29 digits, a point and a sign: the longest decimal in the default format.
    internal const int MaxDecimalText = 32;

    // A decimal of 29 digits formatted with 28 fraction digits is 58 bytes, and it is out of range of every fixed-point format.
    internal const int MaxFixedPointText = 64;

    // "yyyy-MM-ddTHH:mm:ss.fffffffZ".
    internal const int MaxTimestampText = 28;

    internal const int MaxFixedPointDigits = 28;

    internal const int GuidText = 36;

    private static readonly string[] ProblemReasons =
    [
        string.Empty,
        "The value is empty.",
        "The value is not well-formed text.",
        "The value contains a control character (U+0000 to U+001F).",
        "The value contains a delimiter of the key template.",
        "The value contains U+10FFFF, which is reserved for the upper bound of a range.",
    ];

    private static ReadOnlySpan<byte> HighestScalar => [0xF4, 0x8F, 0xBF, 0xBF];

    // The pattern of every timestamp format is a prefix of this one, followed by "Z" from KeyTimeFormat.Second on: d is a digit,
    // anything else stands for itself.
    private static ReadOnlySpan<byte> TimestampPattern => "dddd-dd-ddTdd:dd:dd.ddddddd"u8;

    // The same, with the lowest value of every field: a shorter text is laid over it so that all formats parse alike.
    private static ReadOnlySpan<byte> TimestampDefaults => "0001-01-01T00:00:00.0000000"u8;

    // The length of the pattern before the "Z", indexed by KeyTimeFormat.
    private static ReadOnlySpan<byte> TimestampLengths => [4, 7, 10, 13, 16, 19, 23, 26, 27];

    /// <summary>Finds what keeps <paramref name="text"/> from being a segment of a key.</summary>
    /// <param name="text">The UTF-8 text of one placeholder.</param>
    /// <param name="delimiters">The delimiters of the template; ASCII, so a byte search is exact.</param>
    /// <returns>The first problem, or <see cref="SegmentProblem.None"/>.</returns>
    internal static SegmentProblem Inspect(ReadOnlySpan<byte> text, scoped ReadOnlySpan<byte> delimiters)
    {
        if (text.IsEmpty)
        {
            return SegmentProblem.Empty;
        }

        if (!Utf8.IsValid(text))
        {
            return SegmentProblem.NotWellFormed;
        }

        if (text.IndexOfAnyInRange((byte)0x00, (byte)0x1F) >= 0)
        {
            return SegmentProblem.ControlCharacter;
        }

        if (text.IndexOfAny(delimiters) >= 0)
        {
            return SegmentProblem.Delimiter;
        }

        // A value may not contain U+10FFFF, so that prefix + U+10FFFF is above every key under the prefix.
        return text.IndexOf(HighestScalar) >= 0 ? SegmentProblem.HighestScalar : SegmentProblem.None;
    }

    internal static KeyFormatException Fail(string placeholder, SegmentProblem problem) => new(placeholder, ProblemReasons[(int)problem]) { HoldsDelimiter = problem == SegmentProblem.Delimiter };

    internal static KeyFormatException Fail(string placeholder, string reason) => new(placeholder, reason);

    /// <summary>Gets the number of bytes a timestamp of <paramref name="format"/> takes.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The width in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined value.</exception>
    internal static int TimestampWidth(KeyTimeFormat format) => TimestampLength(format) + (EndsInZulu(format) ? 1 : 0);

    /// <summary>Formats a UTC timestamp as the first bytes of its round-trip text, plus <c>Z</c> from seconds on.</summary>
    /// <param name="utc">A time of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="format">The format.</param>
    /// <param name="destination">At least <see cref="TimestampWidth"/> bytes.</param>
    internal static void FormatTimestamp(DateTime utc, KeyTimeFormat format, Span<byte> destination)
    {
        // "O" on a UTC value is yyyy-MM-ddTHH:mm:ss.fffffffZ. Slicing it truncates, which is what makes {At:yyyy-MM} a bucket.
        Span<byte> text = stackalloc byte[MaxTimestampText];
        _ = utc.TryFormat(text, out _, "O", CultureInfo.InvariantCulture);
        var length = TimestampLength(format);
        text[..length].CopyTo(destination);
        if (EndsInZulu(format))
        {
            destination[length] = (byte)'Z';
        }
    }

    /// <summary>Parses a timestamp text of exactly <see cref="TimestampWidth"/> bytes, strictly.</summary>
    /// <param name="text">The text.</param>
    /// <param name="format">The format.</param>
    /// <param name="utc">The time with <see cref="DateTimeKind.Utc"/>; fields the format does not have are the lowest ones.</param>
    /// <returns><see langword="true"/> if the text is what <see cref="FormatTimestamp"/> writes for some time.</returns>
    internal static bool TryParseTimestamp(ReadOnlySpan<byte> text, KeyTimeFormat format, out DateTime utc)
    {
        utc = default;
        var length = TimestampLength(format);
        if (text.Length != length + (EndsInZulu(format) ? 1 : 0)
            || !MatchesPattern(text[..length], TimestampPattern)
            || (EndsInZulu(format) && text[length] != (byte)'Z'))
        {
            return false;
        }

        Span<byte> fields = stackalloc byte[TimestampDefaults.Length];
        TimestampDefaults.CopyTo(fields);
        text[..length].CopyTo(fields);

        var year = Digits(fields, 0, 4);
        var month = Digits(fields, 5, 2);
        if (year < 1 || month is < 1 or > 12)
        {
            return false;
        }

        var day = Digits(fields, 8, 2);
        var hour = Digits(fields, 11, 2);
        var minute = Digits(fields, 14, 2);
        var second = Digits(fields, 17, 2);
        if (day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).AddTicks(Digits(fields, 20, 7));
        return true;
    }

    /// <summary>Gets the shortest text of a decimal that parses back to the same number: no trailing fraction zeros, no negative zero.</summary>
    /// <param name="value">The number.</param>
    /// <param name="scratch">At least <see cref="MaxDecimalText"/> bytes, which the result may point into.</param>
    /// <returns>The text.</returns>
    internal static ReadOnlySpan<byte> PlainDecimal(decimal value, Span<byte> scratch)
    {
        // 0.0m and -0.0m equal 0m and must be one key: a decimal zero formats without a sign, and its trailing zeros go below.
        _ = value.TryFormat(scratch, out var length, default, CultureInfo.InvariantCulture);
        var text = scratch[..length];
        return text.Contains((byte)'.') ? text.TrimEnd((byte)'0').TrimEnd((byte)'.') : text;
    }

    internal static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool EndsInZulu(KeyTimeFormat format) => format >= KeyTimeFormat.Second;

    private static int TimestampLength(KeyTimeFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)format, (uint)KeyTimeFormat.Ticks);
        return TimestampLengths[(int)format];
    }

    // Pattern bytes other than 'd' must match exactly; 'd' needs a digit.
    private static bool MatchesPattern(ReadOnlySpan<byte> text, ReadOnlySpan<byte> pattern)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (pattern[i] == (byte)'d' ? !IsDigit(text[i]) : text[i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    // The text has been matched against the pattern, so these are digits.
    private static int Digits(ReadOnlySpan<byte> text, int start, int count)
    {
        var value = 0;
        foreach (var digit in text.Slice(start, count))
        {
            value = (value * 10) + (digit - '0');
        }

        return value;
    }
}
