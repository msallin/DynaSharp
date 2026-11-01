namespace DynaSharp.Serialization;

/// <summary>Reads the entries of a map into builders, shared by the dictionary converters. The caller checks the type, creates the builders and disposes them.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <typeparam name="TConverter">The converter of the values.</typeparam>
internal static class MapEntries<T, TConverter>
    where TConverter : IDynamoDbConverter<T>
{
    internal static void Read(ref DynamoDbReader reader, ref ArrayBuilder<string> names, ref ArrayBuilder<T> values)
    {
        while (reader.TryReadAttributeName())
        {
            names.Add(reader.GetAttributeName());
            var type = reader.ReadStartAttribute();
            values.Add(TConverter.Read(ref reader, type));
            reader.ReadEndAttribute();
        }
    }

    // Last wins for a name that appears twice, as in the generated reads; DynamoDB never sends one.
    internal static Dictionary<string, T> ToDictionary(ReadOnlySpan<string> names, ReadOnlySpan<T> values)
    {
        var dictionary = new Dictionary<string, T>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            dictionary[names[i]] = values[i];
        }

        return dictionary;
    }
}
