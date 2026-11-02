using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A plain sort key stored as a number.</summary>
[DynamoDbEntity]
[Discriminator("TICK")]
internal sealed partial record CounterTick
{
    [PartitionKey]
    public required string Stream { get; init; }

    [SortKey]
    public required long Number { get; init; }
}
