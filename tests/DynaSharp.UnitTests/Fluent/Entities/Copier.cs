using DynaSharp.Mapping;

namespace DynaSharp.UnitTests.Fluent;

internal enum CopierLevel
{
    Low,
    High,
}

/// <summary>
/// A generated entity whose sparse indexes have a key that is the stored text of an attribute, of each kind an update copies: a string under an attribute name of its own, shared by two indexes, an enum by name,
/// a timestamp in <c>ticks</c>, a <see cref="Guid"/> and a <see cref="DateOnly"/>. The last index pads a number, which is no copy.
/// </summary>
[DynamoDbEntity]
[Discriminator("COPIER")]
[PartitionKey("COPIER#{Id}")]
[GlobalIndexKey("ByOwner", PartitionKey = "{Owner}", SortKey = "LIVE#{LiveSince}", Sparse = true)]
[GlobalIndexKey("ByDue", PartitionKey = "{Owner}", PartitionKeyAttributeName = "byownerpk", SortKey = "DUE#{Due}", Sparse = true)]
[GlobalIndexKey("ByLevel", PartitionKey = "LEVEL#{Mark}", SortKey = "{Level}", Sparse = true)]
[GlobalIndexKey("ByAt", PartitionKey = "AT#{Tag}", SortKey = "{At}", Sparse = true)]
[GlobalIndexKey("ByRef", PartitionKey = "{Ref}", SortKey = "REF#{RefMark}", Sparse = true)]
[GlobalIndexKey("ByDay", PartitionKey = "{Day}", SortKey = "DAY#{DayMark}", Sparse = true)]
[GlobalIndexKey("ByCount", PartitionKey = "{Count:d6}", SortKey = "COUNT#{CountMark}", Sparse = true)]
internal sealed partial record Copier
{
    public required string Id { get; init; }

    [DynamoDbAttribute("owner")]
    public string? Owner { get; init; }

    public DateTimeOffset? LiveSince { get; init; }

    public DateOnly? Due { get; init; }

    public string? Mark { get; init; }

    public CopierLevel? Level { get; init; }

    public string? Tag { get; init; }

    public DateTime? At { get; init; }

    public Guid? Ref { get; init; }

    public string? RefMark { get; init; }

    public DateOnly? Day { get; init; }

    public string? DayMark { get; init; }

    public int? Count { get; init; }

    public string? CountMark { get; init; }
}
