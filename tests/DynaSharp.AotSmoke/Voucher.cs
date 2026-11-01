using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>A generated entity with a version and key templates: a delegate per template and a second write path, which AOT has to keep.</summary>
[DynamoDbEntity]
[Discriminator("VOUCHER")]
[PartitionKey("VOUCHER#{Id}")]
[GlobalIndexKey("ByPriority", PartitionKey = "PRIORITY#{Priority}", SortKey = "{Rank:d3}-{Campaign}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", Sparse = true)]
internal sealed partial record Voucher
{
    public required string Id { get; init; }

    public Priority Priority { get; init; }

    public int Rank { get; init; }

    public string Campaign { get; init; } = "main";

    public string? Owner { get; init; }

    [Version]
    public long Revision { get; init; }
}
