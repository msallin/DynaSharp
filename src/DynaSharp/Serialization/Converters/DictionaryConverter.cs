using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="Dictionary{TKey, TValue}"/> with string keys to and from a map value (<c>M</c>), each value in the form of <typeparamref name="TConverter"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <typeparam name="TConverter">The converter of the values.</typeparam>
/// <remarks>
/// <para>
/// The keys are the attribute names of the map, so they are always strings, and names are compared ordinally. DynamoDB has no empty name in a map, so a write of a dictionary with an
/// empty key throws <see cref="ArgumentException"/>. DynamoDB does not keep the order of a map.
/// </para>
/// <para>
/// The dictionary that is read has exactly the capacity of its entries. A name that appears twice, which DynamoDB never sends, keeps its last value.
/// </para>
/// <para>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</para>
/// </remarks>
public readonly struct DictionaryConverter<T, TConverter> : IDynamoDbConverter<Dictionary<string, T>>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key of <paramref name="value"/> is empty, which DynamoDB rejects.</exception>
    public static void Write(DynamoDbWriter writer, Dictionary<string, T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
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
    public static Dictionary<string, T> Read(ref DynamoDbReader reader, AttributeType type) =>
        Read(ref reader, type, ArrayPool<string>.Shared, ArrayPool<T>.Shared);

    // The pools are parameters so counting pools can prove every rented array comes back.
    internal static Dictionary<string, T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<string> namePool, ArrayPool<T> valuePool)
    {
        ConverterChecks.Expect(type, AttributeType.Map);
        var names = new ArrayBuilder<string>(namePool);
        var values = new ArrayBuilder<T>(valuePool);
        try
        {
            MapEntries<T, TConverter>.Read(ref reader, ref names, ref values);
            return MapEntries<T, TConverter>.ToDictionary(names.WrittenSpan, values.WrittenSpan);
        }
        finally
        {
            names.Dispose();
            values.Dispose();
        }
    }
}
