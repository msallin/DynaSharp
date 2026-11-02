namespace DynaSharp.Serialization;

/// <summary>Converts <c>T?</c> for a reference type <typeparamref name="T"/>: the value in the form of <typeparamref name="TConverter"/>, or the null value <c>{"NULL":true}</c>.</summary>
/// <typeparam name="T">The underlying reference type.</typeparam>
/// <typeparam name="TConverter">The converter of <typeparamref name="T"/>.</typeparam>
/// <remarks>See <see cref="NullableConverter{T, TConverter}"/> for value types; the behavior is the same. Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.</remarks>
public readonly struct NullableReferenceConverter<T, TConverter> : IDynamoDbConverter<T?>
    where T : class
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T? value)
    {
        if (value is null)
        {
            writer.WriteNull();
        }
        else
        {
            TConverter.Write(writer, value);
        }
    }

    /// <inheritdoc/>
    public static T? Read(ref DynamoDbReader reader, AttributeType type) =>
        type == AttributeType.Null ? null : TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool TryReadMissing(out T? value)
    {
        value = null;
        return true;
    }

    /// <inheritdoc/>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) =>
        type != AttributeType.Null && TConverter.CountsAsMissing(in reader, type);
}
