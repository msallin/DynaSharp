using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Plain keys of the table, and sparse indexes over them: one with a template of its own, one that shares the attribute.</summary>
[DynamoDbEntity]
[Discriminator("PLAIN")]
[GlobalIndexKey("ByRack", PartitionKey = "RACK#{Rack}", SortKey = "SERIAL#{Serial}", Sparse = true)]
[GlobalIndexKey("ByShelf", PartitionKey = "SHELF#{Shelf}", SortKey = "{Serial}", SortKeyAttributeName = "Serial", Sparse = true)]
internal sealed partial record PlainDevice
{
    [PartitionKey]
    public required string Serial { get; init; }

    public string? Rack { get; init; }

    public string? Shelf { get; init; }
}
