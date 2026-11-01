using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>The nested document of <see cref="HandWrittenOrder"/>.</summary>
/// <param name="street">The street.</param>
/// <param name="city">The city.</param>
/// <param name="zip">The postal code.</param>
internal sealed class HandWrittenAddress(string street, string city, string zip) : IDynamoDbDocument<HandWrittenAddress>
{
    public string Street { get; } = street;

    public string City { get; } = city;

    public string Zip { get; } = zip;

    public static void WriteAttributes(DynamoDbWriter writer, HandWrittenAddress entity)
    {
        writer.WriteAttributeName("street"u8);
        writer.WriteString(entity.Street);
        writer.WriteAttributeName("city"u8);
        writer.WriteString(entity.City);
        writer.WriteAttributeName("zip"u8);
        writer.WriteString(entity.Zip);
    }

    public static HandWrittenAddress ReadAttributes(ref DynamoDbReader reader)
    {
        string street = null!, city = null!, zip = null!;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("street"u8))
            {
                street = reader.ReadAttribute<string, StringConverter>();
            }
            else if (reader.AttributeNameEquals("city"u8))
            {
                city = reader.ReadAttribute<string, StringConverter>();
            }
            else if (reader.AttributeNameEquals("zip"u8))
            {
                zip = reader.ReadAttribute<string, StringConverter>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return new HandWrittenAddress(street, city, zip);
    }
}
