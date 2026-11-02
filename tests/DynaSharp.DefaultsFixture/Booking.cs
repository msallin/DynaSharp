using DynaSharp.Mapping;

namespace DynaSharp.DefaultsFixture;

/// <summary>An entity without a discriminator, with template keys under the frozen names and a sparse index whose input stays written.</summary>
[DynamoDbEntity]
[PartitionKey("BOOKING#{BookingId}")]
[SortKey("AT#{CheckIn:ticks}")]
[GlobalIndexKey("ByRoom", PartitionKey = "ROOM#{RoomNumber}", Sparse = true)]
public sealed partial record Booking
{
    public required string BookingId { get; init; }

    public required DateTimeOffset CheckIn { get; init; }

    public string? RoomNumber { get; init; }

    public DateTimeOffset BookedAt { get; init; }

    public HashSet<string> SpecialRequests { get; init; } = [];
}
