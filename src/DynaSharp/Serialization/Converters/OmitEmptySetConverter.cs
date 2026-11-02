using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// <see cref="OmitEmptyCollectionConverter{T, TElement, TConverter}"/> for a <typeparamref name="TConverter"/> that <c>Add</c> takes, so that <see cref="Mapping.OmitEmptyAttribute"/> keeps the <c>Add</c> of a set, and a list
/// without one stays a compile error.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand.</remarks>
/// <typeparam name="T">The collection.</typeparam>
/// <typeparam name="TElement">What the collection enumerates.</typeparam>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OmitEmptySetConverter<T, TElement, TConverter> : IDynamoDbConverter<T>, IAddableConverter
    where T : class, IEnumerable<TElement>
    where TConverter : IDynamoDbConverter<T>, IAddableConverter
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool TryReadMissing(out T value) => TConverter.TryReadMissing(out value);

    /// <inheritdoc/>
    public static bool IsOmitted(T value) => OmitEmptyCollectionConverter<T, TElement, TConverter>.IsOmitted(value);

    /// <inheritdoc/>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => TConverter.CountsAsMissing(in reader, type);
}
