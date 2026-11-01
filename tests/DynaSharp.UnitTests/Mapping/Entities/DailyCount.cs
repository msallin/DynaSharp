using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>What only updates write, with required properties that the key holds.</summary>
[DynamoDbEntity]
[Discriminator("DAILY")]
[PartitionKey("DAILY#{Path}")]
[SortKey("DAY#{Day}")]
internal sealed partial record DailyCount
{
    public required string Path { get; init; }

    public required DateOnly Day { get; init; }

    public long Count { get; init; }
}
