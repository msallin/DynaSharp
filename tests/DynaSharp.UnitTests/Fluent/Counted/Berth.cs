using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An input of the key with an attribute of its own, which a read restores from the key when the attribute counts as missing.</summary>
[DynamoDbEntity]
[Discriminator("BERTH")]
[PartitionKey("BERTH#{Side}")]
internal sealed partial record Berth
{
    [DynamoDbConverter(typeof(LenientPositionConverter))]
    public required Position Side { get; init; }
}
