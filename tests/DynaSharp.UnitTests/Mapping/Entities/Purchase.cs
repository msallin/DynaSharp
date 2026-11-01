using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Every kind of property at once: what a real entity looks like. Its keys are plain.</summary>
[DynamoDbEntity]
[Discriminator("PURCHASE")]
[GlobalIndexKey("ByStatus", PartitionKey = "{Status}", PartitionKeyAttributeName = "Status")]
internal sealed partial record Purchase
{
    [PartitionKey(AttributeName = "pk")]
    public required string Customer { get; init; }

    [SortKey(AttributeName = "sk")]
    public required int Number { get; init; }

    public required decimal Total { get; init; }

    public required Status Status { get; init; }

    public Guid? Tracking { get; init; }

    public Place? ShipTo { get; init; }

    public IReadOnlyList<Line> Lines { get; init; } = [];

    public HashSet<string> Tags { get; init; } = [];

    public HashSet<Status> Stages { get; init; } = [];

    public Dictionary<string, int> Counters { get; init; } = [];

    public Access Rights { get; init; }

    [DynamoDbConverter(typeof(CentsConverter))]
    public Cents Fee { get; init; }

    [DynamoDbConverter(typeof(CentsConverter))]
    public Cents? Tip { get; init; }

    [DynamoDbAttribute("ttl")]
    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }

    [DynamoDbAttribute("items")]
    public int ItemCount => Lines.Count;

    [Version]
    public long Revision { get; init; }

    [DynamoDbIgnore]
    public string Scratch { get; init; } = "not stored";
}
