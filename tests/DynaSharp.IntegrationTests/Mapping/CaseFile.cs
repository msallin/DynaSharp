using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A sparse index whose sort key repeats the table key, so a file enters the index when it gets a handler.</summary>
[DynamoDbEntity]
[Discriminator("CASE_FILE")]
[PartitionKey("CASE#{Id}")]
[SortKey("DATA")]
[GlobalIndexKey("GSI2", PartitionKey = "HANDLER#{Handler}", SortKey = "CASE#{Id}", Sparse = true)]
internal sealed partial record CaseFile
{
    public required string Id { get; init; }

    public string? Handler { get; init; }
}
