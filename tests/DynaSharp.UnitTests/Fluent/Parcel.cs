using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity on a table that keeps items written before it had a version, so version 0 stands for such an item.</summary>
[DynamoDbEntity]
[Discriminator("PARCEL")]
[PartitionKey("PARCEL#{Id}")]
internal sealed partial record Parcel
{
    public required string Id { get; init; }

    public string? Note { get; init; }

    [Version(Missing = VersionMissing.Allow)]
    public int Revision { get; init; }
}
