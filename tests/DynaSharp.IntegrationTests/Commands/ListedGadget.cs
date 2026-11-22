using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>A device in a list that copies only its name, so a write copies little into the index and a list page reads little.</summary>
[DynamoDbEntity]
[Discriminator("DEVICE")]
[PartitionKey("DEVICE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("DeviceList", PartitionKey = "ALL", SortKey = "{Name}", Include = [nameof(Name)])]
[LocalIndexKey("ByFirmware", "{Firmware}", Sparse = true, Include = [nameof(Name)])]
internal sealed partial record ListedGadget
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Firmware { get; init; }
}
