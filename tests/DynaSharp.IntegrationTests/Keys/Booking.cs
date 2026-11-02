using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A key made of two stored properties.</summary>
[DynamoDbEntity]
[Discriminator("BOOKING")]
[PartitionKey("USER#{UserId}")]
[SortKey("BOOKING#{BookingId}")]
internal sealed partial record Booking
{
    public required string UserId { get; init; }

    public required Guid BookingId { get; init; }

    public string? Note { get; init; }
}
