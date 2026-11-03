using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// The converter of a set that is an element of a list or a map, which refuses an empty set before <typeparamref name="TConverter"/> writes it. A set property that is empty is left out of the item, which an
/// element cannot be, and DynamoDB rejects an empty set with a validation error that names no attribute. The generated write catches the refusal and names the property. Reads go to <typeparamref name="TConverter"/>.
/// </summary>
/// <remarks>Generated code names this type, so it is public, and it is not meant to be used by hand. A set converter itself writes an empty set as it is given.</remarks>
/// <typeparam name="T">The set.</typeparam>
/// <typeparam name="TElement">What the set holds.</typeparam>
/// <typeparam name="TConverter">The converter of the set.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct NonEmptySetConverter<T, TElement, TConverter> : IDynamoDbConverter<T>, IAddableConverter
    where T : class, IReadOnlyCollection<TElement>
    where TConverter : IDynamoDbConverter<T>, IAddableConverter
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public static void Write(DynamoDbWriter writer, T value)
    {
        if (value is { Count: 0 })
        {
            throw new EmptySetException(nameof(value));
        }

        TConverter.Write(writer, value);
    }

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool TryReadMissing(out T value) => TConverter.TryReadMissing(out value);
}
