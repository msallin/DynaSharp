using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>In the index once it has a title, and every post has one, which is what the literal says.</summary>
[DynamoDbEntity]
[Discriminator("LISTED")]
[PartitionKey("LISTED#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI1", PartitionKey = "ALL", SortKey = "{Title}")]
internal sealed partial record ListedPost
{
    public required string Id { get; init; }

    public string Title { get; init; } = "untitled";

    public string? Note { get; init; }
}
