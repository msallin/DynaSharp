using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A template of three inputs, one of them only a key holds, so an update that sets one input lacks one it can set and one it cannot.</summary>
[DynamoDbEntity]
[Discriminator("HALFHIDDEN")]
[PartitionKey("{Id}")]
[GlobalIndexKey("Partly", PartitionKey = "{A}-{B}-{Secret}")]
internal sealed partial record HalfHiddenKey
{
    public required string Id { get; init; }

    public required string A { get; init; }

    public required string B { get; init; }

    [KeyOnly]
    public required string Secret { get; init; }
}
