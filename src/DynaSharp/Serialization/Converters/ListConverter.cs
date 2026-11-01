using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="List{T}"/> to and from a list value (<c>L</c>), each element in the form of <typeparamref name="TConverter"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TConverter">The converter of the elements.</typeparam>
/// <remarks>
/// See <see cref="ArrayConverter{T, TConverter}"/> for what a list may hold. The list that is read has exactly the capacity of its elements,
/// so it does not hold unused space until the first <see cref="List{T}.Add"/>.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct ListConverter<T, TConverter> : IDynamoDbConverter<List<T>>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, List<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ElementWriter<T, TConverter>.WriteList(writer, value);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.List"/>, or an element fails to read.</exception>
    public static List<T> Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static List<T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool)
    {
        ConverterChecks.Expect(type, AttributeType.List);
        var elements = new ArrayBuilder<T>(pool);
        try
        {
            ElementReader<T, TConverter>.ReadElements(ref reader, ref elements);
            var list = new List<T>(elements.Count);
            CollectionExtensions.AddRange(list, elements.WrittenSpan);
            return list;
        }
        finally
        {
            elements.Dispose();
        }
    }
}
