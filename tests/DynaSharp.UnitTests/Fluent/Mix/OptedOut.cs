using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>A type of a table written by other tools, which has no discriminator and so no place in a mix.</summary>
[DynamoDbEntity]
[Discriminator(OptOut = true)]
public partial record OptedOut
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }
}
