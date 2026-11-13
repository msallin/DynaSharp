using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Keys that a converter would leave out, which a put cannot do without.</summary>
[DynamoDbEntity]
[Discriminator("KEYED")]
#pragma warning disable DYNA126 // The converter writes the text the key holds, and only leaves a value out.
[SortKey("SHELF#{Shelf}")]
#pragma warning restore DYNA126
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
