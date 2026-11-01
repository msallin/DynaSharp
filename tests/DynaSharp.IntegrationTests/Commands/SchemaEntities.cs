using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Commands;

// One table of three entities. GSI1 is overloaded: three templates over one pair of attributes. Each entity brings an index of its own, a local one
// among them, and two keys of indexes are plain numbers, so that a type in the schema shows at the emulator.
[DynamoDbEntity]
[Discriminator("PURCHASE")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("PURCHASE#{Number:d6}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{PlacedAt}")]
[GlobalIndexKey("ByParcel", PartitionKey = "PARCEL#{Parcel}", Sparse = true)]
[LocalIndexKey("ByTotal", "TOTAL#{Total:n10.2}")]
[LocalIndexKey("ByLines", "{Lines}", SortKeyAttributeName = "Lines")]
internal sealed partial record Purchase
{
    public required string CustomerId { get; init; }

    public required int Number { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset PlacedAt { get; init; }

    public required decimal Total { get; init; }

    public required int Lines { get; init; }

    public string? Parcel { get; init; }
}

[DynamoDbEntity]
[Discriminator("SHOPPER")]
[PartitionKey("CUSTOMER#{Id}")]
[SortKey("PROFILE")]
[GlobalIndexKey("GSI1", PartitionKey = "TIER#{Tier}", SortKey = "{Name}")]
[GlobalIndexKey("ByEmail", PartitionKey = "EMAIL#{Email}")]
internal sealed partial record Shopper
{
    public required string Id { get; init; }

    public required string Tier { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }
}

[DynamoDbEntity]
[Discriminator("ARTICLE")]
[PartitionKey("ARTICLE#{Sku}")]
[SortKey("DETAILS")]
[GlobalIndexKey("GSI1", PartitionKey = "CATEGORY#{Category}", SortKey = "{Sku}")]
[GlobalIndexKey("ByStock", PartitionKey = "{Category}", PartitionKeyAttributeName = "Category", SortKey = "{Stock}", SortKeyAttributeName = "Stock")]
internal sealed partial record Article
{
    public required string Sku { get; init; }

    public required string Category { get; init; }

    public required int Stock { get; init; }
}

/// <summary>A table whose keys are a number and a binary value, written by the keys and not by a template.</summary>
[DynamoDbEntity]
[Discriminator("SAMPLE")]
internal sealed partial record Reading
{
    [PartitionKey]
    public required long Sensor { get; init; }

    [SortKey]
    public required byte[] Stamp { get; init; }

    public required int Value { get; init; }
}
