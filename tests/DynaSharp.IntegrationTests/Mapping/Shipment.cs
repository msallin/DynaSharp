using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A mapped entity over a table of strings <c>pk</c> and <c>sk</c>, with a value of each kind the store has to keep.</summary>
[DynamoDbEntity]
[Discriminator("SHIPMENT")]
internal sealed partial record Shipment
{
    [PartitionKey(AttributeName = "pk")]
    public required string Customer { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Number { get; init; }

    public required ShipmentState State { get; init; }

    public required decimal Weight { get; init; }

    public Guid? Tracking { get; init; }

    public string? Note { get; init; }

    public Depot? Destination { get; init; }

    public Leg? Route { get; init; }

    public IReadOnlyList<Parcel> Parcels { get; init; } = [];

    public HashSet<string> Tags { get; init; } = [];

    public HashSet<ShipmentState> History { get; init; } = [];

    public Dictionary<string, int> Counters { get; init; } = [];

    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    [Version]
    public long Revision { get; init; }
}
