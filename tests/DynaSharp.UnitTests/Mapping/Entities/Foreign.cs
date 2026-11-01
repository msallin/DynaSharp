using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A table written by other tools: no discriminator is written or checked.</summary>
[DynamoDbEntity]
[Discriminator(OptOut = true)]
internal sealed partial class Foreign
{
    [PartitionKey(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
}
