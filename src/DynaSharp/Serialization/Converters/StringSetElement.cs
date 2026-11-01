namespace DynaSharp.Serialization;

/// <summary>The elements of a string set (<c>SS</c>): <see cref="string"/>.</summary>
/// <remarks>DynamoDB accepts the empty string as an element; both emulators and the service do. Elements are compared ordinally.</remarks>
public readonly struct StringSetElement : IDynamoDbSetElementConverter<string>
{
    /// <inheritdoc/>
    public static AttributeType SetType => AttributeType.StringSet;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void WriteElement(DynamoDbWriter writer, string value) => writer.WriteStringSetElement(value);

    /// <inheritdoc/>
    public static string ReadElement(ref DynamoDbReader reader) => reader.GetString();
}
