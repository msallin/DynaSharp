using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="OmitEmptyAttribute"/> on a type, with everything it passes over: an input of a key, a string that is not nullable, a nested type that says nothing.</summary>
[DynamoDbEntity]
[Discriminator("CARD")]
[PartitionKey("CARD#{Id}")]
[SortKey("CARD")]
[GlobalIndexKey("ByLabel", PartitionKey = "LABEL#{Label}", Sparse = true)]
[OmitEmpty]
internal sealed partial record Card
{
    public required string Id { get; init; }

    public string? Nick { get; init; }

    public string? Label { get; init; }

    public string Motto { get; init; } = "keep";

    public int Visits { get; init; }

    [DynamoDbConverter(typeof(MarkedConverter))]
    public string? Code { get; init; }

    public CardHome? Home { get; init; }

    public CardOffice? Office { get; init; }

    public IReadOnlyList<CardHome>? Homes { get; init; }
}
