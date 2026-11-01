using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a property with <see cref="Mapping.OmitEmptyAttribute"/> has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and tells the commands that an empty string
/// counts as null. Saying so in the type costs a handle of any other property nothing, and needs no field in the handle, which would double its size everywhere.
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
    public static bool IsOmitted(string value) => value is { Length: 0 };
}
