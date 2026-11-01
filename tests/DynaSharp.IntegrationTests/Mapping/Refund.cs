using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A refund, in the partition of its customer, and in the partitions of the same global index as the orders: an overloaded index, which a mixed read reads as both.</summary>
[DynamoDbEntity]
[Discriminator("REFUND")]
[PartitionKey("CUSTOMER#{CustomerId}")]
[SortKey("REFUND#{RefundId}")]
[GlobalIndexKey("GSI1", PartitionKey = "STATUS#{Status}", SortKey = "{Total:n10.2}")]
internal sealed partial record Refund
{
    public required string CustomerId { get; init; }

    public required string RefundId { get; init; }

    public required OrderStatus Status { get; init; }

    public required decimal Total { get; init; }
}
