using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A device in a list sorted by a bare placeholder over an attribute of the index, so the key is the value and nothing else.</summary>
[DynamoDbEntity]
[Discriminator("DEVICE")]
[PartitionKey("DEVICE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("DeviceList", PartitionKey = "ALL", SortKey = "{ListOrder}")]
internal sealed partial record ListedDevice
{
    public required string Id { get; init; }

    public required string ListOrder { get; init; }
}
