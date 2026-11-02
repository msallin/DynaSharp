using DynaSharp.Mapping;

namespace DynaSharp.DefaultsFixture;

/// <summary>An entity with a plain key, so the attribute of the key derives from its property, and a discriminator of its own, which wins over the default.</summary>
[DynamoDbEntity]
[Discriminator("GUEST")]
public sealed partial record Guest
{
    [PartitionKey]
    public required string GuestId { get; init; }

    public required string GuestName { get; init; }

    public string PreferredName { get; init; } = "none";

    public DateTimeOffset LastSeen { get; init; }

    public DateTimeOffset? FirstSeen { get; init; }

    public IReadOnlyList<DateTimeOffset> VisitDates { get; init; } = [];

    public HomeAddress? HomeAddress { get; init; }

    [DynamoDbAttribute("VIP")]
    public bool IsVip { get; init; }

    [OmitEmpty(false)]
    public string KeptNote { get; init; } = "none";
}
