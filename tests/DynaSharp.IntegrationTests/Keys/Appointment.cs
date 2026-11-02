using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Keys;

/// <summary>An appointment under an id that holds the delimiter of its template, as ids of another system do, and a slot of a day that goes on behind the day.</summary>
[DynamoDbEntity]
[Discriminator("APPOINTMENT")]
[PartitionKey("appointment#{Key:raw}", AttributeName = "Id")]
[SortKey("SLOT#{Day:d3}#{Slot:raw}")]
internal sealed partial record Appointment
{
    public required string Key { get; init; }

    public required int Day { get; init; }

    public required string Slot { get; init; }

    public string? Note { get; init; }
}
