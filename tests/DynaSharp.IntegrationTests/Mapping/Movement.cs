using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A stock movement, whose sort key holds a signed integer in the fixed format that sorts like the number.</summary>
[DynamoDbEntity]
[Discriminator("MOVEMENT")]
[PartitionKey("SKU#{Sku}")]
[SortKey("DELTA#{Delta:n6}#{Id}")]
internal sealed partial record Movement
{
    public required string Sku { get; init; }

    public required int Delta { get; init; }

    public required Guid Id { get; init; }
}
