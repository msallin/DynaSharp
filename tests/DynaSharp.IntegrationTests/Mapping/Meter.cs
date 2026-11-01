using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A struct entity without a sort key, whose plain key keeps the name of its property.</summary>
[DynamoDbEntity]
[Discriminator("METER")]
internal partial struct Meter
{
    [PartitionKey(AttributeName = "pk")]
    public string Id { get; set; }

    public long Count { get; set; }

    public bool Active { get; set; }
}
