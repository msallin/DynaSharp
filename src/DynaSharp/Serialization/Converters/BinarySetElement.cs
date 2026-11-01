namespace DynaSharp.Serialization;

/// <summary>The elements of a binary set (<c>BS</c>): <c>byte[]</c>, as base64.</summary>
/// <remarks>
/// DynamoDB accepts a zero-length element. Arrays compare by reference, so a <see cref="HashSet{T}"/> of <c>byte[]</c> built with
/// the default comparer holds two arrays with the same bytes as two elements, and DynamoDB rejects such a set. Leave the duplicates out before
/// writing, or use a set type with a comparer of your own.
/// </remarks>
public readonly struct BinarySetElement : IDynamoDbSetElementConverter<byte[]>
{
    /// <inheritdoc/>
    public static AttributeType SetType => AttributeType.BinarySet;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void WriteElement(DynamoDbWriter writer, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteBinarySetElement(value);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException">The text is not valid base64.</exception>
    public static byte[] ReadElement(ref DynamoDbReader reader) => reader.GetBinary();
}
