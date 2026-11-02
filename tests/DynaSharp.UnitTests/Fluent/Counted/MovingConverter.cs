using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Tries to move the reader it is asked with. Both calls compile, each on a copy that the compiler makes for it, so <see cref="Read"/> still stands on the value.</summary>
internal readonly struct MovingConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString();

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type)
    {
        reader.ReadEndAttribute();
        _ = reader.TryReadAttributeName();
        return false;
    }
}
