namespace DynaSharp.Serialization;

/// <summary>
/// The loops that write the elements of a list, shared by the list converters. Each loop is typed to the collection it runs over, so
/// enumerating never boxes an enumerator.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TConverter">The converter of the elements.</typeparam>
internal static class ElementWriter<T, TConverter>
    where TConverter : IDynamoDbConverter<T>
{
    internal static void WriteArray(DynamoDbWriter writer, T[] elements)
    {
        writer.WriteStartList();
        foreach (var element in elements)
        {
            TConverter.Write(writer, element);
        }

        writer.WriteEndList();
    }

    internal static void WriteList(DynamoDbWriter writer, List<T> elements)
    {
        writer.WriteStartList();
        foreach (var element in elements)
        {
            TConverter.Write(writer, element);
        }

        writer.WriteEndList();
    }

    // The interface's enumerator is a boxed object, an index is not.
    internal static void WriteReadOnlyList(DynamoDbWriter writer, IReadOnlyList<T> elements)
    {
        writer.WriteStartList();
        for (var i = 0; i < elements.Count; i++)
        {
            TConverter.Write(writer, elements[i]);
        }

        writer.WriteEndList();
    }

    internal static void WriteMutableList(DynamoDbWriter writer, IList<T> elements)
    {
        writer.WriteStartList();
        for (var i = 0; i < elements.Count; i++)
        {
            TConverter.Write(writer, elements[i]);
        }

        writer.WriteEndList();
    }
}
