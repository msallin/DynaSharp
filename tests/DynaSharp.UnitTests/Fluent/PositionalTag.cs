using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A version that a constructor without a default takes, which a read of an item without one still gives as 0.</summary>
[DynamoDbEntity]
[Discriminator("TAG")]
[PartitionKey("TAG#{Id}")]
internal sealed partial record PositionalTag(string Id, [property: Version(Missing = VersionMissing.Allow)] long Revision);
