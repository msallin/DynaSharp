using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>An input that its converter stores as binary and a key holds as text.</summary>
[DynamoDbEntity]
[Discriminator("BINARY")]
#pragma warning disable DYNA126 // The key holds the text of the Guid and the attribute its bytes, on purpose.
[PartitionKey("B#{Id}")]
#pragma warning restore DYNA126
internal sealed partial record BinaryInput
{
    [DynamoDbConverter(typeof(GuidBytesConverter))]
    public required Guid Id { get; init; }

    public string? Note { get; init; }
}
