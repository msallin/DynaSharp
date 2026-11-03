using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An entity of what a table that the AWS SDK and other code wrote holds, read through the opt-in converters of the library, each named in <c>[DynamoDbConverter]</c>.</summary>
[DynamoDbEntity]
[Discriminator("REMITTANCE")]
internal sealed partial record Remittance
{
    /// <summary>Gets what <see cref="DueAt"/> holds before a read touches it.</summary>
    public static DateTimeOffset Initial { get; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [PartitionKey]
    public required string Id { get; init; }

    [SortKey]
    [DynamoDbConverter(typeof(SdkDateTimeConverter))]
    public required DateTime PaidAt { get; init; }

    [DynamoDbConverter(typeof(BoolAsNumberConverter))]
    public bool Settled { get; init; }

    [DynamoDbConverter(typeof(BoolAsNumberConverter))]
    public bool? Disputed { get; init; }

    [DynamoDbConverter(typeof(SdkDateTimeConverter))]
    public DateTime? RefundedAt { get; init; }

    [DynamoDbConverter(typeof(OffsetTimestampConverter))]
    public DateTimeOffset LocalTime { get; init; }

    [DynamoDbConverter(typeof(EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>))]
    public DateTimeOffset? ShippedAt { get; init; }

    [DynamoDbConverter(typeof(EmptyStringAsMissingConverter<DateTimeOffset, OffsetTimestampConverter>))]
    public DateTimeOffset DueAt { get; init; } = Initial;

    [DynamoDbConverter(typeof(EmptyStringAsMissingConverter<Position, LenientPositionConverter>))]
    public Position Position { get; init; } = Position.Unknown;

    public int After { get; init; }
}
