using System.Buffers.Text;
using System.Globalization;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="TimeSpan"/> to and from a string value (<c>S</c>) in the constant format <c>[-][d.]hh:mm:ss[.fffffff]</c>, for example
/// <c>1.02:03:04.5000000</c>.
/// </summary>
/// <remarks>The text does not order like the value (a negative span, or one of 10 days against one of 2), so do not use it for sorting or range conditions.</remarks>
public readonly struct TimeSpanConverter : IDynamoDbConverter<TimeSpan>
{
    // -10675199.02:48:05.4775808 is the longest form, 26 characters.
    private const int MaxTextLength = 32;

    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, TimeSpan value)
    {
        Span<byte> text = stackalloc byte[MaxTextLength];
        var invariant = CultureInfo.InvariantCulture;
        _ = value.TryFormat(text, out var length, "c", invariant);
        writer.WriteString(text[..length]);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not a time span in the constant format. The message does not quote the text.</exception>
    public static TimeSpan Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        var text = reader.GetStringUtf8();
        if (Utf8Parser.TryParse(text, out TimeSpan value, out var consumed, 'c') && consumed == text.Length)
        {
            return value;
        }

        throw new FormatException("The value is not a time span in the form [-][d.]hh:mm:ss[.fffffff].");
    }
}
