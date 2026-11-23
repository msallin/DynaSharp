using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Written by hand, and names keys and indexes but no type and no kind, which is all the runtime can expect of such a type.</summary>
internal sealed record SchemaByHand(string Id) : IDynamoDbEntity<SchemaByHand>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "_type", "pk", ScalarAttributeType.Unknown, "sk", ScalarAttributeType.Unknown, [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null), new DynamoDbIndexInfo("ByHand", "gsi1pk", null, false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null)], null);

    static DynamoDbEntityInfo IDynamoDbEntity<SchemaByHand>.Info => EntityInfo;

    static void IDynamoDbEntity<SchemaByHand>.WriteKey(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<SchemaByHand>.WriteAttributes(DynamoDbWriter writer, SchemaByHand entity) => writer.WriteAttributeName("pk"u8);

    static SchemaByHand IDynamoDbDocument<SchemaByHand>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
