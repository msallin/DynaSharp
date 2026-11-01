using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Value types in a class, so a read allocates exactly the object.</summary>
[DynamoDbEntity]
[Discriminator("GAUGE")]
internal sealed partial class Gauge
{
    [PartitionKey]
    public long Id { get; set; }

    public double Low { get; set; }

    public decimal High { get; set; }

    public Status Mode { get; set; }
}
