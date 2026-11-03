using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Every shape of a property of an enum with <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c>, with no converter of its own.</summary>
[DynamoDbEntity]
[Discriminator("LOGBOOK")]
internal sealed partial record Logbook
{
    [PartitionKey]
    public required string Id { get; init; }

    public Wind Wind { get; init; } = Wind.Unknown;

    public Wind Bare { get; init; }

    public Wind? MaybeWind { get; init; }

#pragma warning disable DYNA035 // The tests pin that a read keeps the initializer, which is what DYNA035 warns of.
    public Wind? SeededWind { get; init; } = Wind.Unknown;
#pragma warning restore DYNA035

    public List<Wind> Winds { get; init; } = [];

    public HashSet<Wind> Seen { get; init; } = [];

    public Dictionary<string, Wind> ByHour { get; init; } = [];

    public Sail? Sail { get; init; }

    public int After { get; init; }
}
