namespace DynaSharp.Serialization;

/// <summary>The start of a set and the reading of its elements, shared by the set converters.</summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TElement">The converter of the elements.</typeparam>
internal static class SetElements<T, TElement>
    where TElement : IDynamoDbSetElementConverter<T>
{
    internal static void WriteStart(DynamoDbWriter writer)
    {
        var setType = TElement.SetType;
        switch (setType)
        {
            case AttributeType.StringSet:
                writer.WriteStartStringSet();
                break;
            case AttributeType.NumberSet:
                writer.WriteStartNumberSet();
                break;
            case AttributeType.BinarySet:
                writer.WriteStartBinarySet();
                break;
            default:
                // An implementation of the public interface can return anything; failing here beats writing a set of the wrong kind.
                throw new InvalidOperationException($"{setType} is not a set type: a set element converter must return StringSet, NumberSet or BinarySet.");
        }
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
