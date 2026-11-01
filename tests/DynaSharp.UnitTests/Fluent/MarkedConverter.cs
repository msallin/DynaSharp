using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A converter of a string of its own, which shows that a handle with <see cref="DynaSharp.Mapping.OmitEmptyAttribute"/> still goes through the one its property names.</summary>
internal readonly struct MarkedConverter : IDynamoDbConverter<string>
{
    public static void Write(DynamoDbWriter writer, string value) => writer.WriteString(value + "!");

    public static string Read(ref DynamoDbReader reader, AttributeType type) => reader.GetString().TrimEnd('!');
}
