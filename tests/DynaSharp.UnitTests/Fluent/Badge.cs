using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Computed inputs with an attribute of their own, in a template and as a plain key of an index.</summary>
[DynamoDbEntity]
[Discriminator("BADGE")]
[PartitionKey("BADGE#{Id}")]
[GlobalIndexKey("ByLabel", PartitionKey = "LABEL#{Label}#{Kind}")]
[GlobalIndexKey("ByCode", PartitionKey = "{Code}", PartitionKeyAttributeName = "code")]
internal sealed partial record Badge
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Kind { get; init; } = "guest";

    [DynamoDbAttribute("label")]
    [ComputedFrom(nameof(Name))]
    public string Label => Name.Trim();

    [DynamoDbAttribute("code")]
    [ComputedFrom(nameof(Name))]
    public string Code => Name.ToUpperInvariant();
}
