using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity that is a struct, with a version wider than what a condition holds.</summary>
[DynamoDbEntity]
[Discriminator("GAUGE")]
[PartitionKey("GAUGE#{Id}")]
internal partial record struct Gauge
{
    public required string Id { get; init; }

    [Version]
    public ulong Revision { get; init; }
}
