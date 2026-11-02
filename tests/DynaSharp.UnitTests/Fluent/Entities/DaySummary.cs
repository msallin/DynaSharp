using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A key that cuts a timestamp off, and an index key of the same format, for which that is enough.</summary>
[DynamoDbEntity]
[Discriminator("DAYSUMMARY")]
[PartitionKey("S#{Id}")]
[SortKey("{At:yyyy-MM-dd}#{Seq:d4}")]
[GlobalIndexKey("ByDay", PartitionKey = "DAY#{At:yyyy-MM-dd}", SortKey = "{Id}")]
internal sealed partial record DaySummary
{
    public required string Id { get; init; }

    public required DateTimeOffset At { get; init; }

    public required int Seq { get; init; }

    public string? Note { get; init; }
}
