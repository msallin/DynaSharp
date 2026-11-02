using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts a stored <c>"-"</c> as missing, on a string that <c>[OmitEmpty]</c> leaves out when it is empty, which has no say on a read.</summary>
internal readonly struct DashTextConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => StringConverter.Write(writer, value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => StringConverter.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => reader.GetStringUtf8().SequenceEqual("-"u8);
}
