using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>An entity written by hand the way the generator will write it, in a table of strings <c>pk</c> and <c>sk</c>.</summary>
internal sealed record Profile(string Id, string FullName, int OrderCount = 0) : IDynamoDbEntity<Profile>
{
    private static readonly DynamoDbEntityInfo EntityInfo = new("PROFILE", "pk", "sk", []);

    static DynamoDbEntityInfo IDynamoDbEntity<Profile>.Info => EntityInfo;

    static void IDynamoDbEntity<Profile>.WriteKey(DynamoDbWriter writer, Profile entity) => Keys.Of(entity.Id).WriteKey(writer);

    static void IDynamoDbDocument<Profile>.WriteAttributes(DynamoDbWriter writer, Profile entity)
    {
        Keys.Of(entity.Id).WriteKey(writer);
        writer.WriteAttributeName("_type"u8);
        writer.WriteString("PROFILE"u8);
        writer.WriteAttributeName("id"u8);
        writer.WriteString(entity.Id);
        writer.WriteAttributeName("fullName"u8);
        writer.WriteString(entity.FullName);
        writer.WriteAttributeName("orderCount"u8);
        writer.WriteNumber(entity.OrderCount);
    }

    static Profile IDynamoDbDocument<Profile>.ReadAttributes(ref DynamoDbReader reader)
    {
        string? id = null, fullName = null;
        var orderCount = 0;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("id"u8))
            {
                id = reader.ReadAttribute<string, StringConverter>();
            }
            else if (reader.AttributeNameEquals("fullName"u8))
            {
                fullName = reader.ReadAttribute<string, StringConverter>();
            }
            else if (reader.AttributeNameEquals("orderCount"u8))
            {
                orderCount = reader.ReadAttribute<int, NumberConverter<int>>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Profile(id ?? throw DynamoDbMappingException.Missing(nameof(Profile), "id"), fullName ?? throw DynamoDbMappingException.Missing(nameof(Profile), "fullName"), orderCount);
    }

    /// <summary>The key builders the generator emits.</summary>
    public static class Keys
    {
        public static PrimaryKey Of(string id) => new("USER#" + id);

        public readonly struct PrimaryKey(string partitionKey) : IKey<Profile>
        {
            public void WriteKey(DynamoDbWriter writer)
            {
                writer.WriteAttributeName("pk"u8);
                writer.WriteString(partitionKey);
                writer.WriteAttributeName("sk"u8);
                writer.WriteString("PROFILE"u8);
            }
        }
    }

    /// <summary>The attribute handles the generator emits.</summary>
    public static class Attrs
    {
        public static readonly Attr<string, StringConverter> FullName = new("fullName");

        public static readonly Attr<int, NumberConverter<int>> OrderCount = new("orderCount");
    }
}
