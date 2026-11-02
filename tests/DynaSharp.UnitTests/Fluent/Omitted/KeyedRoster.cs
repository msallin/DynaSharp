using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Keys that a converter would leave out, which a put cannot do without.</summary>
[DynamoDbEntity]
[Discriminator("KEYED")]
[SortKey("SHELF#{Shelf}")]
internal sealed partial record KeyedRoster
{
    [PartitionKey]
    [DynamoDbConverter(typeof(ExplicitZeroConverter))]
    public required int Id { get; init; }

    [DynamoDbConverter(typeof(NoneOmittedConverter))]
    public required string Shelf { get; init; }

    [DynamoDbConverter(typeof(ExplicitZeroConverter))]
    public int Other { get; init; }
}
