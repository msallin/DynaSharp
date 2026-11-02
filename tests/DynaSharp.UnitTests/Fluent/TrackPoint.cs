using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>One timestamp in two keys of the table, one of which holds all of it.</summary>
[DynamoDbEntity]
[Discriminator("POINT")]
[PartitionKey("D#{At:yyyy-MM-dd}")]
[SortKey("{At:ticks}#{Id}")]
internal sealed partial record TrackPoint
{
    public required DateTimeOffset At { get; init; }

    public required string Id { get; init; }

    public string? Note { get; init; }
}
