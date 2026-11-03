using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An input of the key with an attribute of its own, which a read restores from the key when the attribute counts as missing.</summary>
[DynamoDbEntity]
[Discriminator("GUST")]
[PartitionKey("GUST#{Wind}")]
internal sealed partial record Gust
{
    public required Wind Wind { get; init; }
}
