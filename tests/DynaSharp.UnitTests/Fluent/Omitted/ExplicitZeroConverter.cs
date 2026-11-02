using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Leaves 0 out, and says so in an explicit member, which no generated code can call on the type.</summary>
internal readonly struct ExplicitZeroConverter : IDynamoDbConverter<int>
{
    static void IDynamoDbConverter<int>.Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

    static int IDynamoDbConverter<int>.Read(ref DynamoDbReader reader, AttributeType type) => NumberConverter<int>.Read(ref reader, type);

    static bool IDynamoDbConverter<int>.IsOmitted(int value) => value == 0;
}
