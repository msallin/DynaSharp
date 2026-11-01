using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <c>T[]</c> to and from a list value (<c>L</c>), each element in the form of <typeparamref name="TConverter"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TConverter">The converter of the elements.</typeparam>
/// <remarks>
/// The elements of a DynamoDB list may have different types; this converter reads them all with <typeparamref name="TConverter"/>, so an element
/// of another type fails the read. An empty list is valid. Reading collects the elements in a pooled array and allocates the result at its exact size.
/// To store an array as a set instead, see <see cref="SetArrayConverter{T, TElement}"/>.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct ArrayConverter<T, TConverter> : IDynamoDbConverter<T[]>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, T[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ElementWriter<T, TConverter>.WriteArray(writer, value);
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.List"/>, or an element fails to read.</exception>
    public static T[] Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static T[] Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool)
    {
        ConverterChecks.Expect(type, AttributeType.List);
        var elements = new ArrayBuilder<T>(pool);
        try
        {
            ElementReader<T, TConverter>.ReadElements(ref reader, ref elements);
            return elements.ToArray();
        }
        finally
        {
            elements.Dispose();
        }
    }
}
