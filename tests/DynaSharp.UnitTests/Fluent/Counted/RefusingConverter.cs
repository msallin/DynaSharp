using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Fails whenever it is asked, so that a test can tell that nobody asked, and that a failure of the question is reported as one of the read.</summary>
internal readonly struct RefusingConverter : IDynamoDbConverter<int>
{
    public static void Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

    public static int Read(ref DynamoDbReader reader, AttributeType type) => NumberConverter<int>.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => throw new FormatException("Asked.");
}
