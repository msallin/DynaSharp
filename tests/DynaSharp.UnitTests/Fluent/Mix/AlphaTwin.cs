using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>A type with the discriminator of <see cref="Alpha"/>, so that a mix of the two cannot tell their items apart.</summary>
[DynamoDbEntity]
[Discriminator("ALPHA")]
public partial record AlphaTwin
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }
}
