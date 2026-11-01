namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="Guid"/> to and from a string value (<c>S</c>) in the lowercase 36-character form, <c>0f8fad5b-d9cb-469f-a165-70867728950e</c>.</summary>
/// <remarks>
/// Reading accepts that form in either case, and nothing else: the forms without hyphens or with braces are rejected, so one
/// text means one value. The text is formatted and parsed as UTF-8 on the stack, without creating a string.
/// </remarks>
public readonly struct GuidConverter : IDynamoDbConverter<Guid>
{
    /// <summary>The length of the text of a <see cref="Guid"/>: 32 hexadecimal digits and 4 hyphens.</summary>
    internal const int TextLength = 36;

    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, Guid value)
    {
        Span<byte> text = stackalloc byte[TextLength];
        Format(value, text);
        writer.WriteString(text);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not a <see cref="Guid"/> in the 36-character form. The message does not quote the text.</exception>
    public static Guid Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return Parse(reader.GetStringUtf8());
    }

    /// <summary>Formats a Guid as lowercase UTF-8 text.</summary>
    /// <param name="value">The Guid.</param>
    /// <param name="destination">A buffer of <see cref="TextLength"/> bytes.</param>
    internal static void Format(Guid value, Span<byte> destination) => _ = value.TryFormat(destination, out _);

    /// <summary>Parses the 36-character form.</summary>
    /// <param name="text">The UTF-8 text.</param>
    /// <returns>The Guid.</returns>
    /// <exception cref="FormatException">The text is not a Guid in the 36-character form.</exception>
    internal static Guid Parse(ReadOnlySpan<byte> text)
    {
        // Guid.TryParse also takes the forms N, B, P and X. Only D has this length.
        if (text.Length == TextLength && Guid.TryParse(text, out var guid))
        {
            return guid;
        }

        throw new FormatException("The value is not a Guid in the form 00000000-0000-0000-0000-000000000000.");
    }
}
