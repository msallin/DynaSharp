using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts a name the enum does not have as missing, and says what a missing attribute reads as, so that <c>Select</c> agrees with a read of the entity.</summary>
internal readonly struct AnsweringPositionConverter : IDynamoDbConverter<Position>
{
    public static void Write(DynamoDbWriter writer, Position value) => LenientPositionConverter.Write(writer, value);

    public static Position Read(ref DynamoDbReader reader, AttributeType type) => LenientPositionConverter.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => LenientPositionConverter.CountsAsMissing(in reader, type);

    public static bool TryReadMissing(out Position value)
    {
        value = Position.Unknown;
        return true;
    }
}
