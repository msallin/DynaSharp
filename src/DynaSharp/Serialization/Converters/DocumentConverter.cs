using DynaSharp.Mapping;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts a mapped document or entity to and from a map value (<c>M</c>), which is how a type nests in another: an address in a customer, or the
/// lines of an order through <see cref="ListConverter{T, TConverter}"/>. The attributes are the ones <typeparamref name="T"/> writes as an item of its own.
/// </summary>
/// <typeparam name="T">The document.</typeparam>
/// <remarks>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
public readonly struct DocumentConverter<T> : IDynamoDbConverter<T>
    where T : IDynamoDbDocument<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        writer.WriteStartMap();
        T.WriteAttributes(writer, value);
        writer.WriteEndMap();
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Map"/>.</exception>
    /// <exception cref="DynamoDbMappingException">The map cannot be read as <typeparamref name="T"/>.</exception>
    public static T Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Map);
        return T.ReadAttributes(ref reader);
    }
}
