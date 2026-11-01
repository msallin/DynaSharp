namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="bool"/> to and from a boolean value (<c>BOOL</c>).</summary>
public readonly struct BoolConverter : IDynamoDbConverter<bool>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, bool value) => writer.WriteBool(value);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Boolean"/>.</exception>
    public static bool Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Boolean);
        return reader.GetBool();
    }
}
