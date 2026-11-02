using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>An item that other code writes, with values that mean nothing to this type.</summary>
[DynamoDbEntity]
[Discriminator("CABIN")]
[PartitionKey("CABIN#{Id}")]
[SortKey("CABIN")]
public partial record Cabin
{
    public required string Id { get; init; }

    [DynamoDbConverter(typeof(BlankNoteConverter))]
    public CabinNote? Note { get; init; }

    [DynamoDbConverter(typeof(LenientDeckConverter))]
    public Deck Deck { get; init; } = Deck.Unknown;

    public int Berths { get; init; }
}
