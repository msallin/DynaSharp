using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Everything an update can owe at its end: a key it derives, and an index of literals.</summary>
[DynamoDbEntity]
[Discriminator("BEACON")]
[PartitionKey("B#{Id}")]
[GlobalIndexKey("ByState", PartitionKey = "STATE#{State}")]
[GlobalIndexKey("Everything", PartitionKey = "ALL")]
internal sealed partial record Beacon
{
    public required string Id { get; init; }

    public string State { get; init; } = "on";
}
