using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts a name the enum does not have as missing, on a property. An element has its position in the list, or its place in the set, so nothing asks there.</summary>
internal readonly struct TideConverter : IDynamoDbConverter<Tide>, IDynamoDbSetElementConverter<Tide>
{
    public static AttributeType SetType => AttributeType.StringSet;

    public static void Write(DynamoDbWriter writer, Tide value) => writer.WriteString(value.ToString());

    public static Tide Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return ReadElement(ref reader);
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => !Enum.TryParse<Tide>(reader.GetString(), out _);

    public static void WriteElement(DynamoDbWriter writer, Tide value) => writer.WriteString(value.ToString());

    public static Tide ReadElement(ref DynamoDbReader reader) => Enum.TryParse<Tide>(reader.GetString(), out var value) ? value : throw new FormatException("Not a tide.");
}
