using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>An entity written by hand the way the generator will write it, so that the typed commands run in the Native AOT binary.</summary>
internal sealed record Visitor(string Id, int Visits) : IDynamoDbDiscriminated<Visitor>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("VISITOR", "pk", null, []);

    static DynamoDbEntityInfo IDynamoDbEntity<Visitor>.Info => EntityInfo;

    static void IDynamoDbEntity<Visitor>.WriteKey(DynamoDbWriter writer, Visitor entity) => new Key(entity.Id).WriteKey(writer);

    static void IDynamoDbDocument<Visitor>.WriteAttributes(DynamoDbWriter writer, Visitor entity)
    {
        new Key(entity.Id).WriteKey(writer);
        writer.WriteAttributeName("visits"u8);
        writer.WriteNumber(entity.Visits);
    }

    static Visitor IDynamoDbDocument<Visitor>.ReadAttributes(ref DynamoDbReader reader)
    {
        var id = string.Empty;
        var visits = 0;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("pk"u8))
            {
                id = reader.ReadAttribute<string, StringConverter>();
            }
            else if (reader.AttributeNameEquals("visits"u8))
            {
                visits = reader.ReadAttribute<int, NumberConverter<int>>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Visitor(id, visits);
    }

    /// <summary>The key struct the generator emits.</summary>
    /// <param name="id">The id of the visitor.</param>
    public readonly struct Key(string id) : IKey<Visitor>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(id);
        }
    }
}
