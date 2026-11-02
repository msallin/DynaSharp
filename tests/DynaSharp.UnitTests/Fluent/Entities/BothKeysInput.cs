using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An index key that repeats an input of each key of the table.</summary>
[DynamoDbEntity]
[Discriminator("BOTHKEYS")]
[PartitionKey("A#{First}")]
[SortKey("B#{Second}")]
[GlobalIndexKey("ByMark", PartitionKey = "{Mark}-{First}-{Second}")]
internal sealed partial record BothKeysInput
{
    public required string First { get; init; }

    public required string Second { get; init; }

    public required string Mark { get; init; }
}
