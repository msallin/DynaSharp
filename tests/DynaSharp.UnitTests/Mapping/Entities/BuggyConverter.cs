using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Throws what no converter should, so a test sees it is not taken for a bad value.</summary>
internal readonly struct BuggyConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => throw new NotSupportedException("a bug");

    public static string Read(ref DynamoDbReader reader, AttributeType type) => throw new NotSupportedException("a bug");
}
