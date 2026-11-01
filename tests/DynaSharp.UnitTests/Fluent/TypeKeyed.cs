using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity written by hand whose discriminator is the sort key of the table, which the generator rejects.</summary>
internal sealed record TypeKeyed(string Id) : IDynamoDbEntity<TypeKeyed>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("NOTE", "pk", "sk", [], discriminatorAttributeName: "sk");

    static DynamoDbEntityInfo IDynamoDbEntity<TypeKeyed>.Info => EntityInfo;

    static void IDynamoDbEntity<TypeKeyed>.WriteKey(DynamoDbWriter writer, TypeKeyed entity) => writer.WriteAttributeName("pk"u8);

    static void IDynamoDbDocument<TypeKeyed>.WriteAttributes(DynamoDbWriter writer, TypeKeyed entity) => writer.WriteAttributeName("pk"u8);

    static TypeKeyed IDynamoDbDocument<TypeKeyed>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
