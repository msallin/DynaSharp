using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a collection with <see cref="Mapping.OmitEmptyAttribute"/> has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and tells the commands that an empty collection
/// counts as null, on top of what <typeparamref name="TConverter"/> counts. See <see cref="OmitEmptyConverter{TConverter}"/>, which does this for a string, and <see cref="OmitEmptySetConverter{T, TElement, TConverter}"/>
/// for a converter that <c>Add</c> takes.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand.</remarks>
/// <typeparam name="T">The collection.</typeparam>
/// <typeparam name="TElement">What the collection enumerates, which for a dictionary is a key and a value.</typeparam>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OmitEmptyCollectionConverter<T, TElement, TConverter> : IDynamoDbConverter<T>
    where T : class, IEnumerable<TElement>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool TryReadMissing(out T value) => TConverter.TryReadMissing(out value);

    /// <inheritdoc/>
    // A list and a set have these two interfaces between them, with nothing in common that holds a count, and a cast of a reference allocates nothing.
    public static bool IsOmitted(T value) => value switch
    {
        ICollection<TElement> collection => collection.Count == 0,
        IReadOnlyCollection<TElement> collection => collection.Count == 0,
        _ => false,
    } || TConverter.IsOmitted(value);

    /// <inheritdoc/>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => TConverter.CountsAsMissing(in reader, type);
}
