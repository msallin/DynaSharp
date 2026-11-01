using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>The order mapped by hand, the way generated code does (generator design, section 4): dispatch on the name length, unknown attributes skipped. It is what the generated types are measured against.</summary>
internal sealed class HandWrittenOrder : IDynamoDbEntity<HandWrittenOrder>
{
    public static DynamoDbEntityInfo Info { get; } = new("ORDER", "pk", "sk", []);

    public required string Pk { get; init; }

    public required string Sk { get; init; }

    public required string Type { get; init; }

    public required long OrderId { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }

    public required bool Shipped { get; init; }

    public required string[] Tags { get; init; }

    public required HandWrittenAddress Address { get; init; }

    public static void WriteKey(DynamoDbWriter writer, HandWrittenOrder entity)
    {
        writer.WriteAttributeName("pk"u8);
        writer.WriteString(entity.Pk);
        writer.WriteAttributeName("sk"u8);
        writer.WriteString(entity.Sk);
    }

    public static void WriteAttributes(DynamoDbWriter writer, HandWrittenOrder entity)
    {
        WriteKey(writer, entity);
        writer.WriteAttributeName("type"u8);
        writer.WriteString(entity.Type);
        writer.WriteAttributeName("orderId"u8);
        writer.WriteNumber(entity.OrderId);
        writer.WriteAttributeName("total"u8);
        writer.WriteNumber(entity.Total);
        writer.WriteAttributeName("currency"u8);
        writer.WriteString(entity.Currency);
        writer.WriteAttributeName("shipped"u8);
        writer.WriteBool(entity.Shipped);
        writer.WriteAttributeName("giftNote"u8);
        writer.WriteNull();
        writer.WriteAttributeName("tags"u8);
        writer.WriteStartStringSet();
        foreach (var tag in entity.Tags)
        {
            writer.WriteStringSetElement(tag);
        }

        writer.WriteEndSet();
        writer.WriteAttributeName("address"u8);
        writer.WriteStartMap();
        HandWrittenAddress.WriteAttributes(writer, entity.Address);
        writer.WriteEndMap();
    }

    public static HandWrittenOrder ReadAttributes(ref DynamoDbReader reader)
    {
        string pk = null!, sk = null!, type = null!, currency = null!;
        string[] tags = [];
        HandWrittenAddress address = null!;
        long orderId = 0;
        decimal total = 0;
        var shipped = false;
        while (reader.TryReadAttributeName())
        {
            var name = reader.GetAttributeNameUtf8();
            switch (name.Length)
            {
                case 2 when name.SequenceEqual("pk"u8):
                    pk = reader.ReadAttribute<string, StringConverter>();
                    continue;
                case 2 when name.SequenceEqual("sk"u8):
                    sk = reader.ReadAttribute<string, StringConverter>();
                    continue;
                case 4 when name.SequenceEqual("type"u8):
                    type = reader.ReadAttribute<string, StringConverter>();
                    continue;
                case 4 when name.SequenceEqual("tags"u8):
                    tags = reader.ReadAttribute<string[], SetArrayConverter<string, StringSetElement>>();
                    continue;
                case 5 when name.SequenceEqual("total"u8):
                    total = reader.ReadAttribute<decimal, NumberConverter<decimal>>();
                    continue;
                case 7 when name.SequenceEqual("orderId"u8):
                    orderId = reader.ReadAttribute<long, NumberConverter<long>>();
                    continue;
                case 7 when name.SequenceEqual("shipped"u8):
                    shipped = reader.ReadAttribute<bool, BoolConverter>();
                    continue;
                case 7 when name.SequenceEqual("address"u8):
                    address = reader.ReadAttribute<HandWrittenAddress, DocumentConverter<HandWrittenAddress>>();
                    continue;
                case 8 when name.SequenceEqual("currency"u8):
                    currency = reader.ReadAttribute<string, StringConverter>();
                    continue;
            }

            reader.SkipAttributeValue();
        }

        return new HandWrittenOrder { Pk = pk, Sk = sk, Type = type, OrderId = orderId, Total = total, Currency = currency, Shipped = shipped, Tags = tags, Address = address };
    }

    /// <summary>A generated key: the type argument is fixed by the table, so the key struct is inferred and costs no string.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="sk">The sort key.</param>
    public readonly struct Key(string pk, string sk) : IKey<HandWrittenOrder>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(pk);
            writer.WriteAttributeName("sk"u8);
            writer.WriteString(sk);
        }
    }
}
