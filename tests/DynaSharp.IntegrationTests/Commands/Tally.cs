using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>A number that writers count up with a read and a conditional write, for tests of writers that meet on one item.</summary>
[DynamoDbEntity]
[Discriminator("TALLY")]
[PartitionKey("TALLY#{Id}")]
[SortKey("TOTAL")]
internal sealed partial record Tally
{
    public required string Id { get; init; }

    public int Count { get; init; }

    [Version]
    public long Revision { get; init; }
}
