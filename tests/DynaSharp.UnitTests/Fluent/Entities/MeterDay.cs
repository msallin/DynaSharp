using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A key that cuts a timestamp off at the day, which a converter stores as a number.</summary>
[DynamoDbEntity]
[Discriminator("METERDAY")]
[PartitionKey("METER#{Meter}")]
[SortKey("{At:yyyy-MM-dd}#{Seq:d4}")]
internal sealed partial record MeterDay
{
    public required string Meter { get; init; }

    [DynamoDbConverter(typeof(EpochSecondsConverter))]
    public required DateTimeOffset At { get; init; }

    public required int Seq { get; init; }

    public string? Note { get; init; }
}
