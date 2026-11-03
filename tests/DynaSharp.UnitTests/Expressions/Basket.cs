using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Expressions;

/// <summary>One property per shape of handle that <c>contains</c> takes.</summary>
[DynamoDbEntity]
[Discriminator("BASKET")]
[PartitionKey("BASKET#{Id}")]
[SortKey("BASKET")]
internal sealed partial record Basket
{
    public required string Id { get; init; }

    public HashSet<string> Tags { get; init; } = [];

    public HashSet<string>? MaybeTags { get; init; }

    [OmitEmpty]
    public HashSet<string> OmittedTags { get; init; } = [];

    [OmitEmpty]
    public HashSet<string>? MaybeOmittedTags { get; init; }

    public IReadOnlySet<long> Numbers { get; init; } = new HashSet<long>();

    public IReadOnlySet<long>? MaybeNumbers { get; init; }

    [OmitEmpty]
    public IReadOnlySet<long> OmittedNumbers { get; init; } = new HashSet<long>();

    [OmitEmpty]
    public IReadOnlySet<long>? MaybeOmittedNumbers { get; init; }

    [DynamoDbConverter(typeof(SetArrayConverter<Guid, GuidSetElement>))]
    public Guid[] Ids { get; init; } = [];

    [DynamoDbConverter(typeof(SetArrayConverter<Guid, GuidSetElement>))]
    public Guid[]? MaybeIds { get; init; }

    [OmitEmpty]
    [DynamoDbConverter(typeof(SetArrayConverter<Guid, GuidSetElement>))]
    public Guid[] OmittedIds { get; init; } = [];

    [OmitEmpty]
    [DynamoDbConverter(typeof(SetArrayConverter<Guid, GuidSetElement>))]
    public Guid[]? MaybeOmittedIds { get; init; }

    public HashSet<byte[]> Blobs { get; init; } = [];

    public HashSet<decimal> Prices { get; init; } = [];

    public HashSet<Shade> Shades { get; init; } = [];

    public List<string> Lines { get; init; } = [];

    public List<string>? MaybeLines { get; init; }

    [OmitEmpty]
    public List<string> OmittedLines { get; init; } = [];

    [OmitEmpty]
    public List<string>? MaybeOmittedLines { get; init; }

    public IReadOnlyList<int> Counts { get; init; } = [];

    public IReadOnlyList<int>? MaybeCounts { get; init; }

    [OmitEmpty]
    public IReadOnlyList<int> OmittedCounts { get; init; } = [];

    [OmitEmpty]
    public IReadOnlyList<int>? MaybeOmittedCounts { get; init; }

    public IList<bool> Flags { get; init; } = [];

    public IList<bool>? MaybeFlags { get; init; }

    [OmitEmpty]
    public IList<bool> OmittedFlags { get; init; } = [];

    [OmitEmpty]
    public IList<bool>? MaybeOmittedFlags { get; init; }

    public Shade[] Palette { get; init; } = [];

    public Shade[]? MaybePalette { get; init; }

    [OmitEmpty]
    public Shade[] OmittedPalette { get; init; } = [];

    [OmitEmpty]
    public Shade[]? MaybeOmittedPalette { get; init; }

    public List<DateOnly> Days { get; init; } = [];

    public string Name { get; init; } = string.Empty;

    public string? Nickname { get; init; }

    [OmitEmpty]
    public string Note { get; init; } = string.Empty;

    [OmitEmpty]
    public string? Remark { get; init; }
}
