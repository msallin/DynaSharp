using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a string with <see cref="Mapping.OmitEmptyAttribute"/> has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and tells the commands that an empty string
/// counts as null, on top of what <typeparamref name="TConverter"/> counts. Saying so in the type costs a handle of any other property nothing, and needs no field in the handle, which would double its size everywhere.
/// A collection has <see cref="OmitEmptyCollectionConverter{T, TElement, TConverter}"/>.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand.</remarks>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OmitEmptyConverter<TConverter> : IDynamoDbConverter<string>
    where TConverter : IDynamoDbConverter<string>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, string value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static string Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool TryReadMissing(out string value) => TConverter.TryReadMissing(out value);

    /// <inheritdoc/>
    public static bool IsOmitted(string value) => value is { Length: 0 } || TConverter.IsOmitted(value);

    /// <inheritdoc/>
    /// <remarks>Passes the question on and has no answer of its own: a stored <c>""</c> reads as <c>""</c> on a property with <see cref="Mapping.OmitEmptyAttribute"/>, unless its converter says otherwise.</remarks>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => TConverter.CountsAsMissing(in reader, type);
}
