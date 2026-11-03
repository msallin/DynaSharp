using DynaSharp.Mapping;

namespace Mistakes;

/// <summary>DYNA032: a getter that reads more than its [ComputedFrom] names, so an update of the last name leaves the index behind.</summary>
[DynamoDbEntity]
[Discriminator("PERSON")]
[PartitionKey("PERSON#{Id}")]
[GlobalIndexKey("ByName", PartitionKey = "NAME#{Full}")]
public partial class Person
{
    public required string Id { get; init; }

    public required string First { get; init; }

    public required string Last { get; init; }

    [ComputedFrom(nameof(First))]
    public string Full => First + " " + Last;
}
