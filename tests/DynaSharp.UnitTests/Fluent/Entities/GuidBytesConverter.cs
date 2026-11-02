using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Stores a Guid as its bytes, with <c>Write</c> behind the interface, where only a type parameter reaches it.</summary>
internal readonly struct GuidBytesConverter : IDynamoDbConverter<Guid>
{
    static void IDynamoDbConverter<Guid>.Write(DynamoDbWriter writer, Guid value) => writer.WriteBinary(value.ToByteArray());

    static Guid IDynamoDbConverter<Guid>.Read(ref DynamoDbReader reader, AttributeType type) => new(reader.GetBinary());
}
