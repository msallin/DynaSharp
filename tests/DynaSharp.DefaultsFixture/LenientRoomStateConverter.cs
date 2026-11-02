using DynaSharp.Serialization;

namespace DynaSharp.DefaultsFixture;

/// <summary>A default of the assembly that counts a name the enum does not have as missing.</summary>
public readonly record struct LenientRoomStateConverter : IDynamoDbConverter<RoomState>
{
    public static void Write(DynamoDbWriter writer, RoomState value) => writer.WriteString(value.ToString());

    public static RoomState Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return Enum.TryParse<RoomState>(reader.GetString(), out var value) ? value : throw new FormatException("Not a state.");
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && !Enum.TryParse<RoomState>(reader.GetString(), out _);
}
