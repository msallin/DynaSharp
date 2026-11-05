using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>A generated entity with an attribute of time to live, whose info carries it to CreateTable and the fake.</summary>
[DynamoDbEntity]
[Discriminator("PASS")]
[PartitionKey("PASS#{Id}")]
internal sealed partial record Pass
{
    public required string Id { get; init; }

    public long Seq { get; init; }

    [TimeToLive]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
