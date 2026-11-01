using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A score whose own attributes are the keys of an index, so nothing extra is stored and the sort key of the index is a DynamoDB number.</summary>
[DynamoDbEntity]
[Discriminator("SCORE")]
[GlobalIndexKey("ByPoints", PartitionKey = "{Game}", PartitionKeyAttributeName = "Game", SortKey = "{Points}", SortKeyAttributeName = "Points")]
internal sealed partial record Score
{
    [PartitionKey(AttributeName = "pk")]
    public required string Player { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Round { get; init; }

    public required string Game { get; init; }

    public required int Points { get; init; }
}
