using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts "none" as null, on top of what <see cref="OmitEmptyConverter{TConverter}"/> counts.</summary>
internal readonly struct NoneOmittedConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

    public static bool IsOmitted(string value) => value == "none";
}
