using DynaSharp.Mapping;

namespace Mistakes;

// DYNA237: a time without a kind, which DateTimeConverter refuses when the entity is written.
[DynamoDbEntity]
[Discriminator("SHIFT")]
public partial class Shift
{
    [PartitionKey]
    public required string Id { get; init; }

    public DateTime Start { get; init; }
}
