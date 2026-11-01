using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="IReadOnlySet{T}"/> to and from a string set, number set or binary set, as <typeparamref name="TElement"/> says.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements, which also decides the kind of set.</typeparam>
/// <remarks>
/// See <see cref="HashSetConverter{T, TElement}"/> for the rules of sets. A set that is read is a <see cref="HashSet{T}"/>. Writing a
/// <see cref="HashSet{T}"/> enumerates it directly; any other set goes through the interface's enumerator, which is boxed.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct ReadOnlySetConverter<T, TElement> : IDynamoDbConverter<IReadOnlySet<T>>, IAddableConverter
    where TElement : IDynamoDbSetElementConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> does not return a set type from <see cref="IDynamoDbSetElementConverter{T}.SetType"/>.</exception>
    public static void Write(DynamoDbWriter writer, IReadOnlySet<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is HashSet<T> set)
        {
            HashSetConverter<T, TElement>.Write(writer, set);
            return;
        }

        SetElements<T, TElement>.WriteStart(writer);
        foreach (var element in value)
        {
            TElement.WriteElement(writer, element);
        }

        writer.WriteEndSet();
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not the kind of set <typeparamref name="TElement"/> reads, or an element fails to read.</exception>
    public static IReadOnlySet<T> Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    /// <inheritdoc/>
    public static bool TryReadMissing(out IReadOnlySet<T> value)
    {
        value = new HashSet<T>();
        return true;
    }

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static IReadOnlySet<T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool) =>
        HashSetConverter<T, TElement>.Read(ref reader, type, pool);
}
