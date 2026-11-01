namespace DynaSharp.Serialization;

/// <summary>The elements of a string set (<c>SS</c>) that holds <see cref="Guid"/> values, each in the form <see cref="GuidConverter"/> uses.</summary>
public readonly struct GuidSetElement : IDynamoDbSetElementConverter<Guid>
{
    /// <inheritdoc/>
    public static AttributeType SetType => AttributeType.StringSet;

    /// <inheritdoc/>
    public static void WriteElement(DynamoDbWriter writer, Guid value)
    {
        Span<byte> text = stackalloc byte[GuidConverter.TextLength];
        GuidConverter.Format(value, text);
        writer.Json.WriteStringValue(text);
    }

    /// <inheritdoc/>
    /// <exception cref="FormatException">The text is not a <see cref="Guid"/> in the 36-character form. The message does not quote the text.</exception>
    public static Guid ReadElement(ref DynamoDbReader reader) => GuidConverter.Parse(reader.GetStringUtf8());
}
