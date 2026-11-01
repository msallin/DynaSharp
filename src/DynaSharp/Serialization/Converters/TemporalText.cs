using System.Buffers;
using System.Globalization;
using System.Text;

namespace DynaSharp.Serialization;

/// <summary>
/// The text forms of dates and times that the converters share. Instants are written in UTC in the round-trip form, so stored values compare
/// chronologically as text, and are read from the ISO 8601 forms other writers produce.
/// </summary>
internal static class TemporalText
{
    /// <summary>The length of the round-trip form <c>2026-10-31T12:00:00.0000000Z</c>.</summary>
    internal const int InstantLength = 28;

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

    /// <summary>Parses an instant.</summary>
    /// <param name="utf8Text">The text.</param>
    /// <returns>The instant in UTC, as a <see cref="DateTimeOffset"/> with offset zero.</returns>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset, or outside the range of <see cref="DateTimeOffset"/>.</exception>
    internal static DateTimeOffset ParseInstant(ReadOnlySpan<byte> utf8Text)
    {
        Span<char> text = stackalloc char[MaxParsedLength];
        var invariant = CultureInfo.InvariantCulture;
        if (TryWiden(utf8Text, text, out var length)
            && DateTimeOffset.TryParseExact(text[..length], InstantFormats, invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
        {
            return instant;
        }

        throw new FormatException("The value is not an ISO 8601 date and time with a UTC offset, such as 2026-10-31T12:00:00.0000000Z.");
    }

    /// <summary>Copies ASCII text from UTF-8 to a buffer of characters.</summary>
    /// <param name="utf8Text">The text.</param>
    /// <param name="destination">The buffer.</param>
    /// <param name="length">The number of characters written.</param>
    /// <returns><see langword="false"/> if the text has other characters than ASCII or does not fit the buffer: neither can be a date or a time.</returns>
    internal static bool TryWiden(ReadOnlySpan<byte> utf8Text, Span<char> destination, out int length) =>
        Ascii.ToUtf16(utf8Text, destination, out length) == OperationStatus.Done;
}
