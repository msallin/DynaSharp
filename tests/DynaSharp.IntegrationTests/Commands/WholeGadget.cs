using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>The same table and indexes, declared without a projection, as before <c>Include</c>.</summary>
[DynamoDbEntity]
[Discriminator("DEVICE")]
[PartitionKey("DEVICE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("DeviceList", PartitionKey = "ALL", SortKey = "{Name}")]
[LocalIndexKey("ByFirmware", "{Firmware}", Sparse = true, Include = [nameof(Name)])]
internal sealed partial record WholeGadget
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Firmware { get; init; }
}
