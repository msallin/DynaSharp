using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// An entity written by hand the way the generator will write it: explicit members, a key struct that holds the parts of the key, a
/// discriminator that a read checks.
/// </summary>
internal sealed record Customer(string Id, string FullName, int OrderCount = 0, string? Email = null) : IDynamoDbEntity<Customer>
{
    static DynamoDbEntityInfo IDynamoDbEntity<Customer>.Info { get; } = new(
        "CUSTOMER",
        "pk",
        "sk",
        [new DynamoDbIndexInfo("ByEmail", "email", null, Sparse: true), new DynamoDbIndexInfo("ByName", "fullName", "pk", Sparse: false)]);

    static void IDynamoDbEntity<Customer>.WriteKey(DynamoDbWriter writer, Customer entity) => new Keys.PrimaryKey(entity.Id).WriteKey(writer);

    static void IDynamoDbDocument<Customer>.WriteAttributes(DynamoDbWriter writer, Customer entity)
    {
        new Keys.PrimaryKey(entity.Id).WriteKey(writer);
        writer.WriteAttributeName("_type"u8);
        writer.WriteString("CUSTOMER"u8);
        writer.WriteAttributeName("fullName"u8);
        writer.WriteString(entity.FullName);
        writer.WriteAttributeName("orderCount"u8);
        writer.WriteNumber(entity.OrderCount);
        if (entity.Email is { } email)
        {
            writer.WriteAttributeName("email"u8);
            writer.WriteString(email);
        }
    }

    static Customer IDynamoDbDocument<Customer>.ReadAttributes(ref DynamoDbReader reader)
    {
        string? pk = null, fullName = null, email = null;
        var orderCount = 0;
        while (reader.TryReadAttributeName())
        {
            var name = reader.GetAttributeNameUtf8();
            if (name.SequenceEqual("pk"u8))
            {
                pk = reader.ReadAttribute<string, StringConverter>();
            }
            else if (name.SequenceEqual("fullName"u8))
            {
                fullName = reader.ReadAttribute<string, StringConverter>();
            }
            else if (name.SequenceEqual("orderCount"u8))
            {
                orderCount = reader.ReadAttribute<int, NumberConverter<int>>();
            }
            else if (name.SequenceEqual("email"u8))
            {
                email = reader.ReadAttribute<string, StringConverter>();
            }
            else if (name.SequenceEqual("_type"u8))
            {
                var type = reader.ReadAttribute<string, StringConverter>();
                if (type != "CUSTOMER")
                {
                    throw new DynamoDbMappingException(nameof(Customer), "_type");
                }
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Customer(
            pk?.Replace("CUSTOMER#", string.Empty, StringComparison.Ordinal) ?? throw DynamoDbMappingException.Missing(nameof(Customer), "pk"),
            fullName ?? throw DynamoDbMappingException.Missing(nameof(Customer), "fullName"),
            orderCount,
            email);
    }

    /// <summary>The key builders the generator emits.</summary>
    public static class Keys
    {
        public static PrimaryKey Of(string id) => new(id);

        public readonly struct PrimaryKey(string id) : IKey<Customer>
        {
            public void WriteKey(DynamoDbWriter writer)
            {
                writer.WriteAttributeName("pk"u8);
                writer.WriteString("CUSTOMER#" + id);
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

        public static readonly Attr<string, StringConverter> Email = new("email");
    }
}
