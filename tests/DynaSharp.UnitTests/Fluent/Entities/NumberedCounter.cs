using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A plain number as the key of the table, which an index repeats.</summary>
[DynamoDbEntity]
[Discriminator("COUNTER")]
[GlobalIndexKey("ByKind", PartitionKey = "KIND#{Kind}#{Id:d4}")]
internal sealed partial record NumberedCounter
{
    [PartitionKey]
    public required int Id { get; init; }

    public required string Kind { get; init; }
}
