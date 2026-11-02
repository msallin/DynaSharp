using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A computed input of the key of the table, which an update can follow even less than one of an index.</summary>
[DynamoDbEntity]
[Discriminator("SHIFT")]
[PartitionKey("SHIFT#{Code}")]
internal sealed partial record Shift
{
    public required string Name { get; init; }

    public string? Note { get; init; }

    [ComputedFrom(nameof(Name))]
    public string Code => Name.ToUpperInvariant();
}
