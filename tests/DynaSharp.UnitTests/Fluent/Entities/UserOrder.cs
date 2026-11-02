using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A key of two templates over stored properties, with a version.</summary>
[DynamoDbEntity]
[Discriminator("PURCHASE")]
[PartitionKey("USER#{UserId}")]
[SortKey("ORDER#{OrderId}")]
internal sealed partial record UserOrder
{
    [DynamoDbAttribute("userId")]
    public required string UserId { get; init; }

    [DynamoDbAttribute("orderId")]
    public required Guid OrderId { get; init; }

    public int Lines { get; init; }

    [Version]
    public long Revision { get; init; }
}
