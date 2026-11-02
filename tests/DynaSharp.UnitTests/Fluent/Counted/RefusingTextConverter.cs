using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="RefusingConverter"/> for a reference type, which has a wrapper of null of its own.</summary>
internal readonly struct RefusingTextConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => StringConverter.Write(writer, value);

    public static string Read(ref DynamoDbReader reader, AttributeType type) => StringConverter.Read(ref reader, type);

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => throw new FormatException("Asked.");
}
