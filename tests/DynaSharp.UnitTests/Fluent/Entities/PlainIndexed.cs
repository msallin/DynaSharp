using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An index whose partition key is the plain attribute of a property, so it has no key builder.</summary>
[DynamoDbEntity]
[Discriminator("PLAININDEXED")]
[PartitionKey("P#{Id}")]
[GlobalIndexKey("ByCode", PartitionKey = "{Code}", PartitionKeyAttributeName = "Code")]
internal sealed partial record PlainIndexed
{
    public required string Id { get; init; }

    public required string Code { get; init; }
}
