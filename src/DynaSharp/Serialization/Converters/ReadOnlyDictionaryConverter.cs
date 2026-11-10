using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="IReadOnlyDictionary{TKey, TValue}"/> with string keys to and from a map value (<c>M</c>), each value in the form of <typeparamref name="TConverter"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <typeparam name="TConverter">The converter of the values.</typeparam>
/// <remarks>
/// See <see cref="DictionaryConverter{T, TConverter}"/> for the rules of maps. A dictionary that is read is a <see cref="Dictionary{TKey, TValue}"/>.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct ReadOnlyDictionaryConverter<T, TConverter> : IDynamoDbConverter<IReadOnlyDictionary<string, T>>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key of <paramref name="value"/> is empty, which DynamoDB rejects.</exception>
    public static void Write(DynamoDbWriter writer, IReadOnlyDictionary<string, T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is Dictionary<string, T> dictionary)
        {
            DictionaryConverter<T, TConverter>.Write(writer, dictionary);
            return;
        }

        writer.WriteStartMap();
        foreach (var (name, element) in value)
        {
            if (name.Length == 0)
            {
                throw new EmptyMapNameException(nameof(value));
            }

            writer.WriteAttributeName(name);
            TConverter.Write(writer, element);
        }

        writer.WriteEndMap();
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Map"/>, or a value fails to read.</exception>
    public static IReadOnlyDictionary<string, T> Read(ref DynamoDbReader reader, AttributeType type) =>
        Read(ref reader, type, ArrayPool<string>.Shared, ArrayPool<T>.Shared);

    // The pools are parameters so counting pools can prove every rented array comes back.
    internal static IReadOnlyDictionary<string, T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<string> namePool, ArrayPool<T> valuePool) =>
        DictionaryConverter<T, TConverter>.Read(ref reader, type, namePool, valuePool);
}
