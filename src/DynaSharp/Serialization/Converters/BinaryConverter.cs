namespace DynaSharp.Serialization;

/// <summary>Converts <c>byte[]</c> to and from a binary value (<c>B</c>), which travels as base64.</summary>
/// <remarks>Empty binary values are valid, except in key attributes, which DynamoDB validates.</remarks>
public readonly struct BinaryConverter : IDynamoDbConverter<byte[]>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteBinary(value);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Binary"/>, or the text is not valid base64.</exception>
    public static byte[] Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Binary);
        return reader.GetBinary();
    }
}
