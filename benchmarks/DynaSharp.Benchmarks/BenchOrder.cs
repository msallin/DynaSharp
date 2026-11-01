using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

/// <summary>The item of the benchmarks, mapped by the generator. The null in <c>giftNote</c> of the canned items is read and, like every null, not written.</summary>
[DynamoDbEntity]
[Discriminator("Order", AttributeName = "type")]
internal sealed partial class BenchOrder
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }

    [DynamoDbAttribute("orderId")]
    public required long OrderId { get; init; }

    [DynamoDbAttribute("total")]
    public required decimal Total { get; init; }

    [DynamoDbAttribute("currency")]
    public required string Currency { get; init; }

    [DynamoDbAttribute("shipped")]
    public required bool Shipped { get; init; }

    [DynamoDbAttribute("giftNote")]
    public string? GiftNote { get; init; }

    [DynamoDbAttribute("tags")]
    [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))]
    public required string[] Tags { get; init; }

    [DynamoDbAttribute("address")]
    public required BenchAddress Address { get; init; }

    /// <summary>A key as the generator will emit it: the type argument is fixed by the table, so the key struct is inferred and costs no string.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="sk">The sort key.</param>
    public readonly struct Key(string pk, string sk) : IKey<BenchOrder>
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
