using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>The second type of a mix, with a required attribute of its own, so that an item that reaches the wrong type fails.</summary>
[DynamoDbEntity]
[Discriminator("BETA")]
public partial record Beta
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }

    [SortKey(AttributeName = "sk")]
    public required string Sk { get; init; }

    public required int Amount { get; init; }
}
