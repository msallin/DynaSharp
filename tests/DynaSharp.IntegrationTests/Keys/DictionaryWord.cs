using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A plain sort key stored as a string.</summary>
[DynamoDbEntity]
[Discriminator("WORD")]
internal sealed partial record DictionaryWord
{
    [PartitionKey]
    public required string Language { get; init; }

    [SortKey]
    public required string Word { get; init; }
}
