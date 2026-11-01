using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent.Mix;

/// <summary>A type that keeps its discriminator in an attribute of another name, which fits a mix only with types that do the same.</summary>
[DynamoDbEntity]
[Discriminator("KINDED", AttributeName = "kind")]
public partial record Kinded
{
    [PartitionKey(AttributeName = "pk")]
    public required string Pk { get; init; }
}
