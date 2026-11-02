using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Written by hand, and names keys and indexes but no type and no kind, which is all the runtime can expect of such a type.</summary>
internal sealed record SchemaByHand(string Id) : IDynamoDbEntity<SchemaByHand>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "pk", "sk", [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false), new DynamoDbIndexInfo("ByHand", "gsi1pk", null, false)]);

    static DynamoDbEntityInfo IDynamoDbEntity<SchemaByHand>.Info => EntityInfo;

    static void IDynamoDbEntity<SchemaByHand>.WriteKey(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<SchemaByHand>.WriteAttributes(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static SchemaByHand IDynamoDbDocument<SchemaByHand>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
