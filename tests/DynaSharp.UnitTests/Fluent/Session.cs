using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity as a struct, of a table without a sort key, without a discriminator: what a table written by other tools looks like.</summary>
internal readonly record struct Session(string Id) : IDynamoDbEntity<Session>
{
    static DynamoDbEntityInfo IDynamoDbEntity<Session>.Info { get; } = new(null, "id", null, []);

    static void IDynamoDbEntity<Session>.WriteKey(DynamoDbWriter writer, Session entity)
    {
        writer.WriteAttributeName("id"u8);
        writer.WriteString(entity.Id);
    }

    static void IDynamoDbDocument<Session>.WriteAttributes(DynamoDbWriter writer, Session entity)
    {
        writer.WriteAttributeName("id"u8);
        writer.WriteString(entity.Id);
    }

    static Session IDynamoDbDocument<Session>.ReadAttributes(ref DynamoDbReader reader)
    {
        var id = string.Empty;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("id"u8))
            {
                id = reader.ReadAttribute<string, StringConverter>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Session(id);
    }
}
