using DynaSharp.Mapping;

namespace Mistakes;

[DynamoDbEntity]
[Discriminator("ORDER")]
public partial class Order
{
    [PartitionKey]
    public required string Id { get; init; }

    public decimal Total { get; init; }
}
