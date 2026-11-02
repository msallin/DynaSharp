using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A version that the type insists on, which a read of an item without one still gives as 0.</summary>
[DynamoDbEntity]
[Discriminator("LABEL")]
[PartitionKey("LABEL#{Id}")]
internal sealed partial record RequiredLabel
{
    public required string Id { get; init; }

    [Version(Missing = VersionMissing.Allow)]
    public required long Revision { get; init; }
}
