using System.Globalization;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="DateOnly"/> to and from a string value (<c>S</c>) in the form <c>2026-10-31</c>, which orders chronologically as text.</summary>
public readonly struct DateOnlyConverter : IDynamoDbConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";
    private const int TextLength = 10;

    // Two characters longer than the form, so that a longer text is seen as not being a date.
    private const int MaxParsedLength = TextLength + 2;

    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, DateOnly value)
    {
        Span<byte> text = stackalloc byte[TextLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = value.TryFormat(text, out _, Format, invariant);
        writer.WriteString(text);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not a date in the form <c>yyyy-MM-dd</c>. The message does not quote the text.</exception>
    public static DateOnly Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        Span<char> text = stackalloc char[MaxParsedLength];
        var invariant = CultureInfo.InvariantCulture;
        if (TemporalText.TryWiden(reader.GetStringUtf8(), text, out var length)
            && DateOnly.TryParseExact(text[..length], Format, invariant, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new FormatException("The value is not a date in the form yyyy-MM-dd.");
    }
}
