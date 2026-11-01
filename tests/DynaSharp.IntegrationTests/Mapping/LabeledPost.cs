using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Every post is in the local index, whose sort key is a literal: nothing about a post can keep it out.</summary>
[DynamoDbEntity]
[Discriminator("LABELED")]
[PartitionKey("LABELED#{Id}")]
[SortKey("DATA")]
[LocalIndexKey("LSI1", "ALL")]
internal sealed partial record LabeledPost
{
    public required string Id { get; init; }

    public string? Note { get; init; }
}
