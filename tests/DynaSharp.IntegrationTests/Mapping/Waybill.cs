using System.Text.Json;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>A document whose schema belongs to someone else, held as the JSON it is.</summary>
[DynamoDbEntity]
[Discriminator("WAYBILL")]
[PartitionKey("WAYBILL#{Id}")]
[SortKey("WAYBILL")]
public partial record Waybill
{
    public required string Id { get; init; }

    public required JsonElement Document { get; init; }

    public JsonElement? Remark { get; init; }
}
