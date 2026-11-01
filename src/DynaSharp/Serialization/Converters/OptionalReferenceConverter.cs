using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a nullable property of a reference type has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and says in the type that the attribute may be missing.
/// A <c>string?</c> and a <c>string</c> are one type at run time, so nothing else could tell a handle of the one from a handle of the other. See <see cref="OptionalConverter{T, TConverter}"/>.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand.</remarks>
/// <typeparam name="T">The reference type of the property.</typeparam>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OptionalReferenceConverter<T, TConverter> : IDynamoDbConverter<T>
    where T : class
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool IsOmitted(T value) => TConverter.IsOmitted(value);
}
