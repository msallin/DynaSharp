using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts a name the enum does not have as missing, and leaves <see cref="Read"/> as strict as it is in a list.</summary>
internal readonly struct LenientPositionConverter : IDynamoDbConverter<Position>
{
    public static void Write(DynamoDbWriter writer, Position value) => writer.WriteString(value.ToString());

    public static Position Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return Enum.TryParse<Position>(reader.GetString(), out var value) ? value : throw new FormatException("Not a position.");
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && !Enum.TryParse<Position>(reader.GetString(), out _);
}
