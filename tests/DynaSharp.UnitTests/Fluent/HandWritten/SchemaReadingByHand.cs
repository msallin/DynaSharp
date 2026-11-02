using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Written by hand, with the keys of <see cref="SchemaReading"/> and nothing about their types.</summary>
internal sealed record SchemaReadingByHand(string Id) : IDynamoDbEntity<SchemaReadingByHand>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "Sensor", "Stamp", []);

    static DynamoDbEntityInfo IDynamoDbEntity<SchemaReadingByHand>.Info => EntityInfo;

    static void IDynamoDbEntity<SchemaReadingByHand>.WriteKey(DynamoDbWriter writer, SchemaReadingByHand entity) => writer.WriteAttributeName("Sensor"u8);

    static void IDynamoDbDocument<SchemaReadingByHand>.WriteAttributes(DynamoDbWriter writer, SchemaReadingByHand entity) => writer.WriteAttributeName("Sensor"u8);

    static SchemaReadingByHand IDynamoDbDocument<SchemaReadingByHand>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
