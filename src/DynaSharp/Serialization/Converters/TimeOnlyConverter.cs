using System.Globalization;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="TimeOnly"/> to and from a string value (<c>S</c>) in the form <c>12:00:00.0000000</c>: 24-hour clock, always seven
/// fraction digits, so the text orders like the time.
/// </summary>
/// <remarks>Reading also accepts fewer fraction digits, down to none (<c>12:00:00</c>).</remarks>
public readonly struct TimeOnlyConverter : IDynamoDbConverter<TimeOnly>
{
    private const string WriteFormat = "O";
    private const string ReadFormat = "HH:mm:ss.FFFFFFF";
    private const int TextLength = 16;

    // Two characters longer than the form, so that a longer text is seen as not being a time.
    private const int MaxParsedLength = TextLength + 2;

    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, TimeOnly value)
    {
        Span<byte> text = stackalloc byte[TextLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = value.TryFormat(text, out _, WriteFormat, invariant);
        writer.WriteString(text);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not a time in the form <c>HH:mm:ss.fffffff</c>. The message does not quote the text.</exception>
    public static TimeOnly Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        Span<char> text = stackalloc char[MaxParsedLength];
        var invariant = CultureInfo.InvariantCulture;
        if (TemporalText.TryWiden(reader.GetStringUtf8(), text, out var length)
            && TimeOnly.TryParseExact(text[..length], ReadFormat, invariant, DateTimeStyles.None, out var time))
        {
            return time;
        }

        throw new FormatException("The value is not a time in the form HH:mm:ss.fffffff.");
    }
}
