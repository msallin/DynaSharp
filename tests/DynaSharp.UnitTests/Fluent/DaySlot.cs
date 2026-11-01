using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An index wants more of a timestamp than the table key holds, so only a generated key knows it.</summary>
[DynamoDbEntity]
[Discriminator("DAYSLOT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("DAY#{Start:yyyy-MM-dd}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}#AT#{Start:ticks}")]
internal sealed partial record DaySlot
{
    public required string Room { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required string Owner { get; init; }
}
