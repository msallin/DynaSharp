using DynaSharp.Mapping;

namespace DynaSharp.DefaultsFixture;

/// <summary>An entity whose enum takes the default converter of the assembly, which can count a stored value as missing.</summary>
[DynamoDbEntity]
public sealed partial record Room
{
    [PartitionKey]
    public required string RoomNumber { get; init; }

    public RoomState State { get; init; } = RoomState.Unknown;

    public RoomState? PriorState { get; init; }

    public IReadOnlyList<RoomState> History { get; init; } = [];
}
