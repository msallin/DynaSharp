using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="OmitEmptyAttribute"/> on a type with a property that a read cannot do without, which stays written.</summary>
[DynamoDbEntity]
[Discriminator("NOTE")]
[PartitionKey("NOTE#{Id}")]
[OmitEmpty]
internal sealed partial record CardNote
{
    public required string Id { get; init; }

    public required string Text { get; init; }
}
