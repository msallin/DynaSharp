using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts <c>T[]</c> to and from a string set, number set or binary set, as <typeparamref name="TElement"/> says. It is opt-in, with
/// <c>[DynamoDbConverter]</c> on the property, since an array is a list by default.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements, which also decides the kind of set.</typeparam>
/// <remarks>
/// <para>
/// An array is much lighter than a <see cref="HashSet{T}"/>: a <c>string[]</c> of three elements takes about 48 bytes against about 176 for a set.
/// The price is that the array does not stop duplicates: <c>["x", "x"]</c> writes <c>{"SS":["x","x"]}</c>. DynamoDB rejects a set with duplicates (for numbers, with numerically equal elements)
/// with a validation error, so the code that fills the array must keep its elements distinct. See <see cref="HashSetConverter{T, TElement}"/> for
/// the other rules of sets, such as the empty set.
/// </para>
/// <para>The array that is read has exactly the length of the set, in the order the service sent, which is not a stable order.</para>
/// <para>
/// <typeparamref name="TElement"/> is an <see cref="IDynamoDbSetElementConverter{T}"/>, which writes one element of a set, and not the converter of the type, which writes a whole attribute. The built-in ones are
/// <see cref="StringSetElement"/> for a <c>string[]</c>, <see cref="NumberSetElement{T}"/> for an array of numbers, <see cref="BinarySetElement"/> for a <c>byte[][]</c> and <see cref="GuidSetElement"/> for a <c>Guid[]</c>. A set of strings is
/// <c>[DynamoDbConverter(typeof(SetArrayConverter&lt;string, StringSetElement&gt;))] public string[] Tags { get; init; } = [];</c>. <see cref="StringConverter"/> is not an element converter, so
/// <c>SetArrayConverter&lt;string, StringConverter&gt;</c> does not compile: <c>CS0315</c> on the constraint of <typeparamref name="TElement"/>.
/// </para>
/// </remarks>
public readonly struct SetArrayConverter<T, TElement> : IDynamoDbConverter<T[]>, IAddableConverter
    where TElement : IDynamoDbSetElementConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> does not return a set type from <see cref="IDynamoDbSetElementConverter{T}.SetType"/>.</exception>
    public static void Write(DynamoDbWriter writer, T[] value)
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
    public static T[] Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, ArrayPool<T>.Shared);

    /// <inheritdoc/>
    public static bool TryReadMissing(out T[] value)
    {
        value = [];
        return true;
    }

    // The pool is a parameter so a counting pool can prove every rented array comes back.
    internal static T[] Read(ref DynamoDbReader reader, AttributeType type, ArrayPool<T> pool)
    {
        SetElements<T, TElement>.ExpectSet(type);
        var elements = new ArrayBuilder<T>(pool);
        try
        {
            SetElements<T, TElement>.ReadElements(ref reader, ref elements);
            return elements.ToArray();
        }
        finally
        {
            elements.Dispose();
        }
    }
}
