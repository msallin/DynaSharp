using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.DefaultsFixture;

/// <summary>An entity whose <see cref="bool"/>, <see cref="DateTime"/> and <see cref="Guid"/> take the opt-in converters of the library through defaults of the assembly, as a table the AWS SDK wrote holds them.</summary>
[DynamoDbEntity(Table = "payments")]
public sealed partial record Payment
{
    [PartitionKey]
    public required string PaymentId { get; init; }

    public bool Settled { get; init; }

    public DateTime PaidAt { get; init; }

    public Guid? Receipt { get; init; }

    public IReadOnlyList<Guid> Refunds { get; init; } = [];

    [DynamoDbConverter(typeof(OffsetTimestampConverter))]
    public DateTimeOffset LocalTime { get; init; }
}
