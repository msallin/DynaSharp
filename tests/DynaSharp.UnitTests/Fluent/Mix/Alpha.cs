using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>The first type of a mix, mapped by the generator as a consumer's would be.</summary>
[DynamoDbEntity]
[Discriminator("ALPHA")]
public partial record Alpha
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }

    public string? Name { get; init; }
}
