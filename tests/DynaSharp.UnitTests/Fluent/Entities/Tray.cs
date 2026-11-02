using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><see cref="OmitEmptyAttribute"/> on every shape it applies to, nullable or not, beside what takes it back.</summary>
[DynamoDbEntity]
[Discriminator("TRAY")]
[PartitionKey("TRAY#{Id}")]
internal sealed partial record Tray
{
    public required string Id { get; init; }

    [OmitEmpty]
    public string Title { get; init; } = "untitled";

    [OmitEmpty]
    public List<string> Tags { get; init; } = [];

    [OmitEmpty]
    public List<string>? MaybeTags { get; init; }

    [OmitEmpty]
    public IReadOnlyList<int> Scores { get; init; } = [];

    [OmitEmpty]
    public IList<int> Marks { get; init; } = [];

    [OmitEmpty]
    public HashSet<string> Labels { get; init; } = [];

    [OmitEmpty]
    public HashSet<string>? MaybeLabels { get; init; }

    [OmitEmpty]
    public IReadOnlySet<string> Frozen { get; init; } = new HashSet<string>();

    [OmitEmpty]
    public Dictionary<string, int> Counts { get; init; } = [];

    [OmitEmpty]
    public IReadOnlyDictionary<string, int>? Totals { get; init; }

    [OmitEmpty]
    public string[] Names { get; init; } = [];

    [OmitEmpty]
    public byte[] Blob { get; init; } = [];

    [OmitEmpty]
    public byte[]? MaybeBlob { get; init; }

    [OmitEmpty]
    [DynamoDbConverter(typeof(SetArrayConverter<string, StringSetElement>))]
    public string[] Aliases { get; init; } = [];

    [OmitEmpty(false)]
    public List<string> Kept { get; init; } = [];

    public IReadOnlyList<TrayShelf> Shelves { get; init; } = [];
}
