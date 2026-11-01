using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Benchmarks;

// The item of ClientComparisonBenchmarks as a user would declare it, in the three variants that generator design section 6 estimates. The attribute named
// type is the discriminator, so it holds no string on the entity.
[DynamoDbDocument]
internal sealed partial record EntityAddress(
    [property: DynamoDbAttribute("street")] string Street,
    [property: DynamoDbAttribute("city")] string City,
    [property: DynamoDbAttribute("zip")] string Zip);

/// <summary>The keys as plain strings and the tags as an array, attribute by attribute what <see cref="HandWrittenOrder"/> writes by hand.</summary>
[DynamoDbEntity]
[Discriminator("Order", AttributeName = "type")]
internal sealed partial record EntityOrder
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
    public required EntityAddress Address { get; init; }
}

/// <summary>The same with the tags as the <see cref="HashSet{T}"/> that a set maps to by default.</summary>
[DynamoDbEntity]
[Discriminator("Order", AttributeName = "type")]
internal sealed partial record EntityOrderWithSet
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
    public required HashSet<string> Tags { get; init; }

    [DynamoDbAttribute("address")]
    public required EntityAddress Address { get; init; }
}

/// <summary>Template keys whose inputs live in the keys alone, so that a read parses a Guid and an instant instead of keeping two strings.</summary>
[DynamoDbEntity]
[Discriminator("Order", AttributeName = "type")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{CreatedAt:ticks}")]
internal sealed partial record EntityOrderWithTemplateKeys
{
    [KeyOnly]
    public required Guid UserId { get; init; }

    [KeyOnly]
    public required DateTimeOffset CreatedAt { get; init; }

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
    public required EntityAddress Address { get; init; }
}
