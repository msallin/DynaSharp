using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

[DynamoDbEntity]
[Discriminator("BASE")]
internal partial class BaseThing
{
    [PartitionKey]
    public string Id { get; set; } = string.Empty;

    public string Shared { get; init; } = "base";
}
