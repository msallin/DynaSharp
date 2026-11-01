using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>A second type with its discriminator in <c>kind</c>, of a table without a sort key.</summary>
[DynamoDbEntity]
[Discriminator("KINDED2", AttributeName = "kind")]
public partial record KindedToo
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }
}
