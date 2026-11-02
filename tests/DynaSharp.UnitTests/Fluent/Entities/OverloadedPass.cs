using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Shares the index of <see cref="OverloadedBadge"/>, with a template of its own.</summary>
[DynamoDbEntity]
[Discriminator("PASS")]
[PartitionKey("PASS#{Id}")]
[GlobalIndexKey("GSI1", PartitionKey = "GATE#{Gate}")]
internal sealed partial record OverloadedPass
{
    public required string Id { get; init; }

    public required string Gate { get; init; }
}
