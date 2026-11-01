namespace DynaSharp.Serialization;

/// <summary>
/// Reads the elements of a list into a builder, shared by the list converters. The caller checks the type of the list, creates the builder
/// and disposes it, so the rented array comes back whether an element fails or not.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TConverter">The converter of the elements.</typeparam>
internal static class ElementReader<T, TConverter>
    where TConverter : IDynamoDbConverter<T>
{
    internal static void ReadElements(ref DynamoDbReader reader, ref ArrayBuilder<T> elements)
    {
        while (reader.TryReadStartAttribute(out var elementType))
        {
            elements.Add(TConverter.Read(ref reader, elementType));
            reader.ReadEndAttribute();
        }
    }
}
