using DynaSharp.Mapping;

namespace Mistakes;

// DYNA116: the generator reports it on the declaration, as {At:ms} cuts the value off.
[DynamoDbEntity]
[Discriminator("READING")]
[PartitionKey("READING#{Device}")]
[SortKey("{At:ms}")]
public partial class Reading
{
    public required string Device { get; init; }

    public required DateTimeOffset At { get; init; }
}
