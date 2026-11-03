using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA031: an entity stored inside another one, with keys of its own that nothing reads.</summary>
[DynamoDbEntity]
[Discriminator("BASKET")]
[PartitionKey("BASKET#{Id}")]
public partial class Basket
{
    public required string Id { get; init; }

    public Order? LastOrder { get; init; }
}
