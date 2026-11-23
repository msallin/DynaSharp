namespace DynaSharp.Serialization;

/// <summary>The start of a set and the reading of its elements, shared by the set converters.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements.</typeparam>
internal static class SetElements<T, TElement>
    where TElement : IDynamoDbSetElementConverter<T>
{
    internal static void WriteStart(DynamoDbWriter writer)
    {
        switch (ScalarType())
        {
            case AttributeType.String:
                writer.WriteStartStringSet();
                break;
            case AttributeType.Number:
                writer.WriteStartNumberSet();
                break;
            default:
                writer.WriteStartBinarySet();
                break;
        }
    }

    /// <summary>Gets the type of one element standing alone: a string for a string set, a number for a number set, a binary value for a binary set.</summary>
    /// <returns>The scalar type.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TElement"/> returns no set type.</exception>
    internal static AttributeType ScalarType()
    {
        var setType = TElement.SetType;
        return setType switch
        {
            AttributeType.StringSet => AttributeType.String,
            AttributeType.NumberSet => AttributeType.Number,
            AttributeType.BinarySet => AttributeType.Binary,

            // An implementation of the public interface can return anything; failing here beats writing a set or a value of the wrong kind.
            _ => throw new InvalidOperationException($"{setType} is not a set type: a set element converter must return StringSet, NumberSet or BinarySet."),
        };
    }

    // The caller checks the type of the set, creates the builder and disposes it.
    internal static void ReadElements(ref DynamoDbReader reader, ref ArrayBuilder<T> elements)
    {
        while (reader.TryReadSetElement())
        {
            elements.Add(TElement.ReadElement(ref reader));
        }
    }

    internal static void ExpectSet(AttributeType type)
    {
        var setType = TElement.SetType;
        ConverterChecks.Expect(type, setType);
    }
}
