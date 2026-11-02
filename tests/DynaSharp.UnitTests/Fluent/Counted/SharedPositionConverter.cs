using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A class, which shares its compiled code with other reference types, so that the question goes through the type parameter at run time.</summary>
internal sealed class SharedPositionConverter : IDynamoDbConverter<Position>
{
    public static void Write(DynamoDbWriter writer, Position value) => LenientPositionConverter.Write(writer, value);

    public static Position Read(ref DynamoDbReader reader, AttributeType type) => LenientPositionConverter.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => LenientPositionConverter.CountsAsMissing(in reader, type);
}
