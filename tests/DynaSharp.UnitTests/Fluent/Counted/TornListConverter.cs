using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts anything but a list as missing, on a list that <c>[OmitEmpty]</c> leaves out when it is empty.</summary>
internal readonly struct TornListConverter : IDynamoDbConverter<List<string>>
{
    public static void Write(DynamoDbWriter writer, List<string> value) => ListConverter<string, StringConverter>.Write(writer, value);

    public static List<string> Read(ref DynamoDbReader reader, AttributeType type) => ListConverter<string, StringConverter>.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type != AttributeType.List;
}
