using System.ComponentModel;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts one element of a set as a value that stands alone: an element of a string set as a string (<c>S</c>), of a number set as a number (<c>N</c>) and of a binary set as a binary value
/// (<c>B</c>), in the form <typeparamref name="TElement"/> gives it inside the set. That is the operand DynamoDB wants for <c>contains</c> on a set, which is how a handle of a set gets
/// one without a converter of the element type of its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>AttrExtensions.Contains</c> on a set names this type, so it is public, and it is not meant to be used by hand. An element converter that names no set type in
/// <see cref="IDynamoDbSetElementConverter{T}.SetType"/> throws <see cref="InvalidOperationException"/>, as a set converter does.
/// </para>
/// <para>Infrastructure: compatible only within a package version.</para>
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements, which also decides the type of the value.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SetElementValueConverter<T, TElement> : IDynamoDbConverter<T>
    where TElement : IDynamoDbSetElementConverter<T>
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> does not return a set type from <see cref="IDynamoDbSetElementConverter{T}.SetType"/>.</exception>
    public static void Write(DynamoDbWriter writer, T value)
    {
        // {"S":"a"}: an element is a plain JSON string, the same one inside a set and after a descriptor.
        var descriptor = ScalarType() switch
        {
            AttributeType.String => "S"u8,
            AttributeType.Number => "N"u8,
            _ => "B"u8,
        };

        writer.Json.WriteStartObject();
        writer.Json.WritePropertyName(descriptor);
        TElement.WriteElement(writer, value);
        writer.Json.WriteEndObject();
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not the scalar type of the set, or the element fails to read.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> does not return a set type from <see cref="IDynamoDbSetElementConverter{T}.SetType"/>.</exception>
    public static T Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, ScalarType());
        return TElement.ReadElement(ref reader);
    }

    private static AttributeType ScalarType()
    {
        var setType = TElement.SetType;
        return setType switch
        {
            AttributeType.StringSet => AttributeType.String,
            AttributeType.NumberSet => AttributeType.Number,
            AttributeType.BinarySet => AttributeType.Binary,

            // An implementation of the public interface can return anything; failing here beats writing a value of the wrong kind.
            _ => throw new InvalidOperationException($"{setType} is not a set type: a set element converter must return StringSet, NumberSet or BinarySet."),
        };
    }
}
