using System.Text.Json;
using DynaSharp.Mapping;

namespace DynaSharp.AotSmoke;

/// <summary>A document of a contract another team owns, held as JSON: <c>JsonElementConverter</c> and the bridge in a binary without reflection.</summary>
[DynamoDbEntity]
[Discriminator("SHIPMENT")]
[PartitionKey("SHIPMENT#{Id}")]
internal sealed partial record Shipment
{
    public required string Id { get; init; }

    public required JsonElement Label { get; init; }
}
