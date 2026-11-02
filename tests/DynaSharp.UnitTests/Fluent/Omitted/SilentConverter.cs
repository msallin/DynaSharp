using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Has nothing to say about what is omitted, so that a property with it has to stay as it is.</summary>
internal readonly struct SilentConverter : IDynamoDbConverter<int>
{
    public static void Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

    public static int Read(ref DynamoDbReader reader, AttributeType type) => NumberConverter<int>.Read(ref reader, type);
}
