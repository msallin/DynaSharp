using System.Buffers;
using System.Globalization;
using System.Text;

namespace DynaSharp.Serialization;

/// <summary>
/// The text forms of dates and times that the converters share. Instants are written in UTC in the round-trip form, so stored values compare
/// chronologically as text, and are read from the ISO 8601 forms other writers produce. The opt-in converters write the milliseconds of the AWS SDK for .NET,
/// and a time with its offset.
/// </summary>
internal static class TemporalText
{
    /// <summary>The length of the round-trip form <c>2026-10-31T12:00:00.0000000Z</c>.</summary>
    internal const int InstantLength = 28;

    // 2026-10-31T12:00:00.123Z
    private const int MillisecondsLength = 24;

    // 2026-10-31T12:00:00.0000000+01:00: "O" of a DateTimeOffset always has seven fraction digits and an offset of hours and minutes.
    private const int WithOffsetLength = 33;

    // The longest form read is 2026-10-31T12:00:00.1234567+01:00, 33 characters.
    private const int MaxParsedLength = 40;

    // Order matters for speed only: the form the converters write comes first. 'F' allows up to seven fraction digits, none included. An instant
    // needs the Z or an offset; without one the text would name a local time, and a library cannot know whose.
    private static readonly string[] InstantFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
    ];

    /// <summary>Writes an instant as a string value in the round-trip form.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="utc">The instant, with <see cref="DateTimeKind.Utc"/>, which makes the form end in <c>Z</c>.</param>
    internal static void WriteInstant(DynamoDbWriter writer, DateTime utc)
    {
        Span<byte> text = stackalloc byte[InstantLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = utc.TryFormat(text, out _, "O", invariant);
        writer.WriteString(text);
    }

    /// <summary>Writes an instant as a string value with three fraction digits, the form of the AWS SDK for .NET: <c>2026-10-31T12:00:00.123Z</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="utc">The instant, with <see cref="DateTimeKind.Utc"/>. Ticks below the millisecond are cut off, as the SDK's <c>fff</c> does.</param>
    internal static void WriteMilliseconds(DynamoDbWriter writer, DateTime utc)
    {
        // "O" on a UTC value is 2026-10-31T12:00:00.1234567Z: the first 23 bytes end at the milliseconds, and the 24th becomes the Z.
        Span<byte> text = stackalloc byte[InstantLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = utc.TryFormat(text, out _, "O", invariant);
        text[MillisecondsLength - 1] = (byte)'Z';
        writer.WriteString(text[..MillisecondsLength]);
    }

    /// <summary>Writes a time with its offset as a string value in the round-trip form: <c>2026-10-31T12:00:00.0000000+01:00</c>, and <c>+00:00</c> for offset zero.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The time.</param>
    internal static void WriteWithOffset(DynamoDbWriter writer, DateTimeOffset value)
    {
        Span<byte> text = stackalloc byte[WithOffsetLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = value.TryFormat(text, out _, "O", invariant);
        writer.WriteString(text);
    }

    /// <summary>Parses an instant.</summary>
    /// <param name="utf8Text">The text.</param>
    /// <returns>The instant in UTC, as a <see cref="DateTimeOffset"/> with offset zero.</returns>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset, or outside the range of <see cref="DateTimeOffset"/>.</exception>
    internal static DateTimeOffset ParseInstant(ReadOnlySpan<byte> utf8Text) => Parse(utf8Text, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>Parses a time and keeps its offset.</summary>
    /// <param name="utf8Text">The text.</param>
    /// <returns>The time with the offset of the text, and offset zero for <c>Z</c>.</returns>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset, or outside the range of <see cref="DateTimeOffset"/>.</exception>
    internal static DateTimeOffset ParseWithOffset(ReadOnlySpan<byte> utf8Text) => Parse(utf8Text, DateTimeStyles.AssumeUniversal);

    /// <summary>Copies ASCII text from UTF-8 to a buffer of characters.</summary>
    /// <param name="utf8Text">The text.</param>
    /// <param name="destination">The buffer.</param>
    /// <param name="length">The number of characters written.</param>
    /// <returns><see langword="false"/> if the text has other characters than ASCII or does not fit the buffer: neither can be a date or a time.</returns>
    internal static bool TryWiden(ReadOnlySpan<byte> utf8Text, Span<char> destination, out int length) =>
        Ascii.ToUtf16(utf8Text, destination, out length) == OperationStatus.Done;

    private static DateTimeOffset Parse(ReadOnlySpan<byte> utf8Text, DateTimeStyles styles)
    {
        // AssumeUniversal gives the text that ends in a literal Z offset zero; an offset in the text wins over it.
        Span<char> text = stackalloc char[MaxParsedLength];
        var invariant = CultureInfo.InvariantCulture;
        if (TryWiden(utf8Text, text, out var length)
            && DateTimeOffset.TryParseExact(text[..length], InstantFormats, invariant, styles, out var instant))
        {
            return instant;
        }

        throw new FormatException("The value is not an ISO 8601 date and time with a UTC offset, such as 2026-10-31T12:00:00.0000000Z.");
    }
}
