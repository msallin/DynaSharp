using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Sparse indexes whose other key is a key of the table: a global one in the attribute of the table, and a local one.</summary>
[DynamoDbEntity]
[Discriminator("SIBLING")]
[PartitionKey("A#{First}")]
[SortKey("B#{Second}")]
[GlobalIndexKey("ByMark", PartitionKey = "MARK#{Mark}", SortKey = "B#{Second}", SortKeyAttributeName = "sk", Sparse = true)]
[LocalIndexKey("ByNick", "NICK#{Nick}", Sparse = true)]
internal sealed partial record TableSibling
{
    public required string First { get; init; }

    public required string Second { get; init; }

    public string? Mark { get; init; }

    public string? Nick { get; init; }
}
