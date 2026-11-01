using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A key of an index that is not sparse, made of a timestamp the table key holds in part.</summary>
[DynamoDbEntity]
[Discriminator("OTHERSLOT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("DAY#{Start:yyyy-MM-dd}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "AT#{Start}")]
internal sealed partial record OtherSlot
{
    public required string Room { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required string Owner { get; init; }
}
