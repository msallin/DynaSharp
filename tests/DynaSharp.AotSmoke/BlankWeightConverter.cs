using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>Counts a blank string as missing, in an explicit member, which only a type parameter reaches.</summary>
internal readonly struct BlankWeightConverter : IDynamoDbConverter<Weight>
{
    static void IDynamoDbConverter<Weight>.Write(DynamoDbWriter writer, Weight value) => WeightConverter.Write(writer, value);

    static Weight IDynamoDbConverter<Weight>.Read(ref DynamoDbReader reader, AttributeType type) => WeightConverter.Read(ref reader, type);

    static bool IDynamoDbConverter<Weight>.CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && reader.GetStringUtf8().IsEmpty;
}
