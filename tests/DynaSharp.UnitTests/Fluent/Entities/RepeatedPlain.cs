using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A plain key that a template of the table repeats, beside an input with an attribute of its own.</summary>
[DynamoDbEntity]
[Discriminator("REPEATED")]
[SortKey("S#{Id}#{Day}")]
internal sealed partial record RepeatedPlain
{
    [PartitionKey]
    public required string Id { get; init; }

    public required DateOnly Day { get; init; }

    public string? Note { get; init; }
}
