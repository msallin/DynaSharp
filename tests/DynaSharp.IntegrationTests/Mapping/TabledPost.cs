using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Every post is in a global index on the partition key of the table, whose sort key is a literal.</summary>
[DynamoDbEntity]
[Discriminator("TABLED")]
[PartitionKey("TABLED#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey(ShopSandbox.ByPartitionAndLabel, PartitionKey = "TABLED#{Id}", PartitionKeyAttributeName = "pk", SortKey = "ALL", SortKeyAttributeName = "labelsk")]
internal sealed partial record TabledPost
{
    public required string Id { get; init; }

    public string? Note { get; init; }
}
