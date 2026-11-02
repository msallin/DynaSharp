using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A <c>required</c> member, which has nothing to fall back on.</summary>
[DynamoDbEntity]
[Discriminator("HULL")]
internal sealed partial record Hull
{
    [PartitionKey]
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(LenientPositionConverter))]
    public required Position Position { get; init; }
}
