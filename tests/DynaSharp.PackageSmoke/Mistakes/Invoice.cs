using DynaSharp.Mapping;

namespace Mistakes;

[DynamoDbEntity]
[Discriminator("INVOICE")]
public partial class Invoice
{
    [PartitionKey]
    public required string Id { get; init; }
}

[DynamoDbEntity]
[Discriminator("INVOICE")]
public partial class Refund
{
    [PartitionKey]
    public required string Id { get; init; }
}
