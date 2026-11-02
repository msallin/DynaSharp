using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>A raw value, whose delimiter comes back from the text of the key.</summary>
[DynamoDbEntity]
[Discriminator("RAWSLOT")]
[PartitionKey("slot#{Key:raw}")]
internal sealed partial record RawSlot
{
    public required string Key { get; init; }

    public string? Note { get; init; }
}
