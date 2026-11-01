using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>Converts <see cref="HashSet{T}"/> to and from a string set, number set or binary set, as <typeparamref name="TElement"/> says.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements, which also decides the kind of set.</typeparam>
/// <remarks>
/// <para>
/// DynamoDB cannot store an empty set and rejects one with a validation error, so a set attribute that may be empty must be left out of the item.
/// The mapping code for entities does that, and reads a missing set attribute as an empty set. This converter writes whatever it is given,
/// for example an empty set that is an element of a list, and a set read from an empty <c>SS</c> is empty as well.
/// </para>
/// <para>
/// The set that is read has exactly the capacity of its elements and uses the default equality comparer of <typeparamref name="T"/>. DynamoDB does
/// not keep the order of a set.
/// </para>
/// <para>Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</para>
/// </remarks>
public readonly struct HashSetConverter<T, TElement> : IDynamoDbConverter<HashSet<T>>, IAddableConverter
    where TElement : IDynamoDbSetElementConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> does not return a set type from <see cref="IDynamoDbSetElementConverter{T}.SetType"/>.</exception>
    public static void Write(DynamoDbWriter writer, HashSet<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetElements<T, TElement>.WriteStart(writer);
        foreach (var element in value)
        {
            TElement.WriteElement(writer, element);
        }

        writer.WriteEndSet();
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not the kind of set <typeparamref name="TElement"/> reads, or an element fails to read.</exception>
    public static HashSet<T> Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static HashSet<T> Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool)
    {
        SetElements<T, TElement>.ExpectSet(type);
        var elements = new ArrayBuilder<T>(pool);
        try
        {
            SetElements<T, TElement>.ReadElements(ref reader, ref elements);
            var set = new HashSet<T>(elements.Count);
            foreach (var element in elements.WrittenSpan)
            {
                set.Add(element);
            }

            return set;
        }
        finally
        {
            elements.Dispose();
        }
    }
}
