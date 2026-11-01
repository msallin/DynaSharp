using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A sparse index with a plain key beside a template.</summary>
[DynamoDbEntity]
[Discriminator("HALF")]
[PartitionKey("{Id}")]
[GlobalIndexKey("Half", PartitionKey = "H#{Tag}", SortKey = "{Mark}", SortKeyAttributeName = "Mark", Sparse = true)]
internal sealed partial record HalfPlain
{
    public required string Id { get; init; }

    public string? Tag { get; init; }

    public string? Mark { get; init; }
}
