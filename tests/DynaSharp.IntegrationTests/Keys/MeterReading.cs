using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>A reading of a meter, in a key of a timestamp, an index of a signed number, one of a string that ends the key and a local one of digits.</summary>
[DynamoDbEntity]
[Discriminator("READING")]
[PartitionKey("METER#{Meter}")]
[SortKey("AT#{At}#{Sequence:d3}")]
[GlobalIndexKey("ByValue", PartitionKey = "VALUES#{Meter}", SortKey = "{Value:n6.2}")]
[GlobalIndexKey("ByLabel", PartitionKey = "LABELS#{Meter}", SortKey = "LABEL#{Label}")]
[LocalIndexKey("ByDigits", "DIGITS#{Digits:d4}#{Sequence:d3}")]
internal sealed partial record MeterReading
{
    public required string Meter { get; init; }

    public required DateTimeOffset At { get; init; }

    public required int Sequence { get; init; }

    public required decimal Value { get; init; }

    public required string Label { get; init; }

    public required int Digits { get; init; }
}
