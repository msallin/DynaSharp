using DynaSharp.Mapping;

namespace Mistakes;

[DynamoDbEntity]
[Discriminator("SHIPMENT")]
[PartitionKey("SHIPMENT#{Id}")]
public partial class Shipment
{
    public required string Id { get; init; }

    public required string Carrier { get; init; }

    public decimal Weight { get; init; }
}
