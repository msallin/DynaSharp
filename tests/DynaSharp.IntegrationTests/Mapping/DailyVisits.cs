using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>What only updates write, with required properties that the key holds.</summary>
[DynamoDbEntity]
[Discriminator("DAILY_VISITS")]
[PartitionKey("VISITS#{Path}")]
[SortKey("DAY#{Day}")]
internal sealed partial record DailyVisits
{
    public required string Path { get; init; }

    public required DateOnly Day { get; init; }

    public long Count { get; init; }
}
