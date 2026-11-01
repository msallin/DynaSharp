using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// What the handle of a nullable property of a value type has for a converter: it reads and writes what <typeparamref name="TConverter"/> does, and says in the type that the attribute may be missing, which
/// is how <c>Select</c> gives an <c>int?</c> for such a handle and an <c>int</c> for any other. A handle holds its value type without the <c>?</c>, because <c>Set</c> and the conditions take one.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand. See <see cref="OptionalReferenceConverter{T, TConverter}"/> for reference types.</remarks>
/// <typeparam name="T">The value type of the property, without the <c>?</c>.</typeparam>
/// <typeparam name="TConverter">The converter of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct OptionalConverter<T, TConverter> : IDynamoDbConverter<T>
    where T : struct
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool IsOmitted(T value) => TConverter.IsOmitted(value);
}
