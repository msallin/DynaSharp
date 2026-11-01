using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>A set element converter that claims a type that is not a set, as a faulty implementation of the public interface would.</summary>
internal readonly struct NotASetElement : IDynamoDbSetElementConverter<int>
{
    public static AttributeType SetType => AttributeType.String;

    public static void WriteElement(DynamoDbWriter writer, int value) => writer.WriteNumberSetElement(value);

    public static int ReadElement(ref DynamoDbReader reader) => reader.GetNumber<int>();
}
