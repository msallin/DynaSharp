using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An input that its converter stores as binary and a key holds as text.</summary>
[DynamoDbEntity]
[Discriminator("BINARY")]
[PartitionKey("B#{Id}")]
internal sealed partial record BinaryInput
{
    [DynamoDbConverter(typeof(GuidBytesConverter))]
    public required Guid Id { get; init; }

    public string? Note { get; init; }
}
