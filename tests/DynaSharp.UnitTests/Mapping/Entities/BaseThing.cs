using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbEntity]
[Discriminator("BASE")]
internal partial class BaseThing
{
    [PartitionKey]
    public string Id { get; set; } = "none";

    public string Shared { get; init; } = "base";
}
