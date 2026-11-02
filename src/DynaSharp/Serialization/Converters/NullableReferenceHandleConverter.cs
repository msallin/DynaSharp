using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a nullable property of a reference type has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and says in the type that the attribute may be missing.
/// A <c>string?</c> and a <c>string</c> are one type at run time, so nothing else could tell a handle of the one from a handle of the other. See <see cref="NullableHandleConverter{T, TConverter}"/>.
/// </summary>
/// <remarks>
/// <para>Generated code names this type, so it is public, and it is not meant to be used by hand.</para>
/// <para>
/// It is never a read converter of its own, because it has no answer of its own for a missing attribute: <c>Select</c> sees through it and reads with <see cref="NullableReferenceConverter{T, TConverter}"/>, which gives null,
/// and <c>Return(attr)</c>, which goes through <see cref="Read"/>, reads what <typeparamref name="TConverter"/> reads and throws on a missing attribute, as it is specified to.
/// </para>
/// </remarks>
/// <typeparam name="T">The reference type of the property.</typeparam>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct NullableReferenceHandleConverter<T, TConverter> : IDynamoDbConverter<T>
    where T : class
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool IsOmitted(T value) => TConverter.IsOmitted(value);

    /// <inheritdoc/>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => TConverter.CountsAsMissing(in reader, type);
}
