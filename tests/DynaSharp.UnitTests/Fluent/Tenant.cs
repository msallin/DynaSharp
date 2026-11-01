using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity whose discriminator lives in an attribute of another name.</summary>
internal sealed record Tenant(string Id) : IDynamoDbEntity<Tenant>
{
    static DynamoDbEntityInfo IDynamoDbEntity<Tenant>.Info { get; } = new("TENANT", "pk", null, [], discriminatorAttributeName: "kind");

    static void IDynamoDbEntity<Tenant>.WriteKey(DynamoDbWriter writer, Tenant entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static void IDynamoDbDocument<Tenant>.WriteAttributes(DynamoDbWriter writer, Tenant entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Id);
    }

    static Tenant IDynamoDbDocument<Tenant>.ReadAttributes(ref DynamoDbReader reader)
    {
        var id = string.Empty;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("pk"u8))
            {
                id = reader.ReadAttribute<string, StringConverter>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Tenant(id);
    }
}
