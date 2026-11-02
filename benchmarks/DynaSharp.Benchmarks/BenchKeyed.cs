using DynaSharp.Mapping;

namespace DynaSharp.Benchmarks;

/// <summary>A key of strings, which is the kind whose values can fail.</summary>
[DynamoDbEntity]
[Discriminator("KEYED")]
[PartitionKey("TENANT#{Tenant}#USER#{User}")]
[SortKey("ORDER#{Order}")]
public sealed partial record BenchKeyed
{
    public required string Tenant { get; init; }

    public required string User { get; init; }

    public required string Order { get; init; }
}
