using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A template of many inputs, one of them only a key holds, so a typed update can never complete it.</summary>
[DynamoDbEntity]
[Discriminator("WIDE")]
[PartitionKey("{Id}")]
[GlobalIndexKey("Wide", PartitionKey = "{A}-{B}-{C}-{D}-{E}-{F}")]
[GlobalIndexKey("Hidden", PartitionKey = "{G}-{Secret}")]
internal sealed partial record WideKey
{
    public required string Id { get; init; }

    public required string A { get; init; }

    public required string B { get; init; }

    public required string C { get; init; }

    public required string D { get; init; }

    public required string E { get; init; }

    public required string F { get; init; }

    public required string G { get; init; }

    [KeyOnly]
    public required string Secret { get; init; }
}
