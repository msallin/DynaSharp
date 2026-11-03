using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Every shape of a property whose converter can count a stored value as missing.</summary>
[DynamoDbEntity]
[Discriminator("MOORING")]
internal sealed partial record Mooring
{
    [PartitionKey]
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(BlankPayloadConverter))]
    public Payload? Payload { get; init; }

    [DynamoDbConverter(typeof(BlankPayloadConverter))]
#pragma warning disable DYNA035 // The tests pin that a read keeps the initializer, which is what DYNA035 warns of.
    public Payload? Seeded { get; init; } = new("seed");
#pragma warning restore DYNA035

    [DynamoDbConverter(typeof(LenientPositionConverter))]
    public Position Position { get; init; } = Position.Unknown;

    [DynamoDbConverter(typeof(LenientPositionConverter))]
    public Position? MaybePosition { get; init; }

    [DynamoDbConverter(typeof(ExplicitPositionConverter))]
    public Position Pinned { get; init; } = Position.Unknown;

    [DynamoDbConverter(typeof(ExplicitPositionConverter))]
    public Position? MaybePinned { get; init; }

    [DynamoDbConverter(typeof(SharedPositionConverter))]
    public Position Shared { get; init; } = Position.Unknown;

    [DynamoDbConverter(typeof(AnsweringPositionConverter))]
    public Position Answering { get; init; } = Position.Bow;

    [OmitEmpty]
    [DynamoDbConverter(typeof(DashTextConverter))]
    public string Text { get; init; } = "initial";

    [OmitEmpty]
    [DynamoDbConverter(typeof(DashTextConverter))]
    public string? MaybeText { get; init; }

    [OmitEmpty]
    [DynamoDbConverter(typeof(TornListConverter))]
    public List<string> Lines { get; init; } = ["initial"];

    [OmitEmpty]
    [DynamoDbConverter(typeof(TornSetConverter))]
    public HashSet<string> Labels { get; init; } = ["initial"];

    [DynamoDbConverter(typeof(EnvelopeConverter))]
    public Envelope? Mail { get; init; }

    public Tide Tide { get; init; } = Tide.High;

    public List<Tide> Tides { get; init; } = [];

    public HashSet<Tide> Seen { get; init; } = [];

    public Rigging? Rig { get; init; }

    [DynamoDbConverter(typeof(RefusingConverter))]
    public int Refused { get; init; }

    public int After { get; init; }
}
