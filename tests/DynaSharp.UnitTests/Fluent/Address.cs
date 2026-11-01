using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A nested document: it has no key, so it is no entity.</summary>
internal sealed record Address(string City) : IDynamoDbDocument<Address>
{
    static void IDynamoDbDocument<Address>.WriteAttributes(DynamoDbWriter writer, Address entity)
    {
        writer.WriteAttributeName("city"u8);
        writer.WriteString(entity.City);
    }

    static Address IDynamoDbDocument<Address>.ReadAttributes(ref DynamoDbReader reader)
    {
        var city = string.Empty;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("city"u8))
            {
                city = reader.ReadAttribute<string, StringConverter>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new Address(city);
    }
}
