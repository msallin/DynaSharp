using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>What a transaction writes beside a <see cref="Tally"/>, one per number it counted up to.</summary>
[DynamoDbEntity]
[Discriminator("TALLY_ENTRY")]
[PartitionKey("TALLY_ENTRIES")]
[SortKey("ENTRY#{Number:d8}")]
internal sealed partial record TallyEntry
{
    public required int Number { get; init; }
}
