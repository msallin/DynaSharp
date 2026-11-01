using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>The fourth type of a mix, a struct, which a mix holds without a box.</summary>
[DynamoDbEntity]
[Discriminator("DELTA")]
public partial record struct Delta
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }
}
