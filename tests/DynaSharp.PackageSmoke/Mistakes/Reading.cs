using DynaSharp.Mapping;

namespace Mistakes;

// DYNA116: the generator reports it on the declaration.
[DynamoDbEntity]
[Discriminator("READING")]
[PartitionKey("READING#{Device}")]
[SortKey("{At}")]
public partial class Reading
{
    public required string Device { get; init; }

    public required DateTimeOffset At { get; init; }
}
