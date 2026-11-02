using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="OmitEmptyAttribute"/> on a property, beside one without.</summary>
[DynamoDbEntity]
[Discriminator("SLIP")]
[PartitionKey("SLIP#{Id}")]
[SortKey("SLIP")]
internal sealed partial record Slip
{
    public required string Id { get; init; }

    [OmitEmpty]
    public string? Note { get; init; }

    public string? Comment { get; init; }
}
