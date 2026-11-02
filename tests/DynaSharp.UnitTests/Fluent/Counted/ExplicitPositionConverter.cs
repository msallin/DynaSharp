using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Says so in an explicit member, which only a type parameter reaches.</summary>
internal readonly struct ExplicitPositionConverter : IDynamoDbConverter<Position>
{
    static void IDynamoDbConverter<Position>.Write(DynamoDbWriter writer, Position value) => LenientPositionConverter.Write(writer, value);

    static Position IDynamoDbConverter<Position>.Read(ref DynamoDbReader reader, AttributeType type) => LenientPositionConverter.Read(ref reader, type);

    static bool IDynamoDbConverter<Position>.CountsAsMissing(in DynamoDbReader reader, AttributeType type) => LenientPositionConverter.CountsAsMissing(in reader, type);
}
