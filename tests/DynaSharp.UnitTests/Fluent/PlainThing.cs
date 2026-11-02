using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A plain key: the attribute is the key, so nothing is left to store.</summary>
[DynamoDbEntity]
[Discriminator("PLAINTHING")]
internal sealed partial record PlainThing
{
    [PartitionKey]
    public required string Id { get; init; }

    public string? Note { get; init; }
}
