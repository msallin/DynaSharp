using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>An item beside the readings of a meter, whose key sorts above theirs.</summary>
[DynamoDbEntity]
[Discriminator("ZONE")]
[PartitionKey("METER#{Meter}")]
[SortKey("ZONE#{Zone}")]
internal sealed partial record MeterZone
{
    public required string Meter { get; init; }

    public required string Zone { get; init; }
}
