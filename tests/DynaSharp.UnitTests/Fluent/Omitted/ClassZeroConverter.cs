using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Writes through a class, which shares its compiled code with other reference types, so that the call goes through the type parameter at run time.</summary>
internal sealed class ClassZeroConverter : IDynamoDbConverter<int>
{
    public static void Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

    public static int Read(ref DynamoDbReader reader, AttributeType type) => NumberConverter<int>.Read(ref reader, type);

    public static bool IsOmitted(int value) => value == 0;
}
