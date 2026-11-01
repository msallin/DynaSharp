namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="string"/> to and from a string value (<c>S</c>).</summary>
/// <remarks>
/// A <see langword="null"/> string cannot be written as a value: a converter of a nullable type writes <c>{"NULL":true}</c> instead, see
/// <see cref="NullableReferenceConverter{T, TConverter}"/>. The empty string is a valid value, except in key attributes, which DynamoDB validates.
/// </remarks>
public readonly struct StringConverter : IDynamoDbConverter<string>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    public static string Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return reader.GetString();
    }
}
