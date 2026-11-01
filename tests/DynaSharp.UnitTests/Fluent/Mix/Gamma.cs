using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>The third type of a mix.</summary>
[DynamoDbEntity]
[Discriminator("GAMMA")]
public partial record Gamma
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }
}
