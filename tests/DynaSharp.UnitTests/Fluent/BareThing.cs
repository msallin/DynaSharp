using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A template of one placeholder over the attribute of its property, which is a plain key.</summary>
[DynamoDbEntity]
[Discriminator("BARETHING")]
[PartitionKey("{Id}", AttributeName = "Id")]
internal sealed partial record BareThing
{
    public required string Id { get; init; }

    public string? Note { get; init; }
}
