using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="IList{T}"/> to and from a list value (<c>L</c>), each element in the form of <typeparamref name="TConverter"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TConverter">The converter of the elements.</typeparam>
/// <remarks>
/// See <see cref="ArrayConverter{T, TConverter}"/> for what a list may hold. Writing an array or a <see cref="List{T}"/> enumerates it directly
/// and any other list by index, so no enumerator is boxed. A list that is read is a <see cref="List{T}"/> with exactly the capacity of its
/// elements, not an array: an array is an <see cref="IList{T}"/> that throws on <see cref="ICollection{T}.Add"/>.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct MutableListConverter<T, TConverter> : IDynamoDbConverter<IList<T>>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void Write(DynamoDbWriter writer, IList<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is T[] array)
        {
            ElementWriter<T, TConverter>.WriteArray(writer, array);
        }
        else if (value is List<T> list)
        {
            ElementWriter<T, TConverter>.WriteList(writer, list);
        }
        else
        {
            ElementWriter<T, TConverter>.WriteMutableList(writer, value);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.List"/>, or an element fails to read.</exception>
    public static IList<T> Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static IList<T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool) =>
        ListConverter<T, TConverter>.Read(ref reader, type, pool);
}
