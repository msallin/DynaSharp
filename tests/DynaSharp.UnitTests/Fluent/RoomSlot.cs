using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A timestamp in the sort key, which a key holds to the minute: one slot per room and minute. The index repeats that format, so a parsed key gives what it needs.</summary>
[DynamoDbEntity]
[Discriminator("SLOT")]
[PartitionKey("ROOM#{Room}")]
[SortKey("AT#{Start:yyyy-MM-ddTHH:mm}")]
[GlobalIndexKey("ByOwner", PartitionKey = "OWNER#{Owner}", SortKey = "{Start:yyyy-MM-ddTHH:mm}")]
internal sealed partial record RoomSlot
{
    public required string Room { get; init; }

    public required DateTimeOffset Start { get; init; }

    public required string Owner { get; init; }
}
