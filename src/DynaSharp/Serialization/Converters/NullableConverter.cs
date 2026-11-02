namespace DynaSharp.Serialization;

/// <summary>Converts <c>T?</c> for a value type <typeparamref name="T"/>: the value in the form of <typeparamref name="TConverter"/>, or the null value <c>{"NULL":true}</c>.</summary>
/// <typeparam name="T">The underlying value type.</typeparam>
/// <typeparam name="TConverter">The converter of <typeparamref name="T"/>.</typeparam>
/// <remarks>
/// Writing <see langword="null"/> produces <c>{"NULL":true}</c>, which is a value, not a missing attribute: it is what a null element of a list
/// becomes. The mapping code for entities leaves a property that is <see langword="null"/> out instead. Reading accepts the null value, and
/// everything <typeparamref name="TConverter"/> accepts.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public readonly struct NullableConverter<T, TConverter> : IDynamoDbConverter<T?>
    where T : struct
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T? value)
    {
        if (value is { } present)
        {
            TConverter.Write(writer, present);
        }
        else
        {
            writer.WriteNull();
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
