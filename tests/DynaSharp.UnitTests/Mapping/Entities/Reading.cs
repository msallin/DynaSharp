using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Only value types, so reading it needs no heap at all.</summary>
[DynamoDbEntity]
[Discriminator("READING")]
internal partial struct Reading
{
    [PartitionKey]
    public long Sensor { get; set; }

    public double Value { get; set; }

    public decimal Calibrated { get; set; }

    public bool Valid { get; set; }

    public Guid Run { get; set; }

    public DateTimeOffset At { get; set; }

    public DateOnly Day { get; set; }

    public TimeSpan Duration { get; set; }

    public Status Status { get; set; }

    public Access Rights { get; set; }

    public int? Offset { get; set; }

    public Status? Previous { get; set; }
}
