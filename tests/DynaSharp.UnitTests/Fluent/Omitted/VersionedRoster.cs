using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A version beside a property that a converter leaves out, which goes through the writer of the next version.</summary>
[DynamoDbEntity]
[Discriminator("VERSIONED_ROSTER")]
internal sealed partial record VersionedRoster
{
    [PartitionKey]
    public required string Id { get; init; }

    [Version]
    public long Revision { get; init; }

    [DynamoDbConverter(typeof(OmittedListConverter))]
    public List<string> Tags { get; init; } = [];
}
