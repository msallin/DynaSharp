using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA240: two entities of one declared table, of which the chain in <c>Code</c> creates the table of one.</summary>
[DynamoDbEntity(Table = "shelves")]
[Discriminator("SHELF")]
[PartitionKey("SHELF#{Id}")]
public partial class Shelf
{
    public required string Id { get; init; }
}

/// <summary>The entity the chain leaves out.</summary>
[DynamoDbEntity(Table = "shelves")]
[Discriminator("BIN")]
[PartitionKey("BIN#{Id}")]
public partial class Bin
{
    public required string Id { get; init; }
}
