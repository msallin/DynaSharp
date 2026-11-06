using System.Text.Json;
using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A document of a contract that someone else owns, stored as the JSON it is: alone, nullable, in a list and in a nested document.</summary>
[DynamoDbEntity]
[Discriminator("CONSIGNMENT")]
public sealed partial record Consignment
{
    [PartitionKey]
    public required string Id { get; init; }

    public required JsonElement Label { get; init; }

    public JsonElement? Note { get; init; }

    public IReadOnlyList<JsonElement>? Parts { get; init; }

    public ConsignmentStop? Stop { get; init; }
}
