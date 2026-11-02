using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Indexes that hold every item, because each key is a literal or a key of the table: a local one, and global ones on the partition key and on the sort key of the table.</summary>
[DynamoDbEntity]
[Discriminator("LABELED")]
[PartitionKey("LABEL#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("OnPartition", PartitionKey = "LABEL#{Id}", PartitionKeyAttributeName = "pk", SortKey = "ANY")]
[GlobalIndexKey("OnSort", PartitionKey = "EVERY", SortKey = "DATA", SortKeyAttributeName = "sk")]
[LocalIndexKey("ByLabel", "ALL")]
internal sealed partial record LabeledNote
{
    public required string Id { get; init; }

    public string? Note { get; init; }
}
