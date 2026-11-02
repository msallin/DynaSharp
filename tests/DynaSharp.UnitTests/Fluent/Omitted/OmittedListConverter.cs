using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Stores a list of strings as a set, which DynamoDB refuses to hold empty: <c>{"Tags":{"SS":[]}}</c>. So an empty list has to be left out.</summary>
internal readonly record struct OmittedListConverter : IDynamoDbConverter<List<string>>
{
    public static void Write(DynamoDbWriter writer, List<string> value)
    {
        writer.WriteStartStringSet();
        foreach (var element in value)
        {
            writer.WriteStringSetElement(element);
        }

        writer.WriteEndSet();
    }

    public static List<string> Read(ref DynamoDbReader reader, AttributeType type) => [.. HashSetConverter<string, StringSetElement>.Read(ref reader, type)];

    public static bool IsOmitted(List<string> value) => value.Count == 0;
}
