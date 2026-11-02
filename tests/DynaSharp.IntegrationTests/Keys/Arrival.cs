using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>An arrival in an index by its day, which a getter makes of the timestamp.</summary>
[DynamoDbEntity]
[Discriminator("ARRIVAL")]
[PartitionKey("ARRIVAL#{Id}")]
[GlobalIndexKey("ByDay", PartitionKey = "DAY#{Day}", SortKey = "{Id}")]
internal sealed partial record Arrival
{
    public required string Id { get; init; }

    public required DateTimeOffset At { get; init; }

    [ComputedFrom(nameof(At))]
    public DateOnly Day => DateOnly.FromDateTime(At.UtcDateTime);
}
