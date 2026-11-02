using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An index whose keys are made of computed properties and of a property beside them, in a template and alone.</summary>
[DynamoDbEntity]
[Discriminator("VISIT")]
[PartitionKey("VISIT#{Id}")]
[GlobalIndexKey("ByArrival", PartitionKey = "SITE#{Site}", SortKey = "{Arrival}#{AppointmentKey}")]
[GlobalIndexKey("ByLoad", PartitionKey = "LOAD#{Load}")]
internal sealed partial record Visit
{
    public required string Id { get; init; }

    public required string Site { get; init; }

    public required string AppointmentKey { get; init; }

    public required VisitMessage Message { get; init; }

    public int Guests { get; init; }

    public HashSet<string> Tags { get; init; } = [];

    public string? Note { get; init; }

    [ComputedFrom(nameof(Message))]
    public DateTimeOffset Arrival => Message.Arrival;

    [ComputedFrom(nameof(Guests), nameof(Tags))]
    public int Load => Guests + Tags.Count;
}
