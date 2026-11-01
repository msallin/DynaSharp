using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity written by hand that names a version attribute and forgets to write it.</summary>
internal sealed record HandVersioned(string Id) : IDynamoDbEntity<HandVersioned>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("HAND", "pk", null, [], versionAttributeName: "revision");

    static DynamoDbEntityInfo IDynamoDbEntity<HandVersioned>.Info => EntityInfo;

    static void IDynamoDbEntity<HandVersioned>.WriteKey(DynamoDbWriter writer, HandVersioned entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static void IDynamoDbDocument<HandVersioned>.WriteAttributes(DynamoDbWriter writer, HandVersioned entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static HandVersioned IDynamoDbDocument<HandVersioned>.ReadAttributes(ref DynamoDbReader reader) => new(string.Empty);
}
