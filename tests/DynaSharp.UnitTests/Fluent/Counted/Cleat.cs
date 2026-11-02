using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A plain key, which is read for what it is: an entity with its initial value here would be an entity with another key.</summary>
[DynamoDbEntity]
[Discriminator("CLEAT")]
internal sealed partial record Cleat
{
    [PartitionKey]
    [DynamoDbConverter(typeof(LenientPositionConverter))]
    public Position Side { get; init; } = Position.Unknown;
}
