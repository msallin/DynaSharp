using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>An index with nothing to set, so every post is in it.</summary>
[DynamoDbEntity]
[Discriminator("PINNED")]
[PartitionKey("PINNED#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "PINNED", SortKey = "ANY")]
internal sealed partial record PinnedPost
{
    public required string Id { get; init; }

    public string? Note { get; init; }
}
