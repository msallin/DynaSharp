using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts anything but a string set as missing, on a set that <c>[OmitEmpty]</c> leaves out when it is empty and that <c>Add</c> takes.</summary>
internal readonly struct TornSetConverter : IDynamoDbConverter<HashSet<string>>, IAddableConverter
{
    public static void Write(DynamoDbWriter writer, HashSet<string> value) => HashSetConverter<string, StringSetElement>.Write(writer, value);

    public static HashSet<string> Read(ref DynamoDbReader reader, AttributeType type) => HashSetConverter<string, StringSetElement>.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type != AttributeType.StringSet;
}
