using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Shares the index of <see cref="OverloadedPass"/>, which is what overloading it means.</summary>
[DynamoDbEntity]
[Discriminator("BADGE")]
[PartitionKey("BADGE#{Id}")]
[GlobalIndexKey("GSI1", PartitionKey = "HOLDER#{Holder}")]
internal sealed partial record OverloadedBadge
{
    public required string Id { get; init; }

    public required string Holder { get; init; }
}
