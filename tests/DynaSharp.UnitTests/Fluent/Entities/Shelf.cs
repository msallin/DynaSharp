using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A property that feeds a template itself and a computed input of another one.</summary>
[DynamoDbEntity]
[Discriminator("SHELF")]
[PartitionKey("SHELF#{Id}")]
[GlobalIndexKey("BySite", PartitionKey = "SITE#{Site}")]
[GlobalIndexKey("ByRegion", PartitionKey = "REGION#{Region}")]
internal sealed partial record Shelf
{
    public required string Id { get; init; }

    public required string Site { get; init; }

    [ComputedFrom(nameof(Site))]
    public string Region => Site[..2];
}
