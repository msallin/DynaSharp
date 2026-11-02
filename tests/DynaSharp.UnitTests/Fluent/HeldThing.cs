using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A property only a key holds, which has no attribute to store it in.</summary>
[DynamoDbEntity]
[Discriminator("HELDTHING")]
[PartitionKey("H#{Id}")]
internal sealed partial record HeldThing
{
    [KeyOnly]
    public required string Id { get; init; }

    public string? Note { get; init; }
}
