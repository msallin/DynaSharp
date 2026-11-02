using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>Counts a stored dash as missing, under the wrapper that [OmitEmpty] gives a handle.</summary>
internal readonly struct DashConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => StringConverter.Write(writer, value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => StringConverter.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => reader.GetStringUtf8().SequenceEqual("-"u8);
}
