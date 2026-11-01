using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A required property in a format that loses it.</summary>
[DynamoDbEntity]
[Discriminator("MONTHLY")]
[PartitionKey("SUMMARY#{Name}")]
[SortKey("MONTH#{At:yyyy-MM}")]
internal sealed partial record MonthlySummary
{
    public required string Name { get; init; }

    public required DateTimeOffset At { get; init; }

    public int Total { get; init; }
}
