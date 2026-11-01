namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/> to and from a binary value (<c>B</c>), which travels as base64.</summary>
/// <remarks>
/// A value read is backed by a new array that nothing else references, so keeping it is safe. Writing reads the memory without copying it.
/// Empty binary values are valid, except in key attributes, which DynamoDB validates.
/// </remarks>
public readonly struct ReadOnlyMemoryConverter : IDynamoDbConverter<ReadOnlyMemory<byte>>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, ReadOnlyMemory<byte> value) => writer.WriteBinary(value.Span);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Binary"/>, or the text is not valid base64.</exception>
    public static ReadOnlyMemory<byte> Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Binary);
        return reader.GetBinary();
    }
}
