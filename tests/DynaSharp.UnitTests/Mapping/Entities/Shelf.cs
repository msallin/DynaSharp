using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A sort key that is a plain number, with a property named like a keyword, and every shape of set.</summary>
[DynamoDbEntity]
[Discriminator("SHELF")]
internal sealed partial class Shelf
{
    [PartitionKey]
    public string Id { get; set; } = string.Empty;

    [SortKey]
    public int Rack { get; set; }

    public HashSet<Access> Flags { get; set; } = [];

    public IReadOnlySet<int> Numbers { get; set; } = new HashSet<int>();

    public HashSet<byte[]> Blobs { get; set; } = [];

    public HashSet<Guid> Ids { get; set; } = [];

    [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))]
    public string[] Names { get; set; } = [];

    public HashSet<string>? Optional { get; set; }
}
